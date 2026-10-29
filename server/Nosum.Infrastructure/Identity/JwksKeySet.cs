// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Infrastructure.Identity;

/// <summary>
/// The RSA signing keys a provider publishes at its JWKS address, cached for an hour (docs/adr/ADR-069 D3). A key id the cache does not know
/// triggers one refetch, at most once a minute, so a forged key id cannot make the server fetch on every request.
/// </summary>
internal sealed class JwksKeySet
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinimumRefetchInterval = TimeSpan.FromMinutes(1);

    private readonly HttpClient _http;
    private readonly Uri _address;
    private readonly TimeProvider _time;
    private volatile KeySnapshot? _snapshot;

    /// <summary>Initializes the key set.</summary>
    /// <param name="http">The client that fetches the keys.</param>
    /// <param name="address">The provider's JWKS address.</param>
    /// <param name="time">The clock for cache ages.</param>
    internal JwksKeySet(HttpClient http, Uri address, TimeProvider time)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>Finds the public key a token names.</summary>
    /// <param name="keyId">The token header's <c>kid</c>.</param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    /// <returns>The key, or null when the provider does not publish that key id.</returns>
    /// <exception cref="HttpRequestException">The key set could not be fetched.</exception>
    /// <exception cref="JsonException">The key set is not valid JSON of the expected shape.</exception>
    internal async Task<RSAParameters?> FindAsync(string keyId, CancellationToken cancellationToken)
    {
        KeySnapshot? snapshot = _snapshot;
        DateTimeOffset now = _time.GetUtcNow();
        if (snapshot is not null && now - snapshot.FetchedAt < Lifetime && snapshot.Keys.TryGetValue(keyId, out RSAParameters known))
        {
            return known;
        }

        // Unknown or stale: fetch again, but not more often than the minimum interval.
        if (snapshot is not null && now - snapshot.FetchedAt < MinimumRefetchInterval)
        {
            return null;
        }

        KeySnapshot fresh = await FetchAsync(cancellationToken).ConfigureAwait(false);
        _snapshot = fresh;
        return fresh.Keys.TryGetValue(keyId, out RSAParameters found) ? found : null;
    }

    private async Task<KeySnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.GetAsync(_address, cancellationToken).ConfigureAwait(false);
        _ = response.EnsureSuccessStatusCode();
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        using JsonDocument document = JsonDocument.Parse(body);
        Dictionary<string, RSAParameters> keys = new(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("keys", out JsonElement list))
        {
            foreach (JsonElement jwk in list.EnumerateArray())
            {
                if (TryReadRsaSigningKey(jwk, out string kid, out RSAParameters parameters))
                {
                    keys[kid] = parameters;
                }
            }
        }

        return new KeySnapshot(keys, _time.GetUtcNow());
    }

    // Only RSA keys meant for signatures, with an id; a key with another algorithm or use is skipped, not trusted by default.
    private static bool TryReadRsaSigningKey(JsonElement jwk, out string kid, out RSAParameters parameters)
    {
        kid = string.Empty;
        parameters = default;
        if (!IsString(jwk, "kty", "RSA") || !IsStringOrAbsent(jwk, "use", "sig") || !IsStringOrAbsent(jwk, "alg", "RS256"))
        {
            return false;
        }

        if (!jwk.TryGetProperty("kid", out JsonElement kidElement) || kidElement.ValueKind != JsonValueKind.String
            || !jwk.TryGetProperty("n", out JsonElement n) || n.ValueKind != JsonValueKind.String
            || !jwk.TryGetProperty("e", out JsonElement e) || e.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            RSAParameters candidate = new()
            {
                Modulus = Base64Url.DecodeFromChars(n.GetString()!),
                Exponent = Base64Url.DecodeFromChars(e.GetString()!),
            };

            // A key the runtime rejects is skipped now, rather than failing every token later.
            using RSA probe = RSA.Create(candidate);
            kid = kidElement.GetString()!;
            parameters = candidate;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    private static bool IsString(JsonElement element, string property, string expected)
        => element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static bool IsStringOrAbsent(JsonElement element, string property, string expected)
        => !element.TryGetProperty(property, out _) || IsString(element, property, expected);

    private sealed record KeySnapshot(IReadOnlyDictionary<string, RSAParameters> Keys, DateTimeOffset FetchedAt);
}
