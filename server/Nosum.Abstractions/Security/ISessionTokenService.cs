// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Abstractions.Security;

/// <summary>Creates opaque session tokens and the hashes that are stored in their place.</summary>
public interface ISessionTokenService
{
    /// <summary>Creates a new random token.</summary>
    /// <returns>The token (sent to the client once) and its hash (stored).</returns>
    IssuedToken Create();

    /// <summary>Hashes a token presented by a client so it can be looked up.</summary>
    /// <param name="token">The token as the client sent it.</param>
    /// <returns>The hash to compare with stored hashes.</returns>
    string Hash(string token);
}

/// <summary>A freshly created session token.</summary>
/// <param name="Token">The secret token; give it to the client and never store or log it.</param>
/// <param name="Hash">The hash to store.</param>
public readonly record struct IssuedToken(string Token, string Hash);
