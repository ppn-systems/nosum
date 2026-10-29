// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Infrastructure.Identity;

/// <summary>
/// Checks an OpenID Connect ID token (docs/adr/ADR-069 D3): an RS256 signature from the provider's published keys, then the claims that bind the token
/// to this server: its audience, its expiry and start time, and the issuer the provider's rules accept. Built on the BCL only; no token library is added.
/// Only <c>RS256</c> is accepted, so <c>none</c> and the HMAC algorithms are refused.
/// </summary>
internal sealed class IdTokenValidator
{
    /// <summary>Clock skew tolerated on <c>exp</c> and <c>nbf</c>.</summary>
    internal static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    private readonly JwksKeySet _keys;
    private readonly TimeProvider _time;

    /// <summary>Initializes the validator.</summary>
    /// <param name="keys">The provider's signing keys.</param>
    /// <param name="time">The clock for the expiry checks.</param>
    internal IdTokenValidator(JwksKeySet keys, TimeProvider time)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>Validates a token.</summary>
    /// <param name="token">The compact JWS.</param>
    /// <param name="audience">The client id the token must be issued to.</param>
    /// <param name="issuerAccepted">Decides from the payload whether the issuer, and any claim that ties it to the provider, is acceptable.</param>
    /// <param name="cancellationToken">Cancels the key fetch.</param>
    /// <returns>The payload when every check passes (the caller disposes it); otherwise null.</returns>
    /// <exception cref="System.Net.Http.HttpRequestException">The provider's keys could not be fetched.</exception>
    internal async Task<JsonDocument?> ValidateAsync(string token, string audience, Func<JsonElement, bool> issuerAccepted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        string[] parts = token.Split('.');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || parts[2].Length == 0)
        {
            return null;
        }

        using JsonDocument? header = ParseSegment(parts[0]);
        JsonDocument? payload = ParseSegment(parts[1]);
        if (header is null || payload is null)
        {
            payload?.Dispose();
            return null;
        }

        if (await IsValidAsync(parts, header.RootElement, payload.RootElement, audience, issuerAccepted, cancellationToken).ConfigureAwait(false))
        {
            return payload;
        }

        payload.Dispose();
        return null;
    }

    private async Task<bool> IsValidAsync(string[] parts, JsonElement header, JsonElement claims, string audience, Func<JsonElement, bool> issuerAccepted, CancellationToken cancellationToken)
    {
        if (!HasString(header, "alg", "RS256") || !header.TryGetProperty("kid", out JsonElement kid) || kid.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        RSAParameters? keyParameters = await _keys.FindAsync(kid.GetString()!, cancellationToken).ConfigureAwait(false);
        byte[]? signature = DecodeSegment(parts[2]);
        if (keyParameters is null || signature is null)
        {
            return false;
        }

        byte[] signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        using RSA key = RSA.Create(keyParameters.Value);
        if (!key.VerifyData(signingInput, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            return false;
        }

        DateTimeOffset now = _time.GetUtcNow();
        if (!HasAudience(claims, audience)
            || !claims.TryGetProperty("exp", out JsonElement exp) || !exp.TryGetInt64(out long expSeconds)
            || now > DateTimeOffset.FromUnixTimeSeconds(expSeconds) + ClockSkew)
        {
            return false;
        }

        if (claims.TryGetProperty("nbf", out JsonElement nbf) && nbf.TryGetInt64(out long nbfSeconds)
            && now < DateTimeOffset.FromUnixTimeSeconds(nbfSeconds) - ClockSkew)
        {
            return false;
        }

        return issuerAccepted(claims);
    }

    private static JsonDocument? ParseSegment(string segment)
    {
        byte[]? bytes = DecodeSegment(segment);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static byte[]? DecodeSegment(string segment)
    {
        if (!Base64Url.IsValid(segment))
        {
            return null;
        }

        return Base64Url.DecodeFromChars(segment);
    }

    // The audience claim is one string or a list; the token is for this server when its list names the client id.
    private static bool HasAudience(JsonElement claims, string audience)
    {
        if (!claims.TryGetProperty("aud", out JsonElement aud))
        {
            return false;
        }

        if (aud.ValueKind == JsonValueKind.String)
        {
            return aud.GetString() == audience;
        }

        if (aud.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement item in aud.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() == audience)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasString(JsonElement element, string property, string expected)
        => element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;
}
