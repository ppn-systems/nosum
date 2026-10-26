// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;

namespace Nosum.UnitTests.Maps;

/// <summary>
/// The Stardew Valley placeholder content (docs/adr/ADR-007, docs/adr/ADR-040 D9), which the repository no longer holds. A test that compares with it or
/// regenerates from it reads the <c>assets/</c> folder of commit <c>64ce88e</c> named by <c>NOSUM_PLACEHOLDER_ROOT</c>, and returns early when the
/// variable is unset; tools/README.md says how to set it. A variable that names a missing folder fails the test run.
/// </summary>
internal static class PlaceholderContent
{
    private const string Variable = "NOSUM_PLACEHOLDER_ROOT";

    private static readonly string? s_assets = Resolve();

    /// <summary>Gets a value indicating whether the placeholder content can be read.</summary>
    public static bool Available => s_assets is not null;

    /// <summary>Gets the placeholder <c>assets/</c> folder. Read only when <see cref="Available"/>.</summary>
    /// <exception cref="InvalidOperationException">The variable is unset.</exception>
    public static string Assets => s_assets ?? throw new InvalidOperationException(Variable + " is not set.");

    /// <summary>Combines parts under the placeholder <c>assets/</c> folder.</summary>
    /// <param name="parts">The parts of the path.</param>
    /// <returns>The full path.</returns>
    public static string Of(params string[] parts) => Path.Combine([Assets, .. parts]);

    private static string? Resolve()
    {
        string? root = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException(Variable + " names a folder that does not exist: " + root);
        }

        return Path.GetFullPath(root);
    }
}
