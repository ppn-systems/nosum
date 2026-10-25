// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Maps;
using Nosum.Infrastructure.Presence;
using Xunit;

namespace Nosum.UnitTests.Maps;

/// <summary>Instance keys: the farm and farmhouse are one copy per player, everything else is shared (docs/adr/ADR-015).</summary>
public sealed class MapInstanceTests
{
    private static readonly Guid s_alice = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_bob = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData(GameMaps.Farm)]
    [InlineData(GameMaps.FarmHouse)]
    public void Of_PrivateMap_GivesEachPlayerTheirOwnKey(string mapId)
    {
        MapInstance alice = MapInstance.Of(mapId, s_alice);
        MapInstance bob = MapInstance.Of(mapId, s_bob);

        alice.IsPrivate.Should().BeTrue();
        alice.Key.Should().NotBe(bob.Key);
        alice.Key.Should().Be(mapId + "@11111111111111111111111111111111");
        alice.MapId.Should().Be(bob.MapId);
        alice.Should().Be(MapInstance.Of(mapId, s_alice));
    }

    [Theory]
    [InlineData(GameMaps.BusStop)]
    [InlineData(GameMaps.Town)]
    [InlineData(GameMaps.Forest)]
    [InlineData(GameMaps.Backwoods)]
    public void Of_PublicMap_IsOneSharedInstanceWhoseKeyIsTheMapId(string mapId)
    {
        MapInstance alice = MapInstance.Of(mapId, s_alice);
        MapInstance bob = MapInstance.Of(mapId, s_bob);

        alice.IsPrivate.Should().BeFalse();
        alice.Should().Be(bob);
        alice.Key.Should().Be(mapId, "code written against map ids keeps addressing the same group");
    }

    [Fact]
    public void MapGroup_NamesDifferPerInstance_SoOnePlayersFarmIsNeverAnothers()
    {
        MapGroup.Name(MapInstance.Of(GameMaps.Farm, s_alice).Key).Should().NotBe(MapGroup.Name(MapInstance.Of(GameMaps.Farm, s_bob).Key));
        MapGroup.Name(MapInstance.Of(GameMaps.BusStop, s_alice).Key).Should().Be(MapGroup.Name(MapInstance.Of(GameMaps.BusStop, s_bob).Key));
    }

    [Fact]
    public void OwnerOnlyAccessPolicy_PrivateInstance_AdmitsOnlyItsOwner()
    {
        OwnerOnlyAccessPolicy policy = new();

        policy.CanEnter(MapInstance.Of(GameMaps.FarmHouse, s_alice), s_alice).Should().BeTrue();
        policy.CanEnter(MapInstance.Of(GameMaps.FarmHouse, s_alice), s_bob).Should().BeFalse();
        policy.CanEnter(MapInstance.Of(GameMaps.Town, s_alice), s_bob).Should().BeTrue();
    }

    [Fact]
    public void Shared_EmptyMapId_Throws()
    {
        Action create = () => MapInstance.Shared(string.Empty);

        create.Should().Throw<ArgumentException>();
    }
}
