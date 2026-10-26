// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Nosum.AssetInventory;

/// <summary>
/// The Stardew Valley scan (docs/adr/ADR-042 D5). A file is Stardew Valley content when its SHA-256 is one of the hashes in
/// <c>docs/art/stardew-fingerprints.sha256</c>, wherever the file sits in the repository. Build output and Git's own data are skipped:
/// they are copies of tracked files or are never shipped.
/// </summary>
internal static class StardewScan
{
    /// <summary>The file name under <c>docs/art/</c>.</summary>
    public const string FileName = "stardew-fingerprints.sha256";

    private static readonly HashSet<string> s_skippedFolders = new(StringComparer.Ordinal) { ".git", "bin", "obj", "artifacts", "node_modules", "TestResults" };

    /// <summary>Reads the fingerprint list: one lower-case SHA-256 per line; blank lines and <c>#</c> comments are ignored.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hashes.</returns>
    /// <exception cref="InvalidDataException">A line is neither blank, a comment nor a SHA-256 in lower-case hex.</exception>
    public static HashSet<string> ReadFingerprints(string path)
    {
        HashSet<string> hashes = new(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (!IsHash(line))
            {
                throw new InvalidDataException($"{path}:{i + 1}: not a SHA-256 hash in lower-case hex.");
            }

            _ = hashes.Add(line);
        }

        return hashes;
    }

    /// <summary>Finds every file of the repository whose hash is listed.</summary>
    /// <param name="repo">The repository root.</param>
    /// <param name="fingerprints">The listed hashes.</param>
    /// <returns>The matching files, relative to <paramref name="repo"/> with <c>/</c> separators, in ordinal order. An empty file never matches.</returns>
    public static List<string> Scan(string repo, IReadOnlySet<string> fingerprints)
    {
        List<string> hits = [];
        foreach (string file in Files(repo))
        {
            if (new FileInfo(file).Length == 0)
            {
                continue;
            }

            using FileStream stream = File.OpenRead(file);
            string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (fingerprints.Contains(hash))
            {
                hits.Add(Path.GetRelativePath(repo, file).Replace(Path.DirectorySeparatorChar, '/'));
            }
        }

        hits.Sort(StringComparer.Ordinal);
        return hits;
    }

    private static bool IsHash(string text)
    {
        if (text.Length != 64)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!(c is >= '0' and <= '9' || c is >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> Files(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(folder))
        {
            if (!s_skippedFolders.Contains(Path.GetFileName(child)))
            {
                foreach (string file in Files(child))
                {
                    yield return file;
                }
            }
        }
    }
}
