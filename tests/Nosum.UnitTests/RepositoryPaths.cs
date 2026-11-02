// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;

namespace Nosum.UnitTests;

/// <summary>
/// The repository root, found by walking up from the test assembly to the folder that holds <c>AGENTS.md</c>.
/// Do not derive it from <c>[CallerFilePath]</c>: a Release build on GitHub Actions (ContinuousIntegrationBuild, deterministic path map)
/// rewrites caller file paths to <c>/_/</c>, so such a lookup finds nothing.
/// </summary>
public static class RepositoryPaths
{
    /// <summary>Gets the full path of the repository root.</summary>
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
