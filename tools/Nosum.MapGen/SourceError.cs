// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.IO;

namespace Nosum.MapGen;

/// <summary>Builds the error the tool reports for a bad source file: the file, the line when there is one, and what is wrong.</summary>
internal static class SourceError
{
    /// <summary>Creates an error that names the source file and line.</summary>
    /// <param name="source">The file the text came from.</param>
    /// <param name="line">The 1-based line, or zero when the problem is not on one line.</param>
    /// <param name="message">What is wrong.</param>
    /// <returns>The exception.</returns>
    public static InvalidDataException At(string source, int line, string message)
        => new(line == 0 ? $"{source}: {message}" : $"{source}:{line}: {message}");
}
