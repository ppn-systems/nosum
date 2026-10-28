// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Nosum.UnitTests;

/// <summary>
/// Structural and architectural invariants enforced across the repository:
/// 800-line ceiling per source file, platform-free engine assemblies, infrastructure-free and LINQ-free handlers,
/// socket-free Razor UI components, single-responsibility EF Core configurations, and Vietnamese/English `.resx` parity.
/// </summary>
public sealed class ArchitectureAuditTests
{
    [Fact]
    public void AllSourceFiles_StayWithinEightHundredLineCeiling()
    {
        string root = RepoRoot();
        List<string> oversized = [];
        foreach (string file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".cs", StringComparison.Ordinal) && !file.EndsWith(".razor", StringComparison.Ordinal))
            {
                continue;
            }

            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.Contains("/bin/", StringComparison.Ordinal)
                || relative.Contains("/Migrations/", StringComparison.Ordinal)
                || relative.Contains("/.system_generated/", StringComparison.Ordinal)
                || relative.StartsWith("artifacts/", StringComparison.Ordinal)
                || relative.StartsWith(".git/", StringComparison.Ordinal))
            {
                continue;
            }

            int lines = File.ReadLines(file).Count();
            if (lines > 800)
            {
                oversized.Add($"{relative} ({lines} lines)");
            }
        }

        oversized.Should().BeEmpty("every C# and Razor source file must stay at or below 800 lines");
    }

    [Fact]
    public void EngineAssemblies_ReferenceNoPlatformOrGameAssemblies()
    {
        Assembly[] engineAssemblies =
        [
            typeof(Nosum.Core.Facing).Assembly,
            typeof(Nosum.Rendering.Camera2D).Assembly,
        ];
        string[] forbiddenPrefixes =
        [
            "Microsoft.JSInterop",
            "Microsoft.AspNetCore",
            "Nalix",
            "Nosum.Contracts",
            "Nosum.Gameplay",
        ];

        List<string> violations = [];
        foreach (Assembly assembly in engineAssemblies)
        {
            foreach (AssemblyName referenced in assembly.GetReferencedAssemblies())
            {
                string name = referenced.Name ?? string.Empty;
                if (forbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    violations.Add($"{assembly.GetName().Name} -> {name}");
                }
            }
        }

        violations.Should().BeEmpty("Nosum.Core and Nosum.Rendering must remain platform-free and game-rule-free");
    }

    [Fact]
    public void HandlersAssembly_ReferencesNoInfrastructureAndUsesNoLinq()
    {
        string[] referenced = [.. typeof(Nosum.Handlers.Player.PlayerHandlers).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty)];
        referenced.Should().NotContain(name => name.StartsWith("Nosum.Infrastructure", StringComparison.Ordinal));
        referenced.Should().NotContain(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        string handlersDir = Path.Combine(RepoRoot(), "server", "Nosum.Handlers");
        List<string> linqFiles = [];
        foreach (string file in Directory.EnumerateFiles(handlersDir, "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(handlersDir, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            if (File.ReadLines(file).Any(line => line.TrimStart().StartsWith("using System.Linq;", StringComparison.Ordinal)))
            {
                linqFiles.Add(relative);
            }
        }

        linqFiles.Should().BeEmpty("packet handlers are hot paths and must not use System.Linq");
    }

    [Fact]
    public void RazorFiles_NeverReferenceSocketOrContractsDirectly()
    {
        string wasmDir = Path.Combine(RepoRoot(), "wasm");
        List<string> violations = [];
        foreach (string file in Directory.EnumerateFiles(wasmDir, "*.razor*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(wasmDir, file).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            string content = File.ReadAllText(file);
            if (content.Contains("@using Nosum.Wasm.Socket", StringComparison.Ordinal)
                || content.Contains("@using Nosum.Contracts", StringComparison.Ordinal))
            {
                violations.Add(relative);
            }
        }

        violations.Should().BeEmpty("Razor components must communicate through ViewModels rather than referencing socket or wire contracts directly");
    }

    [Fact]
    public void EntityConfigurations_HaveOneConfigurationTypePerFile()
    {
        string configDir = Path.Combine(RepoRoot(), "server", "Nosum.Infrastructure", "Persistence", "Configurations");
        List<string> multiConfigFiles = [];
        foreach (string file in Directory.EnumerateFiles(configDir, "*.cs", SearchOption.AllDirectories))
        {
            int count = File.ReadLines(file).Count(line => line.Contains(": IEntityTypeConfiguration<", StringComparison.Ordinal));
            if (count > 1)
            {
                multiConfigFiles.Add($"{Path.GetFileName(file)} ({count})");
            }
        }

        multiConfigFiles.Should().BeEmpty("each EF Core entity configuration class must live in its own file");
    }

    [Fact]
    public void LocalizationResources_HaveExactKeyParityBetweenVietnameseAndEnglish()
    {
        string resourcesDir = Path.Combine(RepoRoot(), "wasm", "Nosum.Wasm.ViewModels", "Resources");
        (string DefaultFile, string EnglishFile)[] pairs =
        [
            ("Language.resx", "Language.en.resx"),
            ("Catalogue.resx", "Catalogue.en.resx"),
        ];

        foreach ((string defaultFile, string englishFile) in pairs)
        {
            Dictionary<string, string> vi = LoadResx(Path.Combine(resourcesDir, defaultFile));
            Dictionary<string, string> en = LoadResx(Path.Combine(resourcesDir, englishFile));

            vi.Keys.Should().BeEquivalentTo(en.Keys, $"{defaultFile} and {englishFile} must define the exact same resource keys");
            vi.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).Should().BeEmpty($"{defaultFile} must not contain empty values");
            en.Where(kv => string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key).Should().BeEmpty($"{englishFile} must not contain empty values");
        }
    }

    private static Dictionary<string, string> LoadResx(string path)
    {
        XDocument doc = XDocument.Load(path);
        return doc.Root!
            .Elements("data")
            .ToDictionary(
                el => el.Attribute("name")!.Value,
                el => el.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
