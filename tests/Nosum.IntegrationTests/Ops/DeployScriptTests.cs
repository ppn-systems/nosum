// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Npgsql;
using Nosum.Abstractions.Accounts;
using Nosum.Contracts.Common;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.IntegrationTests.Ops;

/// <summary>
/// The deployment scripts and the placeholder-art gate (docs/adr/ADR-031 D3, D9), run for real: a shell script, an MSBuild target, and the
/// backup/restore round trip against PostgreSQL. A gate nobody runs is a gate that rots.
/// </summary>
public sealed class DeployScriptTests : IClassFixture<Server.PostgresFixture>, IDisposable
{
    private readonly Server.PostgresFixture _database;
    private readonly string _scratch = Directory.CreateTempSubdirectory("nosum-deploy-").FullName;

    public DeployScriptTests(Server.PostgresFixture database) => _database = database;

    public void Dispose() => Directory.Delete(_scratch, recursive: true);

    // ---- placeholder-art gate (script) ----

    [Fact]
    public async Task Gate_PublicReleaseWhileTheMarkerExists_IsRefused()
    {
        string root = this.RepositoryWith(marker: true);

        (int code, string output) = await RunAsync("sh", [DeployFile("check-placeholder-art.sh"), "public", root]);

        code.Should().Be(1);
        output.Should().Contain("REFUSED").And.Contain("ADR-007");
    }

    [Fact]
    public async Task Gate_PrivateReleaseWhileTheMarkerExists_PassesWithAWarning()
    {
        string root = this.RepositoryWith(marker: true);

        (int code, string output) = await RunAsync("sh", [DeployFile("check-placeholder-art.sh"), "private", root]);

        code.Should().Be(0);
        output.Should().Contain("placeholder art");
    }

    [Theory]
    [InlineData("public")]
    [InlineData("private")]
    public async Task Gate_NoMarker_PassesSilently(string release)
    {
        string root = this.RepositoryWith(marker: false);

        (int code, string output) = await RunAsync("sh", [DeployFile("check-placeholder-art.sh"), release, root]);

        code.Should().Be(0);
        output.Should().BeEmpty();
    }

    [Fact]
    public async Task Gate_UnknownRelease_IsABadArgument()
    {
        (int code, _) = await RunAsync("sh", [DeployFile("check-placeholder-art.sh"), "staging", this.RepositoryWith(marker: false)]);

        code.Should().Be(2);
    }

    [Fact]
    public async Task Gate_NoArguments_UsesNosumReleaseFromTheEnvironmentAndTheRealRepository()
    {
        (int publicCode, _) = await RunAsync("sh", [DeployFile("check-placeholder-art.sh")], new Dictionary<string, string> { ["NOSUM_RELEASE"] = "public" });
        (int privateCode, _) = await RunAsync("sh", [DeployFile("check-placeholder-art.sh")], new Dictionary<string, string> { ["NOSUM_RELEASE"] = "private" });

        publicCode.Should().Be(1, "assets/PLACEHOLDER_CONTENT is still in the repository");
        privateCode.Should().Be(0);
    }

    // ---- placeholder-art gate (MSBuild) ----

    [Theory]
    [InlineData("server/Nosum.Host/Nosum.Host.csproj")]
    [InlineData("wasm/Nosum.Wasm/Nosum.Wasm.csproj")]
    public async Task GateTarget_PublicReleaseInTheRealRepository_FailsWhileThePlaceholderMarkerIsThere(string project)
    {
        File.Exists(Path.Combine(RepositoryRoot(), "assets", "PLACEHOLDER_CONTENT")).Should().BeTrue("M6 deletes the marker; until then this test pins the gate on");

        (int code, string output) = await RunDotnetAsync(["msbuild", Path.Combine(RepositoryRoot(), project), "-t:NosumPlaceholderGate", "-p:NosumRelease=public", "-nologo", "-v:q"]);

        code.Should().NotBe(0);
        output.Should().Contain("ADR-007");
    }

    [Fact]
    public async Task GateTarget_PublicReleaseWithoutTheMarker_PassesAndPrivateAlwaysPasses()
    {
        string clean = this.RepositoryWith(marker: false) + Path.DirectorySeparatorChar;
        string marked = this.RepositoryWith(marker: true) + Path.DirectorySeparatorChar;
        string project = Path.Combine(RepositoryRoot(), "server", "Nosum.Host", "Nosum.Host.csproj");

        (int publicClean, _) = await RunDotnetAsync(["msbuild", project, "-t:NosumPlaceholderGate", "-p:NosumRelease=public", $"-p:NosumRepoRoot={clean}", "-nologo", "-v:q"]);
        (int privateMarked, _) = await RunDotnetAsync(["msbuild", project, "-t:NosumPlaceholderGate", "-p:NosumRelease=private", $"-p:NosumRepoRoot={marked}", "-nologo", "-v:q"]);
        (int publicMarked, _) = await RunDotnetAsync(["msbuild", project, "-t:NosumPlaceholderGate", "-p:NosumRelease=public", $"-p:NosumRepoRoot={marked}", "-nologo", "-v:q"]);

        publicClean.Should().Be(0);
        privateMarked.Should().Be(0);
        publicMarked.Should().NotBe(0);
    }

    // ---- host entrypoint ----

    [Fact]
    public async Task Entrypoint_FirstStart_WritesTheProxyTrustAndOriginSettings()
    {
        string config = Path.Combine(_scratch, "config");

        (int code, _) = await RunAsync("sh", [DeployFile("host-entrypoint.sh"), "--configure-only"], Entrypoint(config, "https://play.example.org", "172.28.0.0/24, 10.0.0.1"));

        code.Should().Be(0);
        (await File.ReadAllLinesAsync(Path.Combine(config, "trusted_proxies.txt"))).Should().Contain(["172.28.0.0/24", "10.0.0.1"]);
        string ini = await File.ReadAllTextAsync(Path.Combine(config, "server.ini"));
        ini.Should().Contain("[ForwardedHeaders]").And.Contain("RequireTrustedProxy = True").And.Contain("ClientIpHeader      = XForwardedFor");
        ini.Should().Contain("AllowedOrigins = https://play.example.org");
    }

    [Fact]
    public async Task Entrypoint_ExistingServerIni_ChangesOnlyItsOwnKeysInTheRightSections()
    {
        string config = Path.Combine(_scratch, "config");
        Directory.CreateDirectory(config);
        await File.WriteAllTextAsync(Path.Combine(config, "server.ini"), string.Join('\n',
            "[Buffer]", "Enabled             = False", "",
            "[ForwardedHeaders]", "Enabled             = False", "RequireTrustedProxy = False", "ClientIpHeader      = Auto", "",
            "[NetworkWebSocket]", "Port                     = 57300", "AllowedOrigins           = https://old.example.org", "Enabled = False", "",
            "[Other]", "Enabled = False", "AllowedOrigins = untouched", string.Empty));

        (int code, _) = await RunAsync("sh", [DeployFile("host-entrypoint.sh"), "--configure-only"], Entrypoint(config, "https://new.example.org", "10.1.0.0/16"));

        code.Should().Be(0);
        string[] lines = await File.ReadAllLinesAsync(Path.Combine(config, "server.ini"));
        SectionOf(lines, "[Buffer]").Should().Contain("Enabled             = False");
        SectionOf(lines, "[ForwardedHeaders]").Should().Contain(["Enabled             = True", "RequireTrustedProxy = True", "ClientIpHeader      = XForwardedFor"]);
        SectionOf(lines, "[NetworkWebSocket]").Should().Contain(["Port                     = 57300", "AllowedOrigins           = https://new.example.org", "Enabled = False"]);
        SectionOf(lines, "[Other]").Should().Contain(["Enabled = False", "AllowedOrigins = untouched"]);
    }

    [Fact]
    public async Task Entrypoint_WithoutAnOrigin_RefusesToStart()
    {
        Dictionary<string, string> environment = Entrypoint(Path.Combine(_scratch, "config"), "https://x.example", "10.0.0.1");
        _ = environment.Remove("NOSUM_ORIGIN");

        (int code, string output) = await RunAsync("sh", [DeployFile("host-entrypoint.sh"), "--configure-only"], environment);

        code.Should().NotBe(0);
        output.Should().Contain("NOSUM_ORIGIN");
    }

    // ---- backup and restore ----

    [Fact]
    public async Task BackupThenRestore_IntoAScratchDatabase_GivesTheSameRowsAndHonoursTheRetention()
    {
        // Real rows to compare: an account, its player row and the rows registration adds.
        AccountService accounts = new(new EfUnitOfWorkFactory(_database.Options), new Pbkdf2PasswordHasher(iterations: 1000), new SessionTokenService(), TimeProvider.System);
        AccountOutcome outcome = await accounts.RegisterAsync("bk" + Guid.NewGuid().ToString("N")[..12] + "@gmail.com", "password-1", "Test" + Guid.NewGuid().ToString("N")[..10], GameLanguage.English, default);
        outcome.Status.Should().Be(Nosum.Contracts.Account.AccountStatus.Ok);
        NpgsqlConnectionStringBuilder connection = new(_database.ConnectionString);
        Dictionary<string, string> environment = new()
        {
            ["PGHOST"] = connection.Host ?? "localhost",
            ["PGPORT"] = connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["PGUSER"] = connection.Username ?? "nosum",
            ["PGPASSWORD"] = connection.Password ?? string.Empty,
            ["PGDATABASE"] = connection.Database ?? "postgres",
        };

        (int code, string output) = await RunAsync("sh", [DeployFile(Path.Combine("backup", "test-restore.sh"))], environment);

        code.Should().Be(0, output);
        output.Should().Contain("identical row counts");
    }

    // ---- helpers ----

    private static string[] SectionOf(string[] lines, string header)
    {
        int start = Array.IndexOf(lines, header);
        start.Should().BeGreaterThan(-1);
        int end = Array.FindIndex(lines, start + 1, l => l.StartsWith('['));
        return lines[(start + 1)..(end < 0 ? lines.Length : end)];
    }

    private static Dictionary<string, string> Entrypoint(string config, string origin, string proxies)
        => new() { ["NALIX_CONFIG_PATH"] = config, ["NOSUM_ORIGIN"] = origin, ["NOSUM_TRUSTED_PROXIES"] = proxies };

    // A fake repository root: only the marker matters to the gate.
    private string RepositoryWith(bool marker)
    {
        string root = Path.Combine(_scratch, "repo-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        if (marker)
        {
            File.WriteAllText(Path.Combine(root, "assets", "PLACEHOLDER_CONTENT"), "placeholder");
        }

        return root;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root (global.json) was not found above the test binaries.");
    }

    private static string DeployFile(string name) => Path.Combine(RepositoryRoot(), "deploy", name);

    private static Task<(int Code, string Output)> RunDotnetAsync(string[] args)
        => RunAsync(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", args);

    private static async Task<(int Code, string Output)> RunAsync(string file, string[] args, Dictionary<string, string>? environment = null)
    {
        ProcessStartInfo start = new(file) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        foreach ((string key, string value) in environment ?? [])
        {
            start.Environment[key] = value;
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + file);
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await error);
    }
}
