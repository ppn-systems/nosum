// Copyright (c) 2026 PPN Systems. All rights reserved.
// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using System.Linq;
using Nosum.Contracts.Emote;
using Nosum.Contracts.Emote.S2C;
using Nosum.Gameplay.Maps;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// A publish to the players of a chunk must not wait on one of them: a client that stops taking the place's emotes is dropped at its send deadline,
/// and the others get the publish at once (docs/adr/ADR-087, amendment 2026-11-02: the publish fan-out).
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class PublishFanoutTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(90);

    // The emote is delivered within this bound: a publish that waits on the stalled client never reaches the others.
    private static readonly TimeSpan s_step = TimeSpan.FromSeconds(10);

    private readonly PostgresFixture _database;

    public PublishFanoutTests(PostgresFixture database) => _database = database;

    [Fact]
    [SuppressMessage("Design", "CA1031", Justification = "The assertions decide the outcome; a dropped client may fail its own calls.")]
    public async Task StuckViewerThatNeverTakesAnEmote_DoesNotDelayTheOthersOfTheChunk_AndIsDropped()
    {
        ConcurrentDictionary<Guid, byte> stalled = new();
        ConcurrentBag<Guid> stalls = new();
        ServerSeams seams = new(WrapPresence: real => EmoteStallingPresence.Wrap(real, stalled, stalls));
        await using ChunkHost host = await ChunkHost.StartAsync(_database, s_timeout, seams: seams);
        _ = host.Maps.TryGet(GameMaps.Town, out MapDefinition? town);
        CrossingPlan plan = ChunkPlaces.FindCrossing(town!);
        IMapPresence presence = InstanceManager.Instance.GetExistingInstance<IMapPresence>()!;

        // The stuck viewer joins first, so it is the first subscriber of the chunk: a publish that waits on it would reach the others never.
        await using ChunkClient stuck = await host.ConnectAsync();
        await using ChunkClient healthy = await host.ConnectAsync();
        await using ChunkClient sender = await host.ConnectAsync();
        await stuck.PutOnAsync(GameMaps.Town, plan.AX, plan.AY);
        await healthy.PutOnAsync(GameMaps.Town, plan.AX, plan.AY);
        await sender.PutOnAsync(GameMaps.Town, plan.AX, plan.AY);
        _ = await stuck.JoinAsync(host.Timeout);
        _ = await healthy.JoinAsync(host.Timeout);
        _ = await sender.JoinAsync(host.Timeout);
        await SnapshotsWithinAsync(healthy, 3, "before the emote");

        // The stuck viewer keeps its connection and its snapshots; only the emotes of the place never reach it.
        stalled[stuck.AccountId] = 0;
        Task<EmoteResult> answer = sender.SendEmoteAsync(1, host.Timeout);

        // The healthy viewer must get the emote while the stuck viewer's send is still held: the publish does not wait on the stuck one.
        await WithinAsync(healthy, c => c.EmotesShown.Contains(sender.PlayerId), "the emote reaches the healthy viewer");
        stalls.Should().Contain(stuck.AccountId, "the stuck viewer's emote must have been held, or the test proves nothing");
        (await answer).Status.Should().Be(EmoteStatus.Ok);

        // The stuck viewer missed its deadline and is disconnected; the others stay.
        using CancellationTokenSource bound = new(s_step);
        while (presence.PlayerCount != 2)
        {
            await Task.Delay(25, bound.Token);
        }

        presence.PlayerCount.Should().Be(2);
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

    private static async Task WithinAsync(ChunkClient client, Func<ChunkClient, bool> condition, string when)
    {
        using CancellationTokenSource bound = new(s_step);
        try
        {
            while (!condition(client))
            {
                await Task.Delay(25, bound.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (bound.IsCancellationRequested)
        {
            throw new TimeoutException($"{when} did not happen within {s_step}: the publish waited on another viewer.");
        }
    }
}
