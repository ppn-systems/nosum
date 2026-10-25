// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Progress;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Domain.Accounts;
using Nosum.Domain.Inventory;
using Nosum.Domain.Nodes;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Skills;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;
using Xunit;
using PlayerState = Nosum.Domain.Accounts.PlayerState;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Energy, skills and food (docs/adr/ADR-023) over real Nalix WebSocket sessions against the real host and a real PostgreSQL database: a tool use spends energy
/// and earns experience in the transaction of its effect, a player at zero is refused and nothing changes, eating restores, a level-up is pushed, the daily refill
/// follows the game clock, and an old account gets the tools it lacks. Each test puts the nodes it needs in the database before the player enters.
/// </summary>
[Collection(NalixHostSerial.Name)]
public sealed class EnergyWireTests : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(90);
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly int s_hoe = SlotOf(ItemCatalog.Hoe);
    private static readonly int s_axe = SlotOf(ItemCatalog.Axe);
    private static readonly int s_pickaxe = SlotOf(ItemCatalog.Pickaxe);
    private static readonly int s_scythe = SlotOf(ItemCatalog.Scythe);

    // The first free hotbar slot after the starter kit: where a granted item lands.
    private static readonly int s_granted = StarterKit.Items.Length;

    private readonly PostgresFixture _database;

    public EnergyWireTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Join_TellsThePlayerTheirFullEnergyAndNoExperience()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();

        _ = await player.JoinAsync(host.Timeout);

        EnergyChanged energy = await player.Energy.WaitAsync(_ => true, host.Timeout);
        (energy.Energy, energy.MaxEnergy).Should().Be(((ushort)EnergyRules.MaxEnergy, (ushort)EnergyRules.MaxEnergy));
        SkillXpChanged xp = await player.Xp.WaitAsync(_ => true, host.Timeout);
        (xp.FarmingXp, xp.ForagingXp, xp.MiningXp).Should().Be((0, 0, 0));
    }

    [Fact]
    public async Task SelectSlot_WaitsForItsWrite_AndALeavingPlayerFlushesIt()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        Guid account;
        await using (Client player = await host.ConnectAsync())
        {
            account = player.AccountId;
            _ = await player.JoinAsync(host.Timeout);
            (await player.SelectSlotAsync(3, host.Timeout)).Should().Be(InventoryStatus.Ok);
            (await InstanceManager.Instance.GetExistingInstance<IInventoryService>()!.GetAsync(account)).SelectedSlot.Should().Be(3, "the server answers from memory at once");
            (await SelectedInRowsAsync(account)).Should().Be(0, "the row is written within the window, or when the player leaves");
        }

        await WaitForAsync(async () => await SelectedInRowsAsync(account) == 3, host.Timeout);
    }

    [Fact]
    public async Task SelectSlot_AMapChangeFlushesIt()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);
        (await player.SelectSlotAsync(5, host.Timeout)).Should().Be(InventoryStatus.Ok);
        (await SelectedInRowsAsync(player.AccountId)).Should().Be(0);

        Nalix.Abstractions.Networking.IConnection connection = InstanceManager.Instance.GetExistingInstance<ISessionRegistry>()!.GetActive(player.AccountId)!;
        _ = await InstanceManager.Instance.GetExistingInstance<Nosum.Abstractions.Player.IPlayerService>()!.TravelAsync(connection, Nosum.Gameplay.Maps.MapInstance.Shared(Nosum.Gameplay.Maps.GameMaps.Town));

        await WaitForAsync(async () => await SelectedInRowsAsync(player.AccountId) == 5, host.Timeout);
    }

    [Fact]
    public async Task SelectSlot_ShutdownFlushesWhatIsWaiting()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);
        (await player.SelectSlotAsync(4, host.Timeout)).Should().Be(InventoryStatus.Ok);

        await InstanceManager.Instance.GetExistingInstance<Nosum.Infrastructure.Caching.WriteBehind>()!.FlushAllAsync();

        (await SelectedInRowsAsync(player.AccountId)).Should().Be(4);
    }

    private async Task<int> SelectedInRowsAsync(Guid account)
    {
        await using NosumDbContext db = new(_database.Options);
        return await db.PlayerInventories.Where(i => i.PlayerId == account).Select(i => i.SelectedSlot).SingleAsync();
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, CancellationToken ct)
    {
        while (!await condition())
        {
            await Task.Delay(50, ct);
        }
    }

    [Fact]
    public async Task Tilling_SpendsTwoEnergyAndEarnsFarmingExperience_InTheSameCommitAsTheTile()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);
        (int x, int y) = host.TillableTileInReach();

        (await player.UseAsync(s_hoe, x, y, host.Timeout)).Should().BeEquivalentTo(new { Status = FarmStatus.Ok, Action = FarmAction.Tilled });

        (await player.Energy.WaitAsync(e => e.Energy == EnergyRules.MaxEnergy - 2, host.Timeout)).MaxEnergy.Should().Be(EnergyRules.MaxEnergy);
        (await player.Xp.WaitAsync(p => p.FarmingXp == SkillRules.TillXp, host.Timeout)).MiningXp.Should().Be(0);
        await using NosumDbContext db = new(_database.Options);
        PlayerState row = await db.PlayerStates.SingleAsync(s => s.PlayerId == player.AccountId);
        (row.Energy, row.FarmingXp).Should().Be((EnergyRules.MaxEnergy - 2, SkillRules.TillXp));
        row.MapName.Should().BeEmpty("a state row made by the progress service has no saved place yet");
        (await db.FarmPlots.AnyAsync(p => p.OwnerId == player.AccountId && p.X == x && p.Y == y)).Should().BeTrue();
    }

    [Fact]
    public async Task AtZeroEnergy_AToolIsRefusedAndNothingChanges_TheScytheStaysFree_AndEatingRestores()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);
        (int x, int y) = host.TillableTileInReach();
        (await player.UseAsync(s_hoe, x, y, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        await SetAsync(player.AccountId, s => s.SetProperty(p => p.Energy, 0));
        (int x2, int y2) = host.TillableTileInReach(1);

        // Refused: no tile, no experience, and the player is not told they spent anything.
        (await player.UseAsync(s_hoe, x2, y2, host.Timeout)).Should().BeEquivalentTo(new { Status = FarmStatus.NoEnergy, Action = FarmAction.None });
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.FarmPlots.CountAsync(p => p.OwnerId == player.AccountId)).Should().Be(1);
            (await db.PlayerStates.SingleAsync(s => s.PlayerId == player.AccountId)).Should().BeEquivalentTo(new { Energy = 0, FarmingXp = SkillRules.TillXp });
        }

        // The scythe harvests for nothing: grow a parsnip is too slow here, so check that a free tool is not refused at zero (it reaches the farm rules: nothing to do).
        (await player.UseAsync(s_scythe, x2, y2, host.Timeout)).Status.Should().Be(FarmStatus.NothingToDo);

        // Eat a parsnip: ten energy back, one item less, and the hoe works again for two of it.
        (await player.GrantAsync(ItemCatalog.Parsnip, 3, host.Timeout)).Should().BeTrue();
        (await player.EatAsync(s_granted, host.Timeout)).Status.Should().Be(EatStatus.Ok);
        (await player.Energy.WaitAsync(e => e.Energy == 10, host.Timeout)).MaxEnergy.Should().Be(EnergyRules.MaxEnergy);
        IInventoryService inventory = InstanceManager.Instance.GetExistingInstance<IInventoryService>()!;
        (await inventory.PeekSlotAsync(player.AccountId, s_granted)).Should().Be(new InventorySlotView(s_granted, ItemCatalog.Parsnip, 2));
        (await player.UseAsync(s_hoe, x2, y2, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        (await player.Energy.WaitAsync(e => e.Energy == 8, host.Timeout)).Energy.Should().Be(8);
    }

    [Fact]
    public async Task Eat_ForEveryWayItCanBeRefused_ChangesNothing()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);
        IInventoryService inventory = InstanceManager.Instance.GetExistingInstance<IInventoryService>()!;

        (await player.EatAsync(InventoryLimits.HotbarSlots, host.Timeout)).Status.Should().Be(EatStatus.InvalidSlot);
        (await player.EatAsync(s_hoe, host.Timeout)).Status.Should().Be(EatStatus.NotEdible, "a tool is not food");
        (await player.EatAsync(SlotOf(ItemCatalog.ParsnipSeeds), host.Timeout)).Status.Should().Be(EatStatus.NotEdible, "nor is a seed");
        (await player.EatAsync(s_granted, host.Timeout)).Status.Should().Be(EatStatus.NotEdible, "an empty slot");
        (await player.GrantAsync(ItemCatalog.Radish, 2, host.Timeout)).Should().BeTrue();
        (await player.EatAsync(s_granted, host.Timeout)).Status.Should().Be(EatStatus.EnergyFull, "a full player does not waste food");
        (await inventory.PeekSlotAsync(player.AccountId, s_granted)).Quantity.Should().Be(2);

        // Eating tops up to the maximum and no further.
        (int x, int y) = host.TillableTileInReach();
        (await player.UseAsync(s_hoe, x, y, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        await SetAsync(player.AccountId, s => s.SetProperty(p => p.Energy, EnergyRules.MaxEnergy - 3));
        (await player.EatAsync(s_granted, host.Timeout)).Status.Should().Be(EatStatus.Ok);
        (await player.Energy.WaitAsync(e => e.Energy == EnergyRules.MaxEnergy, host.Timeout)).Energy.Should().Be(EnergyRules.MaxEnergy);
        (await inventory.PeekSlotAsync(player.AccountId, s_granted)).Quantity.Should().Be(1);
    }

    [Fact]
    public async Task CrossingAnExperienceThreshold_PushesALevelUp_AndFourLevelsTakeOneOffTheCost()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);
        (int x, int y) = host.TillableTileInReach();
        (await player.UseAsync(s_hoe, x, y, host.Timeout)).Action.Should().Be(FarmAction.Tilled);

        // Three experience short of level one: the next till crosses it.
        await SetAsync(player.AccountId, s => s.SetProperty(p => p.FarmingXp, SkillRules.XpForLevel(1) - SkillRules.TillXp));
        (int x2, int y2) = host.TillableTileInReach(1);
        (await player.UseAsync(s_hoe, x2, y2, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        LevelUp level = await player.Levels.WaitAsync(_ => true, host.Timeout);
        (level.Skill, level.Level).Should().Be(((byte)SkillKind.Farming, (byte)1));
        (await player.Xp.WaitAsync(p => p.FarmingXp == SkillRules.XpForLevel(1), host.Timeout)).FarmingXp.Should().Be(100);

        // At level four the hoe costs one.
        await SetAsync(player.AccountId, s => s.SetProperty(p => p.FarmingXp, SkillRules.XpForLevel(4)).SetProperty(p => p.Energy, 100));
        (int x3, int y3) = host.TillableTileInReach(2);
        (await player.UseAsync(s_hoe, x3, y3, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        (await player.Energy.WaitAsync(e => e.Energy == 99, host.Timeout)).Energy.Should().Be(99);
    }

    [Fact]
    public async Task Energy_RefillsOnTheNextGameDay_WithoutAnyTick()
    {
        // 06:00 on game day zero; a game day is twelve hours.
        FakeTimeProvider clock = new(DateTimeOffset.FromUnixTimeMilliseconds(GameCalendar.DefaultEpochMs).AddHours(6));
        await using TestHost host = await TestHost.StartAsync(_database, clock);
        await using Client player = await host.ConnectAsync();
        _ = await player.JoinAsync(host.Timeout);
        (int x, int y) = host.TillableTileInReach();
        (await player.UseAsync(s_hoe, x, y, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        await SetAsync(player.AccountId, s => s.SetProperty(p => p.Energy, 5));
        IPlayerProgress progress = InstanceManager.Instance.GetExistingInstance<IPlayerProgress>()!;
        (await progress.GetAsync(player.AccountId)).Energy.Should().Be(5, "the same game day");

        clock.Advance(TimeSpan.FromHours(12));

        (await progress.GetAsync(player.AccountId)).Energy.Should().Be(EnergyRules.MaxEnergy, "the day rolled over: read as full without writing anything");
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.PlayerStates.SingleAsync(s => s.PlayerId == player.AccountId)).Energy.Should().Be(5, "the refill is computed, not stored, until the player acts");
        }

        (int x2, int y2) = host.TillableTileInReach(1);
        (await player.UseAsync(s_hoe, x2, y2, host.Timeout)).Action.Should().Be(FarmAction.Tilled);
        (await player.Energy.WaitAsync(e => e.Energy == EnergyRules.MaxEnergy - 2, host.Timeout)).Energy.Should().Be(EnergyRules.MaxEnergy - 2);
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.PlayerStates.SingleAsync(s => s.PlayerId == player.AccountId)).Should().BeEquivalentTo(new { Energy = EnergyRules.MaxEnergy - 2, EnergyRefilledDay = 1L });
        }
    }

    [Fact]
    public async Task BreakingAndHittingNodes_SpendEnergy_EarnTheRightSkill_AndAreRefusedAtZeroWithTheNodeUntouched()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();
        (int X, int Y)[] free = host.FreeTilesNear();
        await SeedAsync(player.AccountId, Row(NodeKind.Stone, free[0].X, free[0].Y), Row(NodeKind.Tree, free[1].X, free[1].Y));
        _ = await player.JoinAsync(host.Timeout);

        // A swing that only damages the tree costs energy and earns one foraging experience.
        (await player.UseAsync(s_axe, free[1].X, free[1].Y, host.Timeout)).Action.Should().Be(FarmAction.Hit);
        (await player.Energy.WaitAsync(e => e.Energy == EnergyRules.MaxEnergy - 2, host.Timeout)).Energy.Should().Be(EnergyRules.MaxEnergy - 2);
        (await player.Xp.WaitAsync(p => p.ForagingXp == SkillRules.HitXp, host.Timeout)).MiningXp.Should().Be(0);

        // At zero the stone cannot be broken: still there, no yield, nothing earned.
        await SetAsync(player.AccountId, s => s.SetProperty(p => p.Energy, 0));
        (await player.UseAsync(s_pickaxe, free[0].X, free[0].Y, host.Timeout)).Status.Should().Be(FarmStatus.NoEnergy);
        (await player.UseAsync(s_axe, free[1].X, free[1].Y, host.Timeout)).Status.Should().Be(FarmStatus.NoEnergy);
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.WorldNodes.CountAsync(n => n.OwnerId == player.AccountId)).Should().Be(2);
            (await db.PlayerStates.SingleAsync(s => s.PlayerId == player.AccountId)).Should().BeEquivalentTo(new { Energy = 0, MiningXp = 0, ForagingXp = SkillRules.HitXp });
        }

        IInventoryService inventory = InstanceManager.Instance.GetExistingInstance<IInventoryService>()!;
        (await inventory.GetAsync(player.AccountId)).Slots.Should().NotContain(s => s.ItemId == ItemCatalog.Stone);

        // With energy the stone breaks: two energy and mining experience, in the same commit as the yield.
        await SetAsync(player.AccountId, s => s.SetProperty(p => p.Energy, 50));
        (await player.UseAsync(s_pickaxe, free[0].X, free[0].Y, host.Timeout)).Action.Should().Be(FarmAction.Broke);
        (await player.Energy.WaitAsync(e => e.Energy == 48, host.Timeout)).Energy.Should().Be(48);
        (await player.Xp.WaitAsync(p => p.MiningXp == SkillRules.BreakXp(NodeKind.Stone), host.Timeout)).ForagingXp.Should().Be(SkillRules.HitXp);
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.PlayerStates.SingleAsync(s => s.PlayerId == player.AccountId)).Should().BeEquivalentTo(new { Energy = 48, MiningXp = SkillRules.BreakXp(NodeKind.Stone) });
            (await db.WorldNodes.AnyAsync(n => n.OwnerId == player.AccountId && n.Kind == (short)NodeKind.Stone)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Join_GivesAnOldAccountTheStarterToolsItLacks_OnceAndAudited()
    {
        await using TestHost host = await TestHost.StartAsync(_database);
        await using Client player = await host.ConnectAsync();
        IInventoryService inventory = InstanceManager.Instance.GetExistingInstance<IInventoryService>()!;

        // An account from before the axe, pickaxe and scythe: its inventory has the first five starter items only.
        _ = await inventory.GetAsync(player.AccountId);
        int[] newTools = [ItemCatalog.Axe, ItemCatalog.Pickaxe, ItemCatalog.Scythe];
        await using (NosumDbContext db = new(_database.Options))
        {
            _ = await db.InventorySlots.Where(s => s.PlayerId == player.AccountId && newTools.Contains(s.ItemId)).ExecuteDeleteAsync();
            _ = await db.InventoryLog.Where(l => l.PlayerId == player.AccountId && newTools.Contains(l.ItemId)).ExecuteDeleteAsync();
        }

        inventory.Forget(player.AccountId); // rows changed behind the service's back: it reloads them (docs/adr/ADR-035 D3)

        (await inventory.GetAsync(player.AccountId)).Slots.Select(s => s.ItemId).Should().NotContain(newTools);

        _ = await player.JoinAsync(host.Timeout);

        InventoryView after = await inventory.GetAsync(player.AccountId);
        after.Slots.Select(s => s.ItemId).Should().Contain(newTools);
        after.Slots.Count(s => newTools.Contains(s.ItemId)).Should().Be(3);
        await using (NosumDbContext db = new(_database.Options))
        {
            InventoryLogEntry[] audit = await db.InventoryLog.Where(l => l.PlayerId == player.AccountId && l.Reason == "starter.backfill").ToArrayAsync();
            audit.Select(a => (a.ItemId, a.Delta)).Should().BeEquivalentTo(newTools.Select(t => (t, 1)));
        }

        // Once: asking again (the next join) grants nothing and logs nothing.
        InventoryChange again = await inventory.BackfillStarterToolsAsync(player.AccountId);
        again.IsOk.Should().BeTrue();
        again.Changed.Should().BeEmpty();
        await using (NosumDbContext db = new(_database.Options))
        {
            (await db.InventoryLog.CountAsync(l => l.PlayerId == player.AccountId && l.Reason == "starter.backfill")).Should().Be(3);
        }

        // A brand-new account already has the whole kit and gets nothing extra.
        await using Client fresh = await host.ConnectAsync();
        _ = await fresh.JoinAsync(host.Timeout);
        await using NosumDbContext check = new(_database.Options);
        (await check.InventoryLog.AnyAsync(l => l.PlayerId == fresh.AccountId && l.Reason == "starter.backfill")).Should().BeFalse();
    }

    private static int SlotOf(int itemId) => Array.FindIndex(StarterKit.Items, i => i.ItemId == itemId);

    private static WorldNode Row(NodeKind kind, int x, int y)
        => new()
        {
            TileX = x,
            TileY = y,
            Kind = (short)kind,
            Stage = kind == NodeKind.Tree ? TreeStage.Grown : (short)0,
            Hits = NodeCatalog.HitsFor(kind, TreeStage.Grown),
            SpawnedAt = s_start,
        };

    // Puts exactly these nodes on the player's farm and marks it seeded, so the map's random first fill never happens.
    private async Task SeedAsync(Guid owner, params WorldNode[] nodes)
    {
        await using NosumDbContext db = new(_database.Options);
        foreach (WorldNode node in nodes)
        {
            node.MapId = GameMaps.Farm;
            node.OwnerId = owner;
            db.WorldNodes.Add(node);
        }

        db.NodeInstances.Add(new NodeInstanceState { MapId = GameMaps.Farm, OwnerId = owner, SeededAt = s_start, LastDay = InstanceManager.Instance.GetExistingInstance<IGameDayClock>()!.Today });
        _ = await db.SaveChangesAsync();
    }

    // Edits the player's state row the way a long play session would have left it.
    private async Task SetAsync(Guid playerId, Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<PlayerState>> change)
    {
        await using NosumDbContext db = new(_database.Options);
        (await db.PlayerStates.Where(s => s.PlayerId == playerId).ExecuteUpdateAsync(change)).Should().Be(1);

        // The row changed behind the server's back: it reads it again (docs/adr/ADR-035 D4).
        InstanceManager.Instance.GetExistingInstance<Nosum.Infrastructure.Progress.PlayerStateCache>()!.Forget(playerId);
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly NetworkApplication _app;
        private readonly CancellationTokenSource _timeout = new(s_timeout);

        private TestHost(NetworkApplication app, ushort port)
        {
            _app = app;
            this.Port = port;
        }

        public ushort Port { get; }

        public CancellationToken Timeout => _timeout.Token;

        public static async Task<TestHost> StartAsync(PostgresFixture database, TimeProvider? clock = null)
        {
            ushort port = GetFreePort();
            HostServices.Register(database.Options);

            // Development, so the tester grant can hand out produce.
            NetworkApplication app = Program.BuildApplication(port, clock, development: true);
            TestHost host = new(app, port);
            await app.ActivateAsync(host.Timeout);
            return host;
        }

        public async Task<Client> ConnectAsync()
        {
            Client client = new(new WebSocketSession(
                new TransportOptions { Address = "127.0.0.1", Port = this.Port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath }));
            await client.ConnectAsync(this.Timeout);
            return client;
        }

        // Walkable, unreserved tiles within reach of the farm's spawn, nearest first.
        public (int X, int Y)[] FreeTilesNear()
        {
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(GameMaps.Farm, out MapDefinition? map);
            int tx = (int)(map!.SpawnX / 16);
            int ty = (int)(map.SpawnY / 16);
            return [.. Enumerable.Range(-2, 5)
                .SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: tx + dx, Y: ty + dy)))
                .Where(t => !map.Grid.IsBlocked(t.X, t.Y) && !map.ReservedTiles.Contains((t.X, t.Y)) && !(t.X == tx && t.Y == ty) && FarmRules.InReach(map.SpawnX, map.SpawnY, t.X, t.Y, 16))
                .OrderBy(t => Math.Abs(t.X - tx) + Math.Abs(t.Y - ty))];
        }

        public (int X, int Y) TillableTileInReach(int index = 0)
        {
            IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
            _ = maps.TryGet(GameMaps.Farm, out MapDefinition? farm);
            return this.FreeTilesNear().Where(t => farm!.Tillable!.IsTillable(t.X, t.Y)).ElementAt(index);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.DeactivateAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _timeout.Dispose();
        }
    }

    private static class Pace
    {
        private static readonly SemaphoreSlim s_gate = new(1, 1);
        private static DateTime s_last = DateTime.MinValue;

        // Resuming a session is limited to two a second per address, and every test of the suite shares one.
        public static async Task WaitAsync(CancellationToken ct)
        {
            await s_gate.WaitAsync(ct);
            try
            {
                TimeSpan wait = TimeSpan.FromMilliseconds(650) - (DateTime.UtcNow - s_last);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }

                s_last = DateTime.UtcNow;
            }
            finally
            {
                _ = s_gate.Release();
            }
        }
    }

    // Pushes arrive on socket threads; tests read them in order and skip what does not match.
    private sealed class Inbox<T>
    {
        private readonly Channel<T> _items = Channel.CreateUnbounded<T>();

        public void Add(T item) => _ = _items.Writer.TryWrite(item);

        public async Task<T> WaitAsync(Func<T, bool> predicate, CancellationToken ct)
        {
            while (true)
            {
                T item = await _items.Reader.ReadAsync(ct);
                if (predicate(item))
                {
                    return item;
                }
            }
        }
    }

    private sealed class Client : IAsyncDisposable
    {
        // The server's swing cooldown is 300 ms.
        private static readonly TimeSpan s_swing = TimeSpan.FromMilliseconds(330);

        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;

        public Client(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<EnergyChanged>(p => this.Energy.Add(new EnergyChanged { Energy = p.Energy, MaxEnergy = p.MaxEnergy })),
                session.On<SkillXpChanged>(p => this.Xp.Add(new SkillXpChanged { FarmingXp = p.FarmingXp, ForagingXp = p.ForagingXp, MiningXp = p.MiningXp })),
                session.On<LevelUp>(p => this.Levels.Add(new LevelUp { Skill = p.Skill, Level = p.Level })),
            ];
        }

        public Inbox<EnergyChanged> Energy { get; } = new();

        public Inbox<SkillXpChanged> Xp { get; } = new();

        public Inbox<LevelUp> Levels { get; } = new();

        public Guid AccountId { get; private set; }

        public async Task ConnectAsync(CancellationToken ct)
        {
            await _session.ConnectAsync(ct: ct);
            await _session.HandshakeAsync(ct);

            // The account is created in the database directly: sign-ups are rate limited per address.
            IAccountService accounts = InstanceManager.Instance.GetExistingInstance<IAccountService>()!;
            AccountOutcome created = await accounts.StartGuestAsync(Nosum.Contracts.Common.GameLanguage.English, ct);
            created.Status.Should().Be(AccountStatus.Ok);

            await Pace.WaitAsync(ct);
            using ResumeSession request = new() { SessionToken = created.SessionToken! };
            AccountResult result = await _session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            this.AccountId = result.PlayerId;
        }

        public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
        {
            using JoinMap request = new() { MapId = Program.FarmMapId };
            JoinMapResult result = await _session.RequestAsync<JoinMapResult>(request, ct: ct);
            result.Status.Should().Be(JoinStatus.Ok);
            return result;
        }

        // Waits out the swing cooldown first, like a player who keeps swinging.
        public async Task<UseItemResult> UseAsync(int slot, int x, int y, CancellationToken ct)
        {
            await Task.Delay(s_swing, ct);
            using UseItem request = new() { Slot = (byte)slot, TileX = x, TileY = y };
            return await _session.RequestAsync<UseItemResult>(request, ct: ct);
        }

        // Paced like a player: the eat handler is rate limited per address.
        public async Task<EatResult> EatAsync(int slot, CancellationToken ct)
        {
            await Task.Delay(s_swing, ct);
            using Eat request = new() { Slot = (byte)slot };
            return await _session.RequestAsync<EatResult>(request, ct: ct);
        }

        public async Task<InventoryStatus> SelectSlotAsync(int slot, CancellationToken ct)
        {
            using SelectSlot request = new() { Slot = (byte)slot };
            return (await _session.RequestAsync<InventoryResult>(request, ct: ct)).Status;
        }

        public async Task<bool> GrantAsync(int itemId, int quantity, CancellationToken ct)
        {
            using DevGrant request = new() { ItemId = itemId, Quantity = quantity };
            return (await _session.RequestAsync<DevGrantResult>(request, ct: ct)).Ok;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            await _session.DisconnectAsync();
            _session.Dispose();
        }
    }
}
