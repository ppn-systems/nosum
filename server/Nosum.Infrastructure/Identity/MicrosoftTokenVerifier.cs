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
/// Verifies Microsoft identity platform ID tokens (docs/adr/ADR-069): signed by the tenant's published keys, issued to the configured application id,
/// and from the issuer of the token's own tenant (<c>tid</c>). When the server is set to one tenant, only that tenant's tokens pass.
/// The subject is the object id (<c>oid</c>), stable within a tenant; <c>sub</c> is the fallback. The e-mail is kept for display only.
/// </summary>
public sealed class MicrosoftTokenVerifier : IExternalTokenVerifier
{
    private readonly string _clientId;
    private readonly string _tenant;
    private readonly IdTokenValidator _validator;

    /// <summary>Initializes the verifier.</summary>
    /// <param name="options">The configured application id and tenant.</param>
    /// <param name="http">The client that fetches the keys; the host shares one with a short timeout.</param>
    /// <param name="time">The clock for expiry checks.</param>
    public MicrosoftTokenVerifier(ExternalSignInOptions options, HttpClient http, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _clientId = options.MicrosoftClientId;
        _tenant = options.MicrosoftTenant;
        Uri keys = new($"https://login.microsoftonline.com/{_tenant}/discovery/v2.0/keys");
        _validator = new IdTokenValidator(new JwksKeySet(http, keys, time), time);
    }

    /// <inheritdoc/>
    public ExternalProvider Provider => ExternalProvider.Microsoft;

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
            await Console.Error.WriteLineAsync("Microsoft sign-in refused: the signing keys could not be read (" + ex.GetType().Name + ").").ConfigureAwait(false);
            return null;
        }

        if (payload is null)
        {
            return null;
        }

        using (payload)
        {
            JsonElement root = payload.RootElement;
            string? subject = StringOf(root, "oid") ?? StringOf(root, "sub");
            if (string.IsNullOrEmpty(subject) || subject.Length > ExternalLogin.MaxSubjectLength)
            {
                return null;
            }

            return new ExternalIdentity(Provider, subject, StringOf(root, "email"));
        }
    }

    // The issuer is the token's own tenant. A tenant id configured on the server must be the token's tenant as well.
    private bool IssuerAccepted(JsonElement claims)
    {
        string? tenantId = StringOf(claims, "tid");
        if (tenantId is null || !Guid.TryParse(tenantId, out _))
        {
            return false;
        }

        if (Guid.TryParse(_tenant, out _) && !string.Equals(tenantId, _tenant, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(StringOf(claims, "iss"), "https://login.microsoftonline.com/" + tenantId + "/v2.0", StringComparison.Ordinal);
    }

    private static string? StringOf(JsonElement claims, string name)
        => claims.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
