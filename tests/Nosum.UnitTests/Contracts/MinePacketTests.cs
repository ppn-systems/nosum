// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Mine;
using Nosum.Contracts.Mine.C2S;
using Nosum.Contracts.Mine.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Xunit;

namespace Nosum.UnitTests.Contracts;

/// <summary>The packets of tool tiers, the blacksmith and the mine (docs/adr/ADR-026 D5): round trips, and that every opcode is new, unique and inside its band.</summary>
public sealed class MinePacketTests
{
    [Fact]
    public void UseItemArea_RoundTrips_AndTheOldUseItemIsUntouched()
    {
        using UseItemArea packet = new() { Slot = 3, TileX = 64, TileY = 20, Tiles = 5 };

        UseItemArea copy = UseItemArea.Deserialize(packet.Serialize());

        (copy.Slot, copy.TileX, copy.TileY, copy.Tiles).Should().Be(((byte)3, 64, 20, (byte)5));
        FarmOpCode.UseItem.Should().Be(0x3000);
        FarmOpCode.UseItemArea.Should().Be(0x3002);

        // The old packet keeps its three fields: a wide use is a packet of its own, so the protocol version stays.
        using UseItem old = new() { Slot = 1, TileX = 2, TileY = 3 };
        UseItem oldCopy = UseItem.Deserialize(old.Serialize());
        (oldCopy.Slot, oldCopy.TileX, oldCopy.TileY).Should().Be(((byte)1, 2, 3));
    }

    [Fact]
    public void BlacksmithPackets_RoundTrip()
    {
        using GetBlacksmith get = new();
        using UpgradeTool upgrade = new() { ItemId = ItemCatalog() };
        using CollectTool collect = new();
        using BlacksmithState state = new() { Status = BlacksmithStatus.NotReady, ToolItemId = 10, ReadyDay = 4_000_000_000L, Today = 3_999_999_999L };

        UpgradeTool.Deserialize(upgrade.Serialize()).ItemId.Should().Be(ItemCatalog());
        BlacksmithState copy = BlacksmithState.Deserialize(state.Serialize());
        (copy.Status, copy.ToolItemId, copy.ReadyDay, copy.Today).Should().Be((BlacksmithStatus.NotReady, 10, 4_000_000_000L, 3_999_999_999L));
        GetBlacksmith.Deserialize(get.Serialize()).Should().NotBeNull();
        CollectTool.Deserialize(collect.Serialize()).Should().NotBeNull();
    }

    [Fact]
    public void MinePackets_RoundTrip()
    {
        using GetMine get = new();
        using RideElevator ride = new() { Floor = 35 };
        using MineState state = new() { Status = MineStatus.NotUnlocked, Floor = 12, Deepest = 40 };

        RideElevator.Deserialize(ride.Serialize()).Floor.Should().Be(35);
        MineState copy = MineState.Deserialize(state.Serialize());
        (copy.Status, copy.Floor, copy.Deepest).Should().Be((MineStatus.NotUnlocked, 12, 40));
        GetMine.Deserialize(get.Serialize()).Should().NotBeNull();
    }

    [Fact]
    public void Opcodes_AreUnique_InsideTheirBands_AndTheShopBandKeepsItsOldValues()
    {
        ushort[] shop = Constants(typeof(ShopOpCode));
        ushort[] mine = Constants(typeof(MineOpCode));

        shop.Should().OnlyHaveUniqueItems().And.OnlyContain(o => o >= 0xC000 && o <= 0xC0FF);
        mine.Should().OnlyHaveUniqueItems().And.OnlyContain(o => o >= 0xC100 && o <= 0xC1FF);
        ShopOpCode.BuyFromShop.Should().Be(0xC000);
        ShopOpCode.ShopResult.Should().Be(0xC040);
        new[] { GetBlacksmith.StaticOpCode, UpgradeTool.StaticOpCode, CollectTool.StaticOpCode }.Should().OnlyContain(o => o >= 0xC001 && o <= 0xC03F);
        BlacksmithState.StaticOpCode.Should().Be(0xC041);
        new[] { GetMine.StaticOpCode, RideElevator.StaticOpCode }.Should().OnlyContain(o => o >= 0xC100 && o <= 0xC13F);
        MineState.StaticOpCode.Should().Be(0xC140);
    }

    [Fact]
    public void AppendedEnumValues_KeepTheOldOnes()
    {
        ((byte)FarmStatus.NoEnergy).Should().Be(21);
        ((byte)FarmStatus.InvalidArea).Should().Be(22);
        ((byte)FarmAction.PlantedTree).Should().Be(9);
        ((byte)FarmAction.Descended).Should().Be(10);
        ((byte)ShopStatus.OutOfSeason).Should().Be(8);
        Enum_Values<BlacksmithStatus>().Should().Equal(Enumerable.Range(0, 11).Select(i => (byte)i));
        Enum_Values<MineStatus>().Should().Equal(Enumerable.Range(0, 5).Select(i => (byte)i));
    }

    private static int ItemCatalog() => Nosum.Gameplay.Items.ItemCatalog.Hoe;

    private static ushort[] Constants(System.Type type)
        => [.. type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (ushort)f.GetRawConstantValue()!)];

    private static IEnumerable<byte> Enum_Values<T>()
        where T : struct, System.Enum
        => System.Enum.GetValues<T>().Select(v => System.Convert.ToByte(v, System.Globalization.CultureInfo.InvariantCulture)).Order();
}
