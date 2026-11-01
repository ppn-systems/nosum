// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Presence;
using Nosum.Host;
using Xunit;
using TestHost = Nosum.IntegrationTests.Server.MapChangeTests.Host;
using TestWorldClient = Nosum.IntegrationTests.Server.MapChangeTests.WorldClient;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// One stalled client must not hold the snapshot tick of everyone else: its send has a deadline, and a viewer that misses it is dropped
/// (docs/perf/server-perf.md, the snapshot send without a deadline).
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class SnapshotSendDeadlineTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public SnapshotSendDeadlineTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task StalledViewer_DoesNotStopTheSnapshotsOfTheOthers_AndIsDropped()
    {
        ConcurrentDictionary<Guid, byte> stalled = new();
        ServerSeams seams = new(WrapPresence: real => StallingPresence.Wrap(real, stalled));
        await using TestHost host = await TestHost.StartAsync(_database, seams);
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;

        await using TestWorldClient healthy = await host.ConnectAsync();
        _ = await healthy.JoinAsync(host.Timeout);
        await using TestWorldClient stuck = await host.ConnectAsync();
        _ = await stuck.JoinAsync(host.Timeout);

        // From now on every snapshot send to the stuck viewer never completes.
        stalled[stuck.AccountId] = 0;

        // The healthy viewer keeps getting its snapshots: the tick does not wait for the stuck send.
        for (int i = 0; i < 5; i++)
        {
            (await healthy.WaitForSnapshotAsync(_ => true, host.Timeout)).Should().NotBeNull();
        }

        // The stuck viewer missed its send deadline and was dropped; the healthy one is still on the map.
        while (presence.PlayerCount != 1)
        {
            await Task.Delay(25, host.Timeout);
        }

        presence.PlayerCount.Should().Be(1);
    }
}
