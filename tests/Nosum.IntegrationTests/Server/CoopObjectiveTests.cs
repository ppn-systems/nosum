// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.S2C;
using Nosum.Domain.Accounts;
using Nosum.Domain.Coops;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Coops;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Farm;
using Nosum.Infrastructure.GroundItems;
using Nosum.Infrastructure.Inventory;
using Nosum.Infrastructure.Maps;
using Nosum.Infrastructure.Nodes;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Progress;
using Nosum.Infrastructure.Quests;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Shared co-op objectives (docs/adr/ADR-067) against a real PostgreSQL database: harvests of any member add to the co-op's ledger, a harvest no objective counts
/// changes nothing, the trophy is earned once when the target is reached, and every online member is sent the new progress.
/// </summary>
public sealed class CoopObjectiveTests : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly DateTimeOffset s_start = new(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly TmxMapCatalog s_maps = new(GameMaps.All, Path.Combine(AppContext.BaseDirectory, "maps"));
    private static readonly CoopObjectiveDefinition s_parsnip = CoopObjectiveCatalog.All.Single(o => o.ItemId == ItemCatalog.Parsnip);

    private readonly PostgresFixture _database;
    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly InventoryService _inventory;
    private readonly CoopLandTests.Standing _presence = new();
    private readonly CoopMembership _membership;
    private readonly CoopService _coops;
    private readonly RecordingNotifier _notifier = new();

    public CoopObjectiveTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _inventory = new InventoryService(_units, _clock);
        _membership = new CoopMembership(_units);
        TransferService transfers = new(_inventory, _clock);
        GroundItemService groundItems = new(_presence, NoChunks.Instance, NoChunks.Instance, s_maps, transfers, _units, _clock);
        FarmService farm = new(_presence, NoChunks.Instance, NoChunks.Instance, s_maps, _inventory, new QuestService(_units, _clock), _membership, new NoNodes(), new NoCooldownTools(), _units, _clock, GameCalendar.Default, groundItems, new PlayerProgressService(_units, new CalendarGameDayClock(GameCalendar.Default, _clock), _inventory, _clock));
        _coops = new CoopService(_units, transfers, _inventory, _presence, new EveryoneOnline(), _notifier, new NicknameDirectory(_units), farm, new CoopLandTests.RecordingEviction(), _clock);
    }

    public void Dispose() => _coops.Dispose();

    [Fact]
    public async Task Contribute_APlayerInNoCoop_ChangesNothing_AndSendsNothing()
    {
        Guid loner = await CreatePlayerAsync();

        await _coops.ContributeAsync(loner, ItemCatalog.Parsnip, 10);

        _notifier.Sent.Should().BeEmpty();
        (await _coops.GetAsync(loner)).HasCoop.Should().BeFalse();
    }

    [Fact]
    public async Task Contribute_AnItemNoObjectiveCounts_ChangesNothing()
    {
        (Guid founder, _, Guid coop) = await CoopWithMemberAsync();
        _notifier.Sent.Clear();

        await _coops.ContributeAsync(founder, ItemCatalog.Wood, 40);

        _notifier.Sent.Should().BeEmpty();
        (await LedgerAsync(coop)).Should().BeEmpty();
    }

    [Fact]
    public async Task Contribute_HarvestsOfEveryMember_AddUpInTheCoopLedger()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();

        await _coops.ContributeAsync(founder, ItemCatalog.Parsnip, 100);
        await _coops.ContributeAsync(member, ItemCatalog.Parsnip, 50);

        (await ProgressOfAsync(coop)).Should().Be(150);
        CoopObjectiveView view = (await _coops.GetAsync(member)).Objectives!.Single(o => o.ObjectiveId == s_parsnip.Id);
        view.Should().Be(new CoopObjectiveView(s_parsnip.Id, 150, false));
    }

    [Fact]
    public async Task Contribute_ReachingTheTarget_EarnsTheTrophy_AndKeepsItOnce()
    {
        (Guid founder, Guid member, Guid coop) = await CoopWithMemberAsync();
        await _coops.ContributeAsync(founder, ItemCatalog.Parsnip, s_parsnip.Target - 1);
        (await _coops.GetAsync(founder)).Objectives!.Single(o => o.ObjectiveId == s_parsnip.Id).Completed.Should().BeFalse();

        await _coops.ContributeAsync(member, ItemCatalog.Parsnip, 1);
        DateTimeOffset? earnedAt = await CompletedAtAsync(coop);
        earnedAt.Should().NotBeNull();

        _clock.Advance(TimeSpan.FromHours(1));
        await _coops.ContributeAsync(member, ItemCatalog.Parsnip, 25);

        CoopObjectiveView view = (await _coops.GetAsync(founder)).Objectives!.Single(o => o.ObjectiveId == s_parsnip.Id);
        view.Should().Be(new CoopObjectiveView(s_parsnip.Id, s_parsnip.Target + 25, true));
        (await CompletedAtAsync(coop)).Should().Be(earnedAt);
    }

    [Fact]
    public async Task Contribute_EveryOnlineMember_IsSentTheNewLedger()
    {
        (Guid founder, Guid member, _) = await CoopWithMemberAsync();
        _notifier.Sent.Clear();

        await _coops.ContributeAsync(member, ItemCatalog.Parsnip, 7);

        _notifier.Sent.Select(s => s.Player).Should().BeEquivalentTo([founder, member]);
        _notifier.Sent.Should().OnlyContain(s => s.Parsnip == 7);
    }

    private async Task<Dictionary<int, CoopObjectiveProgress>> LedgerAsync(Guid coop)
    {
        await using NosumDbContext db = new(_database.Options);
        return (await db.Coops.AsNoTracking().FirstAsync(c => c.Id == coop)).Objectives;
    }

    private async Task<int> ProgressOfAsync(Guid coop) => (await LedgerAsync(coop)).GetValueOrDefault(s_parsnip.Id)?.Contributed ?? 0;

    private async Task<DateTimeOffset?> CompletedAtAsync(Guid coop) => (await LedgerAsync(coop)).GetValueOrDefault(s_parsnip.Id)?.CompletedAt;

    private async Task<(Guid Founder, Guid Member, Guid Coop)> CoopWithMemberAsync()
    {
        Guid founder = await CreatePlayerAsync();
        Guid member = await CreatePlayerAsync();
        (await _coops.CreateAsync(founder, "Harvesting")).Should().Be(CoopStatus.Ok);
        Guid coop = (await _membership.FindAsync(founder))!.CoopId;
        await using (NosumDbContext db = new(_database.Options))
        {
            db.CoopMembers.Add(new CoopMember { CoopId = coop, PlayerId = member, Role = CoopRole.Member, JoinedAt = _clock.GetUtcNow() });
            _ = await db.SaveChangesAsync();
        }

        return (founder, member, coop);
    }

    private async Task<Guid> CreatePlayerAsync()
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            Player player = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start });
            unit.Repository<Player>().Add(player);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }

    private sealed class EveryoneOnline : ISessionRegistry
    {
        private readonly IConnection _connection = DispatchProxy.Create<IConnection, CoopLandTests.ConnectionStub>();

        public ValueTask ActivateAsync(Guid playerId, IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Release(IConnection connection)
        {
        }

        public ValueTask<bool> EndAsync(Guid playerId, Nosum.Contracts.Account.AccountStatus reason, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public IConnection? GetActive(Guid playerId) => _connection;
    }

    // Records the parsnip progress of each snapshot at the moment it is sent (the packet is returned to its pool afterwards).
    private sealed class RecordingNotifier : IPlayerNotifier
    {
        public List<(Guid Player, int Parsnip)> Sent { get; } = [];

        public ValueTask<bool> TrySendAsync(Guid playerId, IPacket packet, CancellationToken cancellationToken = default)
        {
            if (packet is CoopSnapshot snapshot)
            {
                int parsnip = snapshot.Objectives.Find(o => o.ObjectiveId == s_parsnip.Id)?.Contributed ?? 0;
                lock (this.Sent)
                {
                    this.Sent.Add((playerId, parsnip));
                }
            }

            return ValueTask.FromResult(true);
        }
    }
}
