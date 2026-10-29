// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Infrastructure.Identity;

/// <summary>
/// The public client ids of the Google and Microsoft apps whose ID tokens this server accepts (docs/adr/ADR-069). A provider with an empty
/// client id refuses every token. The values are not secrets; the client ids must match the ones the web client uses.
/// </summary>
public sealed class ExternalSignInOptions
{
    /// <summary>Gets the Google OAuth client id (the token audience). Empty turns Google sign-in off.</summary>
    public string GoogleClientId { get; init; } = string.Empty;

    /// <summary>Gets the Microsoft application (client) id (the token audience). Empty turns Microsoft sign-in off.</summary>
    public string MicrosoftClientId { get; init; } = string.Empty;

    /// <summary>Gets the Microsoft tenant: <c>common</c>, <c>organizations</c>, <c>consumers</c>, or one tenant id (a GUID).</summary>
    public string MicrosoftTenant { get; init; } = "common";

    /// <summary>Reads the options from the environment (<c>NOSUM_GOOGLE_CLIENT_ID</c>, <c>NOSUM_MICROSOFT_CLIENT_ID</c>, <c>NOSUM_MICROSOFT_TENANT</c>).</summary>
    /// <returns>The options; unset values keep their defaults.</returns>
    public static ExternalSignInOptions FromEnvironment() => new()
    {
        GoogleClientId = Read("NOSUM_GOOGLE_CLIENT_ID", string.Empty),
        MicrosoftClientId = Read("NOSUM_MICROSOFT_CLIENT_ID", string.Empty),
        MicrosoftTenant = Read("NOSUM_MICROSOFT_TENANT", "common"),
    };

    /// <summary>Checks the values that would otherwise build a wrong key-set address; a bad tenant stops the host at start-up.</summary>
    /// <exception cref="InvalidOperationException">The Microsoft tenant is neither a known alias nor a GUID.</exception>
    public void Validate()
    {
        if (MicrosoftTenant is not ("common" or "organizations" or "consumers") && !Guid.TryParse(MicrosoftTenant, out _))
        {
            throw new InvalidOperationException("NOSUM_MICROSOFT_TENANT must be common, organizations, consumers or a tenant id (GUID).");
        }
    }

    private static string Read(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
