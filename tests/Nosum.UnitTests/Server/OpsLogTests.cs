// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Nosum.Abstractions.Observability;
using Nosum.Infrastructure.Security;
using Xunit;

namespace Nosum.UnitTests.Server;

/// <summary>The JSON log line and its scrubbing (docs/adr/ADR-031 D5): secrets never reach the output.</summary>
[Collection(OpsLogSerial.Name)]
public sealed class OpsLogTests : IDisposable
{
    private readonly StringWriter _output = new();

    public OpsLogTests()
    {
        OpsLog.Configure(_output, new FixedClock(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)));
        OpsLog.MinimumLevel = OpsLevel.Information;
    }

    public void Dispose()
    {
        OpsLog.Configure(TextWriter.Null);
        _output.Dispose();
    }

    [Fact]
    public void Write_Event_IsOneJsonObjectWithEventLevelTimeAndFields()
    {
        Guid player = Guid.NewGuid();

        OpsLog.Warning("handler.slow", "A packet handler was slow.", new LogField("player_id", player), new LogField("latency_ms", 120.5), new LogField("map_instance", "Town"));

        string line = _output.ToString().TrimEnd();
        line.Should().NotContain("\n");
        using JsonDocument json = JsonDocument.Parse(line);
        json.RootElement.GetProperty("ts").GetString().Should().Be("2026-10-25T12:00:00.000Z");
        json.RootElement.GetProperty("level").GetString().Should().Be("warn");
        json.RootElement.GetProperty("event").GetString().Should().Be("handler.slow");
        json.RootElement.GetProperty("player_id").GetGuid().Should().Be(player);
        json.RootElement.GetProperty("latency_ms").GetDouble().Should().Be(120.5);
        json.RootElement.GetProperty("map_instance").GetString().Should().Be("Town");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("new_password")]
    [InlineData("session_token")]
    [InlineData("Authorization")]
    [InlineData("api_key")]
    [InlineData("password_hash")]
    [InlineData("email")]
    [InlineData("chat_text")]
    [InlineData("text")]
    [InlineData("ConnectionString")]
    public void Write_FieldNamedLikeASecret_IsRedactedWhateverItsValue(string name)
    {
        OpsLog.Information("test.secret", "A field.", new LogField(name, "hunter2-the-real-value"));

        _output.ToString().Should().Contain("[redacted]").And.NotContain("hunter2");
    }

    [Fact]
    public void Write_EmailAddressInAnyText_IsMasked()
    {
        OpsLog.Error("test.email", "Sign-in failed for someone@gmail.com today.", new LogField("detail", "retry for other.person+tag@gmail.com"));

        string line = _output.ToString();
        line.Should().NotContain("gmail").And.Contain("[email]");
    }

    [Fact]
    public void Write_SessionTokenInAnyText_IsRedacted()
    {
        // 100 random tokens: base64url, so some contain '-' and '_' that must not break the run.
        SessionTokenService tokens = new();
        for (int i = 0; i < 100; i++)
        {
            string token = tokens.Create().Token;
            using StringWriter output = new();
            OpsLog.Configure(output);

            OpsLog.Information("test.token", "Resume with " + token + " failed.", new LogField("detail", "token=" + token));

            output.ToString().Should().NotContain(token).And.NotContain(token[..20]);
        }
    }

    [Fact]
    public void Write_IdsAndMapKeys_AreKept()
    {
        Guid player = Guid.NewGuid();

        OpsLog.Information("test.ids", "Player " + player + " joined.", new LogField("map_instance", "Farm@" + player.ToString("N")));

        _output.ToString().Should().Contain(player.ToString()).And.Contain(player.ToString("N"));
    }

    [Fact]
    public void Write_BelowMinimumLevel_WritesNothing()
    {
        OpsLog.MinimumLevel = OpsLevel.Warning;

        OpsLog.Information("test.quiet", "Not wanted.");

        _output.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Scrub_PlainText_IsUnchanged()
        => OpsLog.Scrub("The server is accepting connections on port 57300.").Should().Be("The server is accepting connections on port 57300.");

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}

/// <summary>OpsLog is process-wide state: its tests must not run next to each other.</summary>
[CollectionDefinition(Name)]
public sealed class OpsLogSerial
{
    /// <summary>Collection name.</summary>
    public const string Name = "OpsLog";
}
