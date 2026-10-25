// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;

namespace Nosum.UnitTests.Maps;

/// <summary>Finds files of the repository from the test output folder.</summary>
internal static class RepoFiles
{
    private static readonly string s_root = FindRoot();

    public static string Path(params string[] parts) => System.IO.Path.Combine([s_root, .. parts]);

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "AGENTS.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (AGENTS.md) not found above " + AppContext.BaseDirectory);
    }
}
