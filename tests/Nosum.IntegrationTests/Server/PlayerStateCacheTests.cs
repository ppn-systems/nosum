// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Progress;
using Nosum.Core;
using Nosum.Domain.Accounts;
using Nosum.Domain.Inventory;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Skills;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Progress;
using Xunit;
using DomainPlayerState = Nosum.Domain.Accounts.PlayerState;

namespace Nosum.IntegrationTests.Server;

/// <summary>The shared copy of the player_states row (docs/adr/ADR-035 D4): energy checks are serialized, the place and progress writers do not overwrite each other, and a save that did not happen leaves no trace.</summary>
public sealed class PlayerStateCacheTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly PlayerStateCache _cache = new();
    private readonly PlayerProgressService _progress;
    private readonly PlayerStateService _places;

    public PlayerStateCacheTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _progress = new PlayerProgressService(_units, new FixedDay(5), new InventoryService(_units, _clock), _clock, null, _cache);
        _places = new PlayerStateService(_units, _clock, _cache);
    }

    [Fact]
    public async Task TryChargeAsync_TwoQuickSwingsWithOneEnergyLeft_OnlyOnePasses()
    {
        Guid player = await CreatePlayerAsync(energy: 1, farmingXp: SkillRules.XpCap);
        EnergyRules.Cost(ToolKind.Hoe, SkillRules.LevelOf(SkillRules.XpCap)).Should().Be(1, "this test needs a one-energy swing");

        ChargeStatus[] results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => _progress.TryChargeAsync(player, ToolKind.Hoe, SkillKind.Farming, 5)));

        results.Count(r => r == ChargeStatus.Ok).Should().Be(1);
        results.Count(r => r == ChargeStatus.NoEnergy).Should().Be(1);
        await AssertCacheEqualsRowAsync(player);
        (await RowAsync(player)).Energy.Should().Be(0);
    }

    [Fact]
    public async Task TryChargeAsync_ManySwingsAtOnce_NeverSpendMoreThanThePlayerHas()
    {
        Guid player = await CreatePlayerAsync(energy: 7, farmingXp: SkillRules.XpCap);

        ChargeStatus[] results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => _progress.TryChargeAsync(player, ToolKind.Hoe, SkillKind.Farming, 5)));

        results.Count(r => r == ChargeStatus.Ok).Should().Be(7);
        DomainPlayerState row = await RowAsync(player);
        row.Energy.Should().Be(0);
        row.FarmingXp.Should().Be(SkillRules.XpCap, "experience stays capped");
        await AssertCacheEqualsRowAsync(player);
    }

    [Fact]
    public async Task ChargeAsync_DisposedWithoutBeingSaved_LeavesTheCopyAndTheRowAsTheyWere()
    {
        Guid player = await CreatePlayerAsync(energy: 40, farmingXp: 0);
        _ = await _progress.GetAsync(player);

        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            using IProgressCharge charge = await _progress.ChargeAsync(unit, player, ToolKind.Hoe, SkillKind.Farming, 5);
            charge.Allowed.Should().BeTrue();
        }

        (await _progress.GetAsync(player)).Energy.Should().Be(40);
        (await RowAsync(player)).Energy.Should().Be(40);
    }

    [Fact]
    public async Task ChargeAsync_SaveFails_DropsTheCopySoTheNextReadComesFromTheRow()
    {
        Guid player = await CreatePlayerAsync(energy: 40, farmingXp: 0);
        _ = await _progress.GetAsync(player);
        await SetRowAsync(player, energy: 33); // behind the copy's back

        Guid duplicate = Guid.NewGuid();
        await using (NosumDbContext db = new(_database.Options))
        {
            db.InventoryLog.Add(new InventoryLogEntry { Id = duplicate, PlayerId = player, ItemId = 0, Delta = 0, Reason = "first", OccurredAt = s_start });
            _ = await db.SaveChangesAsync();
        }

        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            using IProgressCharge charge = await _progress.ChargeAsync(unit, player, ToolKind.Hoe, SkillKind.Farming, 5);
            unit.Repository<InventoryLogEntry>().Add(new InventoryLogEntry { Id = duplicate, PlayerId = player, ItemId = 0, Delta = 0, Reason = "second", OccurredAt = s_start });
            Func<Task> save = () => unit.SaveChangesAsync(CancellationToken.None);
            _ = await save.Should().ThrowAsync<Exception>();
        }

        (await _progress.GetAsync(player)).Energy.Should().Be(33, "the copy was dropped and the row read again");
        await AssertCacheEqualsRowAsync(player);
    }

    [Fact]
    public async Task ChargeAsync_SavedAndPublished_MovesTheCopyWithTheRow()
    {
        Guid player = await CreatePlayerAsync(energy: 40, farmingXp: 0);
        _ = await _progress.GetAsync(player);

        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            using IProgressCharge charge = await _progress.ChargeAsync(unit, player, ToolKind.Hoe, SkillKind.Farming, 5);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            await charge.PublishAsync();
        }

        PlayerProgressView view = await _progress.GetAsync(player);
        view.Energy.Should().Be(37);
        view.FarmingXp.Should().Be(5);
        await AssertCacheEqualsRowAsync(player);
    }

    [Fact]
    public async Task SaveAsync_PlaceAndProgressInterleaved_NeitherOverwritesTheOther()
    {
        Guid player = await CreatePlayerAsync(energy: 40, farmingXp: 0);
        _ = await _places.LoadAsync(player, CancellationToken.None);

        _ = await _progress.TryChargeAsync(player, ToolKind.Hoe, SkillKind.Farming, 5);
        await _places.SaveAsync(player, new PlayerStateRow("Town", 3, 4, Facing.Up), CancellationToken.None);
        _ = await _progress.TryChargeAsync(player, ToolKind.Hoe, SkillKind.Farming, 5);
        await _places.SaveAsync(player, new PlayerStateRow("Farm", 9, 8, Facing.Left), CancellationToken.None);

        DomainPlayerState row = await RowAsync(player);
        (row.MapName, row.X, row.Y, row.Facing).Should().Be(("Farm", 9d, 8d, Facing.Left));
        (row.Energy, row.FarmingXp).Should().Be((34, 10));
        (await _places.LoadAsync(player, CancellationToken.None)).Should().Be(new PlayerStateRow("Farm", 9, 8, Facing.Left));
        await AssertCacheEqualsRowAsync(player);
    }

    [Fact]
    public async Task SaveAsync_PlayerWithoutARow_CreatesItAndALaterChargeUpdatesIt()
    {
        Guid player = await CreatePlayerAsync(energy: null, farmingXp: 0);
        (await _places.LoadAsync(player, CancellationToken.None)).Should().BeNull();

        await _places.SaveAsync(player, new PlayerStateRow("Farm", 1, 2, Facing.Down), CancellationToken.None);
        ChargeStatus charged = await _progress.TryChargeAsync(player, ToolKind.Hoe, SkillKind.Farming, 5);

        charged.Should().Be(ChargeStatus.Ok);
        (await _places.LoadAsync(player, CancellationToken.None)).Should().Be(new PlayerStateRow("Farm", 1, 2, Facing.Down));
        await AssertCacheEqualsRowAsync(player);
    }

    [Fact]
    public async Task TryChargeAsync_PlayerWithoutARow_CreatesItWithoutASavedPlace()
    {
        Guid player = await CreatePlayerAsync(energy: null, farmingXp: 0);

        ChargeStatus charged = await _progress.TryChargeAsync(player, ToolKind.Hoe, SkillKind.Farming, 5);

        charged.Should().Be(ChargeStatus.Ok);
        (await RowAsync(player)).MapName.Should().BeEmpty("a row made by a charge has no saved place");
        await AssertCacheEqualsRowAsync(player);
    }

    [Fact]
    public async Task Forget_NextReadComesFromTheRow()
    {
        Guid player = await CreatePlayerAsync(energy: 40, farmingXp: 0);
        _ = await _progress.GetAsync(player);
        await SetRowAsync(player, energy: 12);

        (await _progress.GetAsync(player)).Energy.Should().Be(40, "answered from memory");
        _cache.Forget(player);

        (await _progress.GetAsync(player)).Energy.Should().Be(12);
    }

    [Fact]
    public async Task Leave_ThenSessionEnded_EvictsTheCopy()
    {
        Guid player = await CreatePlayerAsync(energy: 40, farmingXp: 0);
        _ = await _places.LoadAsync(player, CancellationToken.None);
        _cache.DescribeHeld(player).Should().NotBeNull("the place was read into memory");

        await _places.LeaveAsync(player, new PlayerStateRow("Farm", 10, 20, Facing.Down), CancellationToken.None);
        _cache.Forget(player);

        _cache.DescribeHeld(player).Should().BeNull("a player who left keeps no copy");
        (await RowAsync(player)).MapName.Should().Be("Farm", "the leave still wrote the place through");
    }

    [Fact]
    public async Task SessionEnded_ThenLeave_EvictsTheCopy()
    {
        Guid player = await CreatePlayerAsync(energy: 40, farmingXp: 0);
        _ = await _places.LoadAsync(player, CancellationToken.None);

        _cache.Forget(player);
        await _places.LeaveAsync(player, new PlayerStateRow("Farm", 10, 20, Facing.Down), CancellationToken.None);

        _cache.DescribeHeld(player).Should().BeNull("the leave evicts whatever the session end left behind");
        (await RowAsync(player)).MapName.Should().Be("Farm");
    }

    [Fact]
    public async Task Leave_WithoutACopy_DoesNotPutOneBack()
    {
        Guid player = await CreatePlayerAsync(energy: 40, farmingXp: 0);

        await _places.LeaveAsync(player, new PlayerStateRow("Farm", 10, 20, Facing.Down), CancellationToken.None);

        _cache.DescribeHeld(player).Should().BeNull("the save of a player who left is written straight to the row");
    }

    private async Task AssertCacheEqualsRowAsync(Guid player)
    {
        PlayerProgressView cached = await _progress.GetAsync(player);
        PlayerStateCache fresh = new();
        PlayerProgressView rows = await new PlayerProgressService(_units, new FixedDay(5), new InventoryService(_units, _clock), _clock, null, fresh).GetAsync(player);
        cached.Should().Be(rows);
        PlayerStateRow? place = await _places.LoadAsync(player, CancellationToken.None);
        PlayerStateRow? placeFromRow = await new PlayerStateService(_units, _clock, fresh).LoadAsync(player, CancellationToken.None);
        place.Should().Be(placeFromRow);
    }

    private async Task<DomainPlayerState> RowAsync(Guid player)
    {
        await using NosumDbContext db = new(_database.Options);
        return await db.PlayerStates.AsNoTracking().SingleAsync(s => s.PlayerId == player);
    }

    private async Task SetRowAsync(Guid player, int energy)
    {
        await using NosumDbContext db = new(_database.Options);
        _ = await db.PlayerStates.Where(s => s.PlayerId == player).ExecuteUpdateAsync(s => s.SetProperty(x => x.Energy, energy));
    }

    private async Task<Guid> CreatePlayerAsync(int? energy, int farmingXp)
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            Player player = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start });
            unit.Repository<Player>().Add(player);
            if (energy is { } value)
            {
                unit.Repository<DomainPlayerState>().Add(new DomainPlayerState { PlayerId = player.Id, MapName = "Farm", Energy = value, EnergyRefilledDay = 5, FarmingXp = farmingXp, UpdatedAt = s_start });
            }

            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }

    private sealed class FixedDay(long today) : IGameDayClock
    {
        public long Today => today;

        public TimeSpan DayLength => TimeSpan.FromMinutes(20);
    }
}
