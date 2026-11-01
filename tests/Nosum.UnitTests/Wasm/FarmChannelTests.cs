// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Wasm.Socket.Farm;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class FarmChannelTests
{
    private static FarmTileRow Row(int x, int y, int crop = 0) => new() { X = (ushort)x, Y = (ushort)y, CropId = crop, PlantedAtMs = 10, GrowthDays = 2, WateredDay = 3 };

    [Fact]
    public void Load_StoresTheTilesTheMapAndTheServerClock()
    {
        FakeGameSocket socket = new();
        long serverNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;
        socket.ReplyWith(_ =>
        {
            FarmSnapshot snapshot = new() { MapId = "farm", ServerNowMs = serverNow };
            snapshot.Tiles.Add(Row(61, 17, 1));
            snapshot.Tiles.Add(Row(62, 17));
            return snapshot;
        });
        using FarmChannel channel = new(socket);

        channel.Load();

        channel.MapId.Should().Be("farm");
        channel.Count.Should().Be(2);
        using (channel.Read())
        {
            channel.TryGetTile(61, 17, out FarmTileInfo tile).Should().BeTrue();
            tile.Should().Be(new FarmTileInfo(61, 17, 1, 10, 2, 3, 0, Withered: false));
            channel.TryGetTile(63, 17, out _).Should().BeFalse();
        }

        channel.ServerNowMs().Should().BeCloseTo(serverNow, 2000, "the estimate follows the server clock, not the local one");
    }

    [Fact]
    public void Load_CarriesTheServersWitheredFlag_SoTheClientDoesNotGuessIt()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            FarmSnapshot snapshot = new() { MapId = "farm", ServerNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            FarmTileRow row = Row(61, 17, 1);
            row.Withered = true;
            snapshot.Tiles.Add(row);
            return snapshot;
        });
        using FarmChannel channel = new(socket);

        channel.Load();

        using (channel.Read())
        {
            channel.TryGetTile(61, 17, out FarmTileInfo tile).Should().BeTrue();
            tile.Withered.Should().BeTrue();
        }
    }

    [Fact]
    public void PushedChange_ReplacesTheTileInPlaceAndAddsNewOnes()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            FarmSnapshot snapshot = new() { MapId = "farm", ServerNowMs = 1 };
            snapshot.Tiles.Add(Row(61, 17));
            return snapshot;
        });
        using FarmChannel channel = new(socket);
        channel.Load();
        int changed = 0;
        channel.Changed += () => changed++;
        FarmTilesChanged push = new() { MapId = "farm", ServerNowMs = 2 };
        push.Tiles.Add(Row(61, 17, 3));
        push.Tiles.Add(Row(70, 20));

        socket.Push(push);

        channel.Count.Should().Be(2);
        using (channel.Read())
        {
            channel.TryGetTile(61, 17, out FarmTileInfo tile).Should().BeTrue();
            tile.CropId.Should().Be(3);
            channel.TryGetTile(70, 20, out _).Should().BeTrue();
        }

        changed.Should().Be(1);
        push.Dispose();
    }

    [Fact]
    public void PushedChange_KeepsTheTendedDayOfBareSoil_SoItRevertsOnTheSameDayAsOnTheServer()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new FarmSnapshot { MapId = "farm", ServerNowMs = 1 });
        using FarmChannel channel = new(socket);
        channel.Load();
        FarmTilesChanged push = new() { MapId = "farm", ServerNowMs = 2 };
        push.Tiles.Add(new FarmTileRow { X = 61, Y = 17, WateredDay = 4, TendedDay = 9 });

        socket.Push(push);

        using (channel.Read())
        {
            channel.TryGetTile(61, 17, out FarmTileInfo tile).Should().BeTrue();
            tile.TendedDay.Should().Be(9);
            tile.WateredDay.Should().Be(4);
        }

        push.Dispose();
    }

    [Fact]
    public void PushedChange_MarkedRemoved_DropsTheTileAndKeepsTheOthersReachable()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            FarmSnapshot snapshot = new() { MapId = "farm", ServerNowMs = 1 };
            snapshot.Tiles.Add(Row(1, 1));
            snapshot.Tiles.Add(Row(2, 1));
            snapshot.Tiles.Add(Row(3, 1));
            return snapshot;
        });
        using FarmChannel channel = new(socket);
        channel.Load();
        FarmTilesChanged push = new() { MapId = "farm", ServerNowMs = 2 };
        push.Tiles.Add(new FarmTileRow { X = 1, Y = 1, Removed = true });

        socket.Push(push);

        channel.Count.Should().Be(2);
        using (channel.Read())
        {
            channel.TryGetTile(1, 1, out _).Should().BeFalse();
            channel.TryGetTile(2, 1, out _).Should().BeTrue();
            channel.TryGetTile(3, 1, out FarmTileInfo moved).Should().BeTrue();
            moved.X.Should().Be(3);
        }

        push.Dispose();
    }

    [Fact]
    public void PushedChange_ForAnotherMap_IsIgnored()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new FarmSnapshot { MapId = "farm", ServerNowMs = 1 });
        using FarmChannel channel = new(socket);
        channel.Load();
        FarmTilesChanged push = new() { MapId = "town", ServerNowMs = 2 };
        push.Tiles.Add(Row(1, 1));

        socket.Push(push);

        channel.Count.Should().Be(0);
        push.Dispose();
    }

    [Fact]
    public void Load_AgainAfterAMapChange_ReplacesEverything()
    {
        FakeGameSocket socket = new();
        string map = "farm";
        socket.ReplyWith(_ =>
        {
            FarmSnapshot snapshot = new() { MapId = map, ServerNowMs = 1 };
            snapshot.Tiles.Add(Row(map == "farm" ? 1 : 2, 1));
            return snapshot;
        });
        using FarmChannel channel = new(socket);
        channel.Load();
        map = "town";

        channel.Load();

        channel.MapId.Should().Be("town");
        channel.Count.Should().Be(1);
        using (channel.Read())
        {
            channel.TryGetTile(2, 1, out _).Should().BeTrue();
            channel.TryGetTile(1, 1, out _).Should().BeFalse();
        }
    }

    [Fact]
    public void Use_SendsTheIntentAndReportsTheServersAnswer()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new UseItemResult { Status = FarmStatus.NotTilled, Action = FarmAction.None });
        using FarmChannel channel = new(socket);
        (FarmStatus Status, FarmAction Action)? answer = null;
        channel.UseCompleted += (status, action, _) => answer = (status, action);

        channel.Use(2, 61, 17);

        UseItem sent = socket.Requests.Should().ContainSingle().Subject.Should().BeOfType<UseItem>().Subject;
        (sent.Slot, sent.TileX, sent.TileY).Should().Be(((byte)2, 61, 17));
        answer.Should().Be((FarmStatus.NotTilled, FarmAction.None));
    }

    [Fact]
    public void UseArea_ReportsHowManyTilesOfTheLineWorked()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new UseItemResult { Status = FarmStatus.Ok, Action = FarmAction.Watered, Worked = 2 });
        using FarmChannel channel = new(socket);
        int? worked = null;
        channel.UseCompleted += (_, _, count) => worked = count;

        channel.UseArea(2, 61, 17, 3);

        // The client charges the energy of each tile that worked (docs/adr/ADR-080), so the count must reach the subscribers.
        worked.Should().Be(2);
    public void Use_WhenTheReplyIsLost_ReportsConflictSoThePressIsNotSilent()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith<UseItemResult>(_ => throw new System.TimeoutException("no reply"));
        using FarmChannel channel = new(socket);
        FarmStatus? status = null;
        channel.UseCompleted += (s, _) => status = s;

        channel.Use(7, 61, 17);

        status.Should().Be(FarmStatus.Conflict, "a lost use is told to be tried again (Farm_Status_Conflict), not dropped without a word");
    }

    [Fact]
    public void UseArea_WhenTheReplyIsLost_ReportsConflictSoThePressIsNotSilent()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith<UseItemResult>(_ => throw new System.TimeoutException("no reply"));
        using FarmChannel channel = new(socket);
        FarmStatus? status = null;
        channel.UseCompleted += (s, _) => status = s;

        channel.UseArea(7, 61, 17, 3);

        status.Should().Be(FarmStatus.Conflict);
    }

    [Fact]
    public void UseArea_SendsTheWideUseAsItsOwnPacket_AndReportsTheAnswer()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new UseItemResult { Status = FarmStatus.InvalidArea, Action = FarmAction.None });
        using FarmChannel channel = new(socket);
        FarmStatus? status = null;
        channel.UseCompleted += (s, _, _) => status = s;

        channel.UseArea(2, 61, 17, 3);

        UseItemArea sent = socket.Requests.Should().ContainSingle().Which.Should().BeOfType<UseItemArea>().Subject;
        (sent.Slot, sent.TileX, sent.TileY, sent.Tiles).Should().Be(((byte)2, 61, 17, (byte)3));
        status.Should().Be(FarmStatus.InvalidArea);
    }

    [Fact]
    public void ChunkState_AddsTheChunksTilesAndReplacesAnyOlderOnes()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            FarmSnapshot snapshot = new() { MapId = "farm", ServerNowMs = 1 };
            snapshot.Tiles.Add(Row(5, 5, 1));
            return snapshot;
        });
        using FarmChannel channel = new(socket);
        channel.Load();
        ChunkState state = new() { MapId = "farm", ChunkX = 4, ChunkY = 2, ServerNowMs = 2 };
        state.FarmTiles.Add(Row(69, 37, 2));
        state.FarmTiles.Add(Row(70, 37));
        ChunkState again = new() { MapId = "farm", ChunkX = 4, ChunkY = 2, ServerNowMs = 3 };
        again.FarmTiles.Add(Row(70, 37, 4));

        socket.Push(state);
        socket.Push(again);

        channel.Count.Should().Be(2, "the second state of the chunk replaces the first, the chunk (0, 0) tile stays");
        using (channel.Read())
        {
            channel.TryGetTile(5, 5, out _).Should().BeTrue();
            channel.TryGetTile(69, 37, out _).Should().BeFalse();
            channel.TryGetTile(70, 37, out FarmTileInfo tile).Should().BeTrue();
            tile.CropId.Should().Be(4);
        }

        state.Dispose();
        again.Dispose();
    }

    [Fact]
    public void ChunkLeft_DropsOnlyThatChunksTiles()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            FarmSnapshot snapshot = new() { MapId = "farm", ServerNowMs = 1 };
            snapshot.Tiles.Add(Row(5, 5));
            snapshot.Tiles.Add(Row(20, 5));
            snapshot.Tiles.Add(Row(21, 6));
            return snapshot;
        });
        using FarmChannel channel = new(socket);
        channel.Load();
        int changed = 0;
        channel.Changed += () => changed++;
        ChunkLeft left = new() { MapId = "farm", ChunkX = 1, ChunkY = 0 };

        socket.Push(left);

        channel.Count.Should().Be(1);
        using (channel.Read())
        {
            channel.TryGetTile(5, 5, out _).Should().BeTrue();
            channel.TryGetTile(20, 5, out _).Should().BeFalse();
            channel.TryGetTile(21, 6, out _).Should().BeFalse();
        }

        changed.Should().Be(1);
        left.Dispose();
    }

    [Fact]
    public void ChunkLeft_OfAnEmptyChunkOrAnotherMap_ChangesNothing()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            FarmSnapshot snapshot = new() { MapId = "farm", ServerNowMs = 1 };
            snapshot.Tiles.Add(Row(5, 5));
            return snapshot;
        });
        using FarmChannel channel = new(socket);
        channel.Load();
        int changed = 0;
        channel.Changed += () => changed++;
        ChunkLeft empty = new() { MapId = "farm", ChunkX = 3, ChunkY = 3 };
        ChunkLeft other = new() { MapId = "town", ChunkX = 0, ChunkY = 0 };

        socket.Push(empty);
        socket.Push(other);

        channel.Count.Should().Be(1);
        changed.Should().Be(0);
        empty.Dispose();
        other.Dispose();
    }

    [Fact]
    public void ChunkState_ForAnotherMap_IsIgnored()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new FarmSnapshot { MapId = "farm", ServerNowMs = 1 });
        using FarmChannel channel = new(socket);
        channel.Load();
        ChunkState state = new() { MapId = "town", ChunkX = 0, ChunkY = 0 };
        state.FarmTiles.Add(Row(1, 1));

        socket.Push(state);

        channel.Count.Should().Be(0);
        state.Dispose();
    }
}
