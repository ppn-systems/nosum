// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Furnace;
using Nosum.Wasm.ViewModels.Furnace;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class FurnaceViewModelTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this.Now;
    }

    private static (FurnaceViewModel Vm, FakeGameSocket Socket, ManualTime Time) Create(params (int Slot, int ItemId, int Quantity)[] stacks)
    {
        FakeGameSocket socket = new();
        Nosum.Wasm.Socket.Inventory.InventoryChannel inventory = LoadedInventory(socket, stacks);
        ManualTime time = new();
        FurnaceChannel furnaces = new(socket, time);
        FurnaceViewModel vm = new(furnaces, inventory, Describer(), Text());
        vm.Open(4);
        return (vm, socket, time);
    }

    [Fact]
    public void Ores_AnIdleFurnaceWithFiveOreAndCoal_CanStart()
    {
        (FurnaceViewModel vm, _, _) = Create((0, ItemCatalog.CopperOre, 5), (1, ItemCatalog.Coal, 1));

        OreChoice copper = vm.Ores.Single(o => o.Item.ItemId == ItemCatalog.CopperOre);
        OreChoice iron = vm.Ores.Single(o => o.Item.ItemId == ItemCatalog.IronOre);

        copper.CanStart.Should().BeTrue();
        iron.CanStart.Should().BeFalse("there is no iron ore");
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(5, 0)]
    public void Ores_TooLittleOreOrNoCoal_CannotStart(int ore, int coal)
    {
        (FurnaceViewModel vm, _, _) = Create((0, ItemCatalog.CopperOre, ore), (1, ItemCatalog.Coal, coal));

        vm.Ores.Single(o => o.Item.ItemId == ItemCatalog.CopperOre).CanStart.Should().BeFalse();
    }

    [Fact]
    public void Start_WhenPossible_SendsTheFurnaceAndOre()
    {
        (FurnaceViewModel vm, FakeGameSocket socket, _) = Create((0, ItemCatalog.CopperOre, 10), (1, ItemCatalog.Coal, 2));
        socket.ReplyWith(_ => new FurnaceResult { Status = FurnaceStatus.Ok, ObjectId = 4 });

        vm.Start(ItemCatalog.CopperOre);

        StartSmelting request = (StartSmelting)socket.Requests.Last();
        (request.ObjectId, request.OreItemId).Should().Be((4, ItemCatalog.CopperOre));
    }

    [Fact]
    public void Start_WhenTheRulesSayNo_SendsNothing()
    {
        (FurnaceViewModel vm, FakeGameSocket socket, _) = Create((0, ItemCatalog.CopperOre, 2));
        int before = socket.Requests.Count;

        vm.Start(ItemCatalog.CopperOre);

        socket.Requests.Count.Should().Be(before);
    }

    [Fact]
    public void Working_ShowsTheCountdown_AndDisablesStarting()
    {
        using CultureScope scope = new("en");
        (FurnaceViewModel vm, FakeGameSocket socket, ManualTime time) = Create((0, ItemCatalog.CopperOre, 10), (1, ItemCatalog.Coal, 2));
        socket.Push(new FurnaceChanged { ObjectId = 4, State = FurnaceStateCode.Working, OreItemId = ItemCatalog.CopperOre, RemainingMs = 125_000 });

        vm.State.Should().Be(FurnaceStateCode.Working);
        vm.StateText.Should().Be("Smelting Copper ore, 2:05 left.");
        vm.Ores.Should().OnlyContain(o => !o.CanStart);

        time.Now = time.Now.AddSeconds(126);
        vm.State.Should().Be(FurnaceStateCode.Ready);
        vm.StateText.Should().Be("The bar is done!");
    }

    [Fact]
    public void Collect_OnlyWhenReady()
    {
        (FurnaceViewModel vm, FakeGameSocket socket, ManualTime time) = Create();
        socket.ReplyWith(_ => new FurnaceResult { Status = FurnaceStatus.Ok, ObjectId = 4 });
        socket.Push(new FurnaceChanged { ObjectId = 4, State = FurnaceStateCode.Working, OreItemId = ItemCatalog.CopperOre, RemainingMs = 5000 });

        vm.Collect();
        socket.Requests.OfType<CollectFurnace>().Should().BeEmpty("the bar is not ready");

        time.Now = time.Now.AddSeconds(6);
        vm.Collect();

        socket.Requests.OfType<CollectFurnace>().Single().ObjectId.Should().Be(4);
    }

    [Fact]
    public void ARefusalToStart_ShowsItsText()
    {
        using CultureScope scope = new("en");
        (FurnaceViewModel vm, FakeGameSocket socket, _) = Create((0, ItemCatalog.CopperOre, 10), (1, ItemCatalog.Coal, 2));
        socket.ReplyWith(_ => new FurnaceResult { Status = FurnaceStatus.Busy, ObjectId = 4 });

        vm.Start(ItemCatalog.CopperOre);

        vm.Status.Should().Be("The furnace already has a batch.");
        vm.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void ARefusalAsOutOfReach_ClosesThePage()
    {
        (FurnaceViewModel vm, FakeGameSocket socket, _) = Create((0, ItemCatalog.CopperOre, 10), (1, ItemCatalog.Coal, 2));
        List<bool> changes = [];
        vm.OpenChanged += changes.Add;
        socket.ReplyWith(_ => new FurnaceResult { Status = FurnaceStatus.OutOfReach, ObjectId = 4 });

        vm.Start(ItemCatalog.CopperOre);

        vm.IsOpen.Should().BeFalse();
        changes.Should().Equal(false);
    }
}
