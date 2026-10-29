// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Identity;
using Nosum.Contracts.Account;
using Nosum.Domain.Accounts;

namespace Nosum.Infrastructure.Identity;

/// <summary>
/// Verifies Google ID tokens (docs/adr/ADR-069): signed by Google's published keys, issued to the configured Google client id, from Google's issuer.
/// The subject (<c>sub</c>) is the identity key; the <c>email</c> claim is kept for display only.
/// </summary>
public sealed class GoogleTokenVerifier : IExternalTokenVerifier
{
    private static readonly Uri KeysAddress = new("https://www.googleapis.com/oauth2/v3/certs");

    private readonly string _clientId;
    private readonly IdTokenValidator _validator;

    /// <summary>Initializes the verifier.</summary>
    /// <param name="options">The configured client ids.</param>
    /// <param name="http">The client that fetches Google's keys; the host shares one with a short timeout.</param>
    /// <param name="time">The clock for expiry checks.</param>
    public GoogleTokenVerifier(ExternalSignInOptions options, HttpClient http, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        _clientId = options.GoogleClientId;
        _validator = new IdTokenValidator(new JwksKeySet(http, KeysAddress, time), time);
    }

    /// <inheritdoc/>
    public ExternalProvider Provider => ExternalProvider.Google;

    /// <inheritdoc/>
    public async Task<ExternalIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken)
    {
        if (_clientId.Length == 0 || string.IsNullOrEmpty(idToken))
        {
            return null;
        }

        JsonDocument? payload;
        try
        {
            payload = await _validator.ValidateAsync(idToken, _clientId, IssuerAccepted, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // The keys are unavailable: the token cannot be checked, so it is refused rather than trusted.
            await Console.Error.WriteLineAsync("Google sign-in refused: the signing keys could not be read (" + ex.GetType().Name + ").").ConfigureAwait(false);
            return null;
        }

        if (payload is null)
        {
            return null;
        }

        using (payload)
        {
            JsonElement root = payload.RootElement;
            string? subject = root.TryGetProperty("sub", out JsonElement sub) && sub.ValueKind == JsonValueKind.String ? sub.GetString() : null;
            if (string.IsNullOrEmpty(subject) || subject.Length > ExternalLogin.MaxSubjectLength)
            {
                return null;
            }

            string? email = root.TryGetProperty("email", out JsonElement mail) && mail.ValueKind == JsonValueKind.String ? mail.GetString() : null;
            return new ExternalIdentity(Provider, subject, email);
        }
    }

    private static bool IssuerAccepted(JsonElement claims)
    {
        string? issuer = claims.TryGetProperty("iss", out JsonElement iss) && iss.ValueKind == JsonValueKind.String ? iss.GetString() : null;
        return issuer is "https://accounts.google.com" or "accounts.google.com";
    }
}
