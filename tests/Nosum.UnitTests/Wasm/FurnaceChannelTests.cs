// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Furnace;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class FurnaceChannelTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this.Now;

        // Timers run on the real clock, so a test that waits for a batch to finish waits a few real milliseconds.
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            TimeProvider.System.CreateTimer(callback, state, dueTime, period);
    }

    [Fact]
    public void Load_StoresTheJobs_AndCountsTheRemainingTimeDown()
    {
        ManualTime time = new();
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            FurnaceSnapshot snapshot = new();
            snapshot.Furnaces.Add(new FurnaceRow { ObjectId = 4, State = FurnaceStateCode.Working, OreItemId = ItemCatalog.CopperOre, RemainingMs = 60_000 });
            snapshot.Furnaces.Add(new FurnaceRow { ObjectId = 5, State = FurnaceStateCode.Ready, OreItemId = ItemCatalog.IronOre, RemainingMs = 0 });
            return snapshot;
        });
        using FurnaceChannel channel = new(socket, time);

        channel.Load();

        channel.StateOf(4, out int ore, out int remaining).Should().Be(FurnaceStateCode.Working);
        (ore, remaining).Should().Be((ItemCatalog.CopperOre, 60_000));
        channel.StateOf(5, out _, out _).Should().Be(FurnaceStateCode.Ready);
        channel.StateOf(6, out _, out _).Should().Be(FurnaceStateCode.Idle, "a furnace with no row is idle");

        time.Now = time.Now.AddSeconds(45);
        channel.StateOf(4, out _, out remaining).Should().Be(FurnaceStateCode.Working);
        remaining.Should().Be(15_000);

        time.Now = time.Now.AddSeconds(16);
        channel.StateOf(4, out _, out remaining).Should().Be(FurnaceStateCode.Ready);
        remaining.Should().Be(0);
    }

    [Fact]
    public void IsWorking_IsTrueOnlyWhileTheBarIsNotReady()
    {
        ManualTime time = new();
        FakeGameSocket socket = new();
        using FurnaceChannel channel = new(socket, time);
        FurnaceChanged push = new() { ObjectId = 4, State = FurnaceStateCode.Working, OreItemId = ItemCatalog.CopperOre, RemainingMs = 1000 };

        socket.Push(push);
        channel.IsWorking(4).Should().BeTrue();
        time.Now = time.Now.AddSeconds(2);

        channel.IsWorking(4).Should().BeFalse();
        push.Dispose();
    }

    [Fact]
    public void ChangedPush_ToIdle_ForgetsTheJob_AndRaisesChanged()
    {
        FakeGameSocket socket = new();
        using FurnaceChannel channel = new(socket, new ManualTime());
        socket.Push(new FurnaceChanged { ObjectId = 4, State = FurnaceStateCode.Ready, OreItemId = ItemCatalog.CopperOre });
        int version = channel.Version;
        int changed = 0;
        channel.Changed += () => changed++;

        socket.Push(new FurnaceChanged { ObjectId = 4, State = FurnaceStateCode.Idle });

        channel.StateOf(4, out _, out _).Should().Be(FurnaceStateCode.Idle);
        channel.Version.Should().NotBe(version);
        changed.Should().Be(1);
    }

    [Fact]
    public void Start_SendsTheFurnaceAndOre_AndReportsTheAnswer()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new FurnaceResult { Status = FurnaceStatus.NotEnough, ObjectId = 4 });
        using FurnaceChannel channel = new(socket, new ManualTime());
        List<FurnaceStatus> answers = [];
        channel.Completed += answers.Add;

        channel.Start(4, ItemCatalog.IronOre);

        StartSmelting request = (StartSmelting)socket.Requests[0];
        (request.ObjectId, request.OreItemId).Should().Be((4, ItemCatalog.IronOre));
        answers.Should().Equal(FurnaceStatus.NotEnough);
    }

    [Fact]
    public void Collect_SendsTheFurnace()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new FurnaceResult { Status = FurnaceStatus.Ok, ObjectId = 4 });
        using FurnaceChannel channel = new(socket, new ManualTime());

        channel.Collect(4);

        ((CollectFurnace)socket.Requests[0]).ObjectId.Should().Be(4);
    }

    [Fact]
    public async Task Finished_RaisedOnceTheBatchReachesItsReadyTime()
    {
        ManualTime time = new();
        FakeGameSocket socket = new();
        using FurnaceChannel channel = new(socket, time);
        TaskCompletionSource<int> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.Finished += id => finished.TrySetResult(id);
        FurnaceChanged push = new() { ObjectId = 7, State = FurnaceStateCode.Working, OreItemId = ItemCatalog.CopperOre, RemainingMs = 50 };

        socket.Push(push);

        (await finished.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(7);
        push.Dispose();
    }
}
