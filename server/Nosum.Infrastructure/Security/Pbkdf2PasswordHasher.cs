// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nosum.Abstractions.Security;

namespace Nosum.Infrastructure.Security;

/// <summary>
/// PBKDF2 with HMAC-SHA256 (<see cref="Rfc2898DeriveBytes.Pbkdf2(byte[], byte[], int, HashAlgorithmName, int)"/>).
/// Stored form: <c>PBKDF2-SHA256$iterations$salt$hash</c> with base64 salt and hash.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>OWASP guidance for PBKDF2-HMAC-SHA256 (Password Storage Cheat Sheet, 600000).</summary>
    public const int DefaultIterations = 600_000;

    private const string Prefix = "PBKDF2-SHA256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int MaxAcceptedIterations = 10_000_000;

    private readonly int _iterations;

    /// <summary>Initializes the hasher.</summary>
    /// <param name="iterations">Iteration count for new hashes; tests pass a small value, production keeps the default.</param>
    public Pbkdf2PasswordHasher(int iterations = DefaultIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        _iterations = iterations;
    }

    /// <inheritdoc/>
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, _iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    /// <inheritdoc/>
    public bool Verify(string password, string storedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(storedHash);

        string[] parts = storedHash.Split('$');
        if (parts.Length != 4
            || parts[0] != Prefix
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations)
            || iterations < 1
            || iterations > MaxAcceptedIterations)
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
