// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Security.Cryptography;
using System.Text;
using Nosum.Abstractions.Security;

namespace Nosum.Infrastructure.Security;

/// <summary>
/// Session tokens are 32 random bytes (base64url). Only the SHA-256 hash is stored; a fast hash is
/// enough because the token already has 256 bits of entropy.
/// </summary>
public sealed class SessionTokenService : ISessionTokenService
{
    private const int TokenBytes = 32;

    /// <inheritdoc/>
    public IssuedToken Create()
    {
        string token = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        return new IssuedToken(token, this.Hash(token));
    }

    /// <inheritdoc/>
    public string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
