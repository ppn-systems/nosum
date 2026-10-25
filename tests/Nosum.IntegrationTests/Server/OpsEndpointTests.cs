// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.Metrics;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Abstractions.Observability;
using Nosum.Host.Ops;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>The health, readiness and metrics endpoint (docs/adr/ADR-031 D4, D6) over real HTTP.</summary>
public sealed class OpsEndpointTests
{
    [Fact]
    public async Task Healthz_Always_AnswersOk()
    {
        await using OpsEndpoint endpoint = Start(static _ => Task.FromResult(false), out ushort port);

        (HttpStatusCode status, string body) = await GetAsync(port, "/healthz");

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Be("ok\n", "the process being up is all /healthz says, even when the database is not");
    }

    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    public async Task Readyz_FollowsTheReadinessCheck(bool ready, HttpStatusCode expected)
    {
        await using OpsEndpoint endpoint = Start(_ => Task.FromResult(ready), out ushort port);

        (HttpStatusCode status, _) = await GetAsync(port, "/readyz");

        status.Should().Be(expected);
    }

    [Fact]
    public async Task UnknownPathAndWrongMethod_AreRefused()
    {
        await using OpsEndpoint endpoint = Start(static _ => Task.FromResult(true), out ushort port);
        using HttpClient client = new();

        (await client.GetAsync(new Uri($"http://localhost:{port}/admin"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using HttpContent empty = new StringContent(string.Empty);
        (await client.PostAsync(new Uri($"http://localhost:{port}/metrics"), empty)).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Metrics_AfterTheServerRecordedThem_ExposesEveryFamilyInPrometheusText()
    {
        await using OpsEndpoint endpoint = Start(static _ => Task.FromResult(true), out ushort port);
        NosumMetrics.ObservePlayers(
            () => 3,
            () => [new Measurement<int>(2, new KeyValuePair<string, object?>("instance", "Town")), new Measurement<int>(1, new KeyValuePair<string, object?>("instance", "Farm"))]);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        NosumMetrics.RecordSnapshotTick(started);
        NosumMetrics.RecordSnapshotBytes(1_500);
        _ = NosumMetrics.RecordHandler(0x2000, started);
        NosumMetrics.RecordDbTransaction(started);
        NosumMetrics.IntentRejected("move", "TooFast");

        (HttpStatusCode status, string body) = await GetAsync(port, "/metrics");

        status.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("# TYPE nosum_players_online gauge").And.Contain("\nnosum_players_online 3\n");
        body.Should().Contain("nosum_instance_players{instance=\"Town\"} 2").And.Contain("nosum_instance_players{instance=\"Farm\"} 1");
        body.Should().Contain("# TYPE nosum_snapshot_tick_duration_ms histogram")
            .And.Contain("nosum_snapshot_tick_duration_ms_bucket{le=\"20\"}")
            .And.Contain("nosum_snapshot_tick_duration_ms_bucket{le=\"+Inf\"}")
            .And.Contain("nosum_snapshot_tick_duration_ms_count");
        body.Should().Contain("nosum_snapshot_bytes_per_tick_bucket{le=\"2048\"}");
        body.Should().Contain("nosum_handler_duration_ms_bucket{opcode=\"0x2000\",le=\"1000\"}");
        body.Should().Contain("nosum_db_transaction_duration_ms_count");
        body.Should().Contain("# TYPE nosum_intents_rejected_total counter").And.Contain("nosum_intents_rejected_total{area=\"move\",status=\"TooFast\"}");
    }

    [Fact]
    public async Task Metrics_Histogram_BucketsAreCumulativeAndEndAtTheCount()
    {
        await using OpsEndpoint endpoint = Start(static _ => Task.FromResult(true), out ushort port);
        NosumMetrics.RecordSnapshotBytes(100);
        NosumMetrics.RecordSnapshotBytes(100);
        NosumMetrics.RecordSnapshotBytes(100_000);

        (_, string body) = await GetAsync(port, "/metrics");

        long CountOf(string le)
        {
            string key = "nosum_snapshot_bytes_per_tick_bucket{le=\"" + le + "\"} ";
            int at = body.IndexOf(key, StringComparison.Ordinal);
            at.Should().BeGreaterThan(-1);
            int end = body.IndexOf('\n', at);
            return long.Parse(body[(at + key.Length)..end], System.Globalization.CultureInfo.InvariantCulture);
        }

        long small = CountOf("128");
        long large = CountOf("131072");
        long all = CountOf("+Inf");
        small.Should().BeGreaterThanOrEqualTo(2);
        large.Should().BeGreaterThanOrEqualTo(small + 1);
        all.Should().BeGreaterThanOrEqualTo(large);
    }

    [Fact]
    public async Task HealthCheckCommand_AgainstARunningEndpoint_ExitsZeroAndAgainstNothingExitsOne()
    {
        await using OpsEndpoint endpoint = Start(static _ => Task.FromResult(true), out ushort port);
        using System.IO.StringWriter output = new();
        static Microsoft.EntityFrameworkCore.DbContextOptions<NosumDbContext> NoDatabase() => throw new InvalidOperationException("The healthcheck must not touch the database.");

        int running = await HostCommands.RunAsync(["healthcheck"], NoDatabase, port, output, CancellationToken.None);
        int absent = await HostCommands.RunAsync(["healthcheck"], NoDatabase, FreePort(), output, CancellationToken.None);

        running.Should().Be(0);
        absent.Should().Be(1);
    }

    private static OpsEndpoint Start(Func<CancellationToken, Task<bool>> ready, out ushort port)
    {
        port = FreePort();
        return OpsEndpoint.Start("localhost", port, ready);
    }

    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(ushort port, string path)
    {
        using HttpClient client = new();
        using HttpResponseMessage response = await client.GetAsync(new Uri($"http://localhost:{port}{path}"));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static ushort FreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
