// Copyright (c) 2026 PPN Systems. All rights reserved.

#pragma warning disable CA2007

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Combat;
using Nosum.Contracts.Combat;
using Nosum.Domain.Bestiary;
using Nosum.Gameplay.Combat;
using Nosum.Infrastructure.Bestiary;
using Nosum.Infrastructure.Persistence;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The hunting log (docs/adr/ADR-067) against a real PostgreSQL database: kills count per species and per player, concurrent kills all count,
/// a slayer title is equipped only once its species' last milestone is reached, and a player's log never shows another player's kills.
/// </summary>
public sealed class BestiaryServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public BestiaryServiceTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task RecordKill_CountsEachSpeciesSeparately()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        BestiaryService bestiary = Create(world);

        await bestiary.RecordKillAsync(player, MonsterKind.CaveBat);
        await bestiary.RecordKillAsync(player, MonsterKind.CaveBat);
        await bestiary.RecordKillAsync(player, MonsterKind.GreenSlime);

        BestiaryView view = await bestiary.ReadAsync(player);
        view.Equipped.Should().Be(MonsterKind.None);
        view.Lines.Select(l => (l.Species, l.Kills)).Should().Equal((MonsterKind.GreenSlime, 1), (MonsterKind.CaveBat, 2));
    }

    [Fact]
    public async Task RecordKill_OfNoMonster_IsIgnored()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        BestiaryService bestiary = Create(world);

        await bestiary.RecordKillAsync(player, MonsterKind.None);

        (await bestiary.ReadAsync(player)).Lines.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordKill_ConcurrentKills_AllCount()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        BestiaryService bestiary = Create(world);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => bestiary.RecordKillAsync(player, MonsterKind.SporeShroom)));

        (await bestiary.ReadAsync(player)).Lines.Should().ContainSingle().Which.Kills.Should().Be(20);
    }

    [Fact]
    public async Task EquipTitle_BeforeTheLastMilestone_IsRefused()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        BestiaryService bestiary = Create(world);
        await SeedKillsAsync(world, player, MonsterKind.CaveBat, 99);

        BestiaryStatus status = await bestiary.EquipTitleAsync(player, MonsterKind.CaveBat);

        status.Should().Be(BestiaryStatus.NotEarned);
        (await bestiary.ReadAsync(player)).Equipped.Should().Be(MonsterKind.None);
    }

    [Fact]
    public async Task EquipTitle_AfterTheLastMilestone_IsEquipped_ThenSwitchedAndTakenOff()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        BestiaryService bestiary = Create(world);
        await SeedKillsAsync(world, player, MonsterKind.CaveBat, 100);
        await SeedKillsAsync(world, player, MonsterKind.MagmaSlime, 100);

        (await bestiary.EquipTitleAsync(player, MonsterKind.CaveBat)).Should().Be(BestiaryStatus.Ok);
        (await bestiary.ReadAsync(player)).Equipped.Should().Be(MonsterKind.CaveBat);

        (await bestiary.EquipTitleAsync(player, MonsterKind.MagmaSlime)).Should().Be(BestiaryStatus.Ok);
        (await bestiary.ReadAsync(player)).Equipped.Should().Be(MonsterKind.MagmaSlime);

        (await bestiary.EquipTitleAsync(player, MonsterKind.None)).Should().Be(BestiaryStatus.Ok);
        (await bestiary.ReadAsync(player)).Equipped.Should().Be(MonsterKind.None);

        await using NosumDbContext db = world.Db();
        (await db.EquippedBestiaryTitles.CountAsync(t => t.PlayerId == player)).Should().Be(0);
    }

    [Fact]
    public async Task EquipTitle_OfAnUnknownSpecies_IsRefused()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        BestiaryService bestiary = Create(world);

        (await bestiary.EquipTitleAsync(player, (MonsterKind)200)).Should().Be(BestiaryStatus.UnknownSpecies);
    }

    [Fact]
    public async Task Read_OfOnePlayer_NeverShowsAnotherPlayersKills()
    {
        using PlacementHarness world = new(_database);
        Guid player = await world.CreatePlayerAsync();
        Guid other = await world.CreatePlayerAsync();
        BestiaryService bestiary = Create(world);
        await bestiary.RecordKillAsync(other, MonsterKind.GoblinScout);

        (await bestiary.ReadAsync(player)).Lines.Should().BeEmpty();
        (await bestiary.ReadAsync(other)).Lines.Should().ContainSingle();
    }

    private static async Task SeedKillsAsync(PlacementHarness world, Guid player, MonsterKind species, int kills)
    {
        await using NosumDbContext db = world.Db();
        db.PlayerBestiaryEntries.Add(new PlayerBestiaryEntry { PlayerId = player, Species = species, Kills = kills, UpdatedAt = world.Clock.GetUtcNow() });
        _ = await db.SaveChangesAsync();
    }

    private static BestiaryService Create(PlacementHarness world) => new(world.Units, world.Clock);
}
