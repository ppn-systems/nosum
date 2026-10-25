// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Abstractions.Security;

/// <summary>Hashes and verifies passwords. The stored value is self-describing, so the algorithm can be strengthened later.</summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a password with a fresh random salt.</summary>
    /// <param name="password">The plain-text password.</param>
    /// <returns>The value to store.</returns>
    string Hash(string password);

    /// <summary>Checks a password against a stored hash in constant time.</summary>
    /// <param name="password">The plain-text password.</param>
    /// <param name="storedHash">A value produced by <see cref="Hash"/>.</param>
    /// <returns>True when the password matches; false for a mismatch or an unreadable hash.</returns>
    bool Verify(string password, string storedHash);
}
