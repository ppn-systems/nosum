// Copyright (c) 2026 PPN Systems. All rights reserved.
// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The snapshot tick must keep ticking whatever the clients do: a client that stops taking the chunk notices of its own walk is dropped
/// (docs/adr/ADR-087 D2, amended 2026-11-02), and clients that come and go in a burst never stop the snapshots of those who stay.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class SnapshotChurnTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(90);

    // One snapshot is due every 100 ms: a tick that does not come within this bound is a frozen loop.
    private static readonly TimeSpan s_step = TimeSpan.FromSeconds(10);

    private readonly PostgresFixture _database;

    public SnapshotChurnTests(PostgresFixture database) => _database = database;

    [Fact]
    [SuppressMessage("Design", "CA1031", Justification = "The walk fails once the server drops the stuck client; the assertions decide the outcome.")]
    public async Task StuckClientThatNeverTakesItsChunkNotices_DoesNotStopTheSnapshotsOfAnother_AndIsDropped()
    {
        ConcurrentDictionary<Guid, byte> exempt = new();
        ConcurrentBag<Guid> stalls = new();
        ServerSeams seams = new(WrapPresence: real => ChunkStallingPresence.Wrap(real, exempt, stalls));
        await using ChunkHost host = await ChunkHost.StartAsync(_database, s_timeout, seams: seams);
        _ = host.Maps.TryGet(GameMaps.Town, out MapDefinition? town);
        CrossingPlan plan = ChunkPlaces.FindCrossing(town!);
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;

        await using ChunkClient healthy = await host.ConnectAsync();
        await using ChunkClient stuck = await host.ConnectAsync();
        await healthy.PutOnAsync(GameMaps.Town, plan.AX, plan.AY);
        await stuck.PutOnAsync(GameMaps.Town, plan.AX, plan.AY);
        _ = await healthy.JoinAsync(host.Timeout);
        exempt[healthy.AccountId] = 0;
        await SnapshotsWithinAsync(healthy, 5, "before the stuck client joined");

        _ = await stuck.JoinAsync(host.Timeout);
        await SnapshotsWithinAsync(healthy, 5, "after the stuck client joined");

        // The stuck client walks three chunks east. The chunks behind it leave its interest set, and the ChunkLeft that says so is never taken.
        // Its later moves may fail once the server drops it, so the walk's own result is not what this test checks.
        int far = (plan.C0 + 3) * ChunkCoord.SizeTiles;
        Exception? walkFailure = null;
        try
        {
            await stuck.WalkToAsync(plan.AX, plan.AY, plan.ColumnX(far + 2), plan.BY, 200, host.Timeout);
        }
        catch (Exception ex)
        {
            // Dropped mid-walk: the assertions below decide.
            walkFailure = ex;
        }

        stalls.Should().Contain(stuck.AccountId, $"the stuck client's ChunkLeft must have been held, or the test proves nothing (walk: {walkFailure?.GetType().Name} {walkFailure?.Message})");
        await SnapshotsWithinAsync(healthy, 10, "after the stuck client's walk");
        while (presence.PlayerCount != 1)
        {
            await Task.Delay(25, host.Timeout);
        }

        presence.PlayerCount.Should().Be(1);
    }

    [Fact]
    public async Task ClientsThatComeAndGoInBursts_DoNotStopTheSnapshotsOfThoseWhoStay()
    {
        await using ChunkHost host = await ChunkHost.StartAsync(_database, s_timeout);
        _ = host.Maps.TryGet(GameMaps.Town, out MapDefinition? town);
        CrossingPlan plan = ChunkPlaces.FindCrossing(town!);
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;

        await using ChunkClient stays = await host.ConnectAsync();
        await stays.PutOnAsync(GameMaps.Town, plan.AX, plan.AY);
        _ = await stays.JoinAsync(host.Timeout);
        await SnapshotsWithinAsync(stays, 5, "before the first burst");

        // Each burst joins three visitors on the stayer's chunk, lets the tick run, and drops them with no leave of their own. The connection
        // closes race the tick that is building their snapshots.
        for (int burst = 0; burst < 5; burst++)
        {
            List<ChunkClient> visitors = [];
            for (int i = 0; i < 3; i++)
            {
                ChunkClient visitor = await host.ConnectAsync();
                visitors.Add(visitor);
                await visitor.PutOnAsync(GameMaps.Town, plan.AX, plan.AY);
                _ = await visitor.JoinAsync(host.Timeout);
            }

            await SnapshotsWithinAsync(stays, 3, "during a burst");
            foreach (ChunkClient visitor in visitors)
            {
                await visitor.DisposeAsync();
            }

            await SnapshotsWithinAsync(stays, 3, "after a burst's visitors left");
        }

        while (presence.PlayerCount != 1)
        {
            await Task.Delay(25, host.Timeout);
        }

        await SnapshotsWithinAsync(stays, 5, "after the last burst");
        presence.PlayerCount.Should().Be(1);
    }

    // Waits for the given number of new snapshots. When the tick stops for longer than one step the wait fails and says where it was.
    private static async Task SnapshotsWithinAsync(ChunkClient client, int count, string when)
    {
        using CancellationTokenSource bound = new(s_step);
        try
        {
            await client.WaitForSnapshotsAsync(count, bound.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            throw new TimeoutException($"{count} snapshots did not arrive within {s_step} {when}: the snapshot tick has stopped.");
        }
    }
}
