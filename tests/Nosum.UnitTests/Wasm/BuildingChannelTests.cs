// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.C2S;
using Nosum.Contracts.Building.S2C;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Wasm.Socket.Building;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The client's mirror of farm buildings and the animals of the room (docs/adr/ADR-028): what the server lists and pushes, and where an animal is while it follows a walk.</summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class BuildingChannelTests
{
    private static readonly Guid s_building = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_hen = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid s_cow = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => this.Now;
    }

    private static AnimalRow Row(Guid id, AnimalKind kind, string name, int friendship = 0, byte flags = 0, float x = 40, float y = 80, float targetX = 40, float targetY = 80, int moveMs = 0)
        => new() { Id = id, Kind = (byte)kind, Name = name, Friendship = friendship, Flags = flags, X = x, Y = y, TargetX = targetX, TargetY = targetY, MoveMs = moveMs };

    [Fact]
    public void LoadHere_StoresTheBuildings_AndTheOnesStillBeingBuiltAreUnderConstruction()
    {
        ManualTime time = new();
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            BuildingsSnapshot snapshot = new() { Mine = false };
            snapshot.Buildings.Add(new BuildingRow { Id = s_building, ObjectId = 7, Kind = (byte)BuildingKind.Coop, MapId = "farm", RemainingMs = 60_000, Animals = 0, Capacity = 4 });
            snapshot.Buildings.Add(new BuildingRow { Id = Guid.NewGuid(), ObjectId = 8, Kind = (byte)BuildingKind.Barn, MapId = "farm", RemainingMs = 0, Animals = 2, Capacity = 4 });
            return snapshot;
        });
        using BuildingChannel channel = new(socket, time);

        channel.LoadHere();

        ((GetBuildings)socket.Requests.Single()).Mine.Should().BeFalse();
        channel.IsUnderConstruction(7).Should().BeTrue();
        channel.IsUnderConstruction(8).Should().BeFalse("a finished building is drawn as it is");
        channel.IsUnderConstruction(99).Should().BeFalse("not a building");
        time.Now = time.Now.AddSeconds(61);
        channel.IsUnderConstruction(7).Should().BeFalse("the clock moved past the finishing time");
    }

    [Fact]
    public void LoadMine_KeepsTheListSeparate_ForTheCarpenterPage()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            BuildingsSnapshot snapshot = new() { Mine = true };
            snapshot.Buildings.Add(new BuildingRow { Id = s_building, ObjectId = 7, Kind = (byte)BuildingKind.Coop, MapId = "farm", RemainingMs = 0, Animals = 1, Capacity = 4 });
            return snapshot;
        });
        using BuildingChannel channel = new(socket);

        channel.LoadMine();

        ((GetBuildings)socket.Requests.Single()).Mine.Should().BeTrue();
        channel.MyBuildings().Should().ContainSingle().Which.Should().BeEquivalentTo(new { Id = s_building, ObjectId = 7, Kind = (byte)BuildingKind.Coop, Animals = 1, Capacity = 4 });
        channel.IsUnderConstruction(7).Should().BeFalse("the land's list was not loaded");
    }

    [Fact]
    public void APushOfTheLandsBuildings_ReplacesTheList_AndTellsTheListeners()
    {
        FakeGameSocket socket = new();
        using BuildingChannel channel = new(socket);
        int changes = 0;
        channel.Changed += () => changes++;
        BuildingsSnapshot push = new() { Mine = false };
        push.Buildings.Add(new BuildingRow { Id = s_building, ObjectId = 7, Kind = (byte)BuildingKind.Coop, MapId = "farm", RemainingMs = 5_000, Capacity = 4 });

        socket.Push(push);

        changes.Should().Be(1);
        channel.IsUnderConstruction(7).Should().BeTrue();
        push.Dispose();
    }

    [Fact]
    public void LoadRoom_StoresTheRoom_AndAnAnimalStandingIsWhereTheServerPutIt()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            AnimalsSnapshot room = new() { BuildingId = s_building, Kind = (byte)BuildingKind.Coop, Rations = 3, MayTend = true };
            room.Animals.Add(Row(s_hen, AnimalKind.Chicken, "Bella", friendship: 120, flags: AnimalView_Produce, x: 56, y: 90, targetX: 56, targetY: 90));
            return room;
        });
        using BuildingChannel channel = new(socket);

        channel.LoadRoom();

        (channel.RoomId, channel.RoomKind, channel.Rations, channel.MayTend).Should().Be((s_building, (byte)BuildingKind.Coop, 3, true));
        using (channel.Read())
        {
            channel.AnimalCount.Should().Be(1);
            AnimalPose pose = channel.AnimalAt(0, channel.NowMs);
            pose.Should().BeEquivalentTo(new { Id = s_hen, Name = "Bella", Friendship = 120, HasProduce = true, Petted = false, X = 56.0, Y = 90.0, Walking = false });
        }

        channel.NameOf(s_hen).Should().Be("Bella");
        channel.NameOf(Guid.NewGuid()).Should().BeEmpty();
    }

    private const byte AnimalView_Produce = 2;

    [Fact]
    public void ARoomThatIsNotABuilding_LeavesTheChannelEmpty()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new AnimalsSnapshot { BuildingId = Guid.Empty });
        using BuildingChannel channel = new(socket);

        channel.LoadRoom();

        channel.RoomId.Should().Be(Guid.Empty);
        channel.RoomKind.Should().Be(0);
        using (channel.Read())
        {
            channel.AnimalCount.Should().Be(0);
        }
    }

    [Fact]
    public void AnimalAt_OnAWalk_MovesInAStraightLineAndStopsAtTheTarget()
    {
        ManualTime time = new();
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            AnimalsSnapshot room = new() { BuildingId = s_building, Kind = (byte)BuildingKind.Coop, MayTend = true };
            room.Animals.Add(Row(s_hen, AnimalKind.Chicken, "Bella", x: 32, y: 64, targetX: 32, targetY: 64));
            return room;
        });
        using BuildingChannel channel = new(socket, time);
        channel.LoadRoom();
        AnimalsMoved walk = new() { BuildingId = s_building };
        walk.Moves.Add(new AnimalMoveRow { Id = s_hen, FromX = 32, FromY = 64, ToX = 96, ToY = 64, DurationMs = 4_000 });

        socket.Push(walk);

        using (channel.Read())
        {
            AnimalPose start = channel.AnimalAt(0, channel.NowMs);
            (start.X, start.Y, start.Walking, start.Facing).Should().Be((32.0, 64.0, true, 1));
            time.Now = time.Now.AddSeconds(1);
            AnimalPose quarter = channel.AnimalAt(0, channel.NowMs);
            (quarter.X, quarter.Y).Should().Be((48.0, 64.0));
            time.Now = time.Now.AddSeconds(3);
            AnimalPose arrived = channel.AnimalAt(0, channel.NowMs);
            (arrived.X, arrived.Walking).Should().Be((96.0, false));
        }

        walk.Dispose();
    }

    [Theory]
    [InlineData(10, 0, 1)]
    [InlineData(-10, 0, 3)]
    [InlineData(0, 10, 0)]
    [InlineData(0, -10, 2)]
    public void AnimalAt_FacesTheWayItWalks(float dx, float dy, int facing)
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            AnimalsSnapshot room = new() { BuildingId = s_building, Kind = (byte)BuildingKind.Barn };
            room.Animals.Add(Row(s_cow, AnimalKind.Cow, "Rosie", x: 100, y: 100, targetX: 100 + dx, targetY: 100 + dy, moveMs: 5_000));
            return room;
        });
        using BuildingChannel channel = new(socket);
        channel.LoadRoom();

        using (channel.Read())
        {
            channel.AnimalAt(0, channel.NowMs).Facing.Should().Be(facing);
        }
    }

    [Fact]
    public void APushForAnotherBuilding_IsIgnored_AndOneForTheRoomUpdatesTheBenchAndTheCards()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            AnimalsSnapshot room = new() { BuildingId = s_building, Kind = (byte)BuildingKind.Coop, MayTend = true };
            room.Animals.Add(Row(s_hen, AnimalKind.Chicken, "Bella"));
            return room;
        });
        using BuildingChannel channel = new(socket);
        channel.LoadRoom();
        AnimalsChanged elsewhere = new() { BuildingId = Guid.NewGuid(), Rations = 9 };
        AnimalsChanged here = new() { BuildingId = s_building, Rations = 4 };
        here.Upserted.Add(Row(s_hen, AnimalKind.Chicken, "Bella", friendship: 40, flags: 1));
        here.Upserted.Add(Row(s_cow, AnimalKind.Cow, "Rosie"));

        socket.Push(elsewhere);
        channel.Rations.Should().Be(0);
        socket.Push(here);

        channel.Rations.Should().Be(4);
        using (channel.Read())
        {
            channel.AnimalCount.Should().Be(2);
            channel.AnimalAt(0, channel.NowMs).Should().BeEquivalentTo(new { Id = s_hen, Friendship = 40, Petted = true });
        }

        AnimalsChanged left = new() { BuildingId = s_building, Rations = 4 };
        left.Removed.Add(s_cow);
        socket.Push(left);
        using (channel.Read())
        {
            channel.AnimalCount.Should().Be(1);
        }

        elsewhere.Dispose();
        here.Dispose();
        left.Dispose();
    }

    [Fact]
    public void ClearRoom_ForgetsTheAnimals()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            AnimalsSnapshot room = new() { BuildingId = s_building, Kind = (byte)BuildingKind.Coop, MayTend = true };
            room.Animals.Add(Row(s_hen, AnimalKind.Chicken, "Bella"));
            return room;
        });
        using BuildingChannel channel = new(socket);
        channel.LoadRoom();

        channel.ClearRoom();

        channel.RoomId.Should().Be(Guid.Empty);
        channel.MayTend.Should().BeFalse();
        using (channel.Read())
        {
            channel.AnimalCount.Should().Be(0);
        }
    }

    [Fact]
    public void TryFindNear_PicksTheNearestAnimalWithinTheDistance()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            AnimalsSnapshot room = new() { BuildingId = s_building, Kind = (byte)BuildingKind.Coop, MayTend = true };
            room.Animals.Add(Row(s_hen, AnimalKind.Chicken, "Bella", x: 100, y: 100, targetX: 100, targetY: 100));
            room.Animals.Add(Row(s_cow, AnimalKind.Cow, "Rosie", x: 120, y: 100, targetX: 120, targetY: 100));
            return room;
        });
        using BuildingChannel channel = new(socket);
        channel.LoadRoom();

        using (channel.Read())
        {
            channel.TryFindNear(108, 100, 30, channel.NowMs, out AnimalPose nearest).Should().BeTrue();
            nearest.Id.Should().Be(s_hen, "8 pixels away beats 12");
            channel.TryFindNear(400, 400, 30, channel.NowMs, out _).Should().BeFalse();
        }
    }

    [Fact]
    public void Requests_CarryWhatTheyNeed_AndTheAnswersAreAnnounced()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new BuildingResult { Status = BuildingStatus.Ok, ItemId = 610, Id = s_hen });
        socket.ReplyWith(_ => new AnimalResult { Status = BuildingStatus.AlreadyPetted, Friendship = 40 });
        socket.ReplyWith(_ => new BuildingsSnapshot { Mine = true });
        using BuildingChannel channel = new(socket);
        System.Collections.Generic.List<BuildingResultInfo> answers = [];
        channel.Completed += answers.Add;

        channel.Commission((byte)BuildingKind.Coop);
        channel.BuyAnimal(s_building, (byte)AnimalKind.Chicken, "Bella");
        channel.Feed(3, 4);
        channel.Pet(s_hen);
        channel.Collect(s_hen, 5);

        socket.Requests.OfType<CommissionBuilding>().Single().Kind.Should().Be((byte)BuildingKind.Coop);
        BuyAnimal buy = socket.Requests.OfType<BuyAnimal>().Single();
        (buy.BuildingId, buy.Kind, buy.Name).Should().Be((s_building, (byte)AnimalKind.Chicken, "Bella"));
        FeedAnimals feed = socket.Requests.OfType<FeedAnimals>().Single();
        (feed.Slot, feed.Quantity).Should().Be((3, 4));
        socket.Requests.OfType<PetAnimal>().Single().AnimalId.Should().Be(s_hen);
        CollectProduce collect = socket.Requests.OfType<CollectProduce>().Single();
        (collect.AnimalId, collect.Slot).Should().Be((s_hen, 5));
        answers.Select(a => a.Action).Should().Equal(BuildingAction.Commission, BuildingAction.BuyAnimal, BuildingAction.Feed, BuildingAction.Pet, BuildingAction.Collect);
        answers[0].Should().BeEquivalentTo(new { Status = BuildingStatus.Ok, ItemId = 610 });
        socket.Requests.OfType<GetBuildings>().Should().ContainSingle(g => g.Mine, "a bought animal refreshes the list on the carpenter page");
    }
}
