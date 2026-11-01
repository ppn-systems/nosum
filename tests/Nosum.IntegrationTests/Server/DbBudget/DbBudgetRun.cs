// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Maps;
using Nosum.Gameplay.Farm;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server.DbBudget;

/// <summary>
/// Runs every measurement scenario once against one host and database and keeps the numbers (docs/adr/ADR-035).
/// The tests of <see cref="DbBudgetTests"/> read them. Each scenario prepares its state unmeasured, then measures one call:
/// the window starts when the database has been quiet for a moment and ends when it is quiet again, so work a request leaves
/// running after its reply is counted for it.
/// </summary>
public sealed partial class DbBudgetRun : IAsyncLifetime
{
    private static readonly TimeSpan s_runLimit = TimeSpan.FromMinutes(12);

    private readonly PostgresFixture _database = new();
    private BudgetRig _rig = null!;
    private int _nickname;

    /// <summary>Gets every measured row, in the order it ran.</summary>
    internal IReadOnlyList<Measurement> Results => _rig.Results;

    internal IReadOnlyList<string> Divergences => _rig.Divergences;

    internal int Comparisons => _rig.Comparisons;

    /// <summary>Gets the scenarios that threw, with the error, so a missing row is explained.</summary>
    internal List<string> Failures { get; } = [];

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        _rig = await BudgetRig.StartAsync(_database, new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero), s_runLimit);

        await this.GuardAsync("account", this.AccountAsync);
        await this.GuardAsync("world", this.WorldAsync);
        await this.GuardAsync("farm", this.FarmAsync);
        await this.GuardAsync("economy", this.EconomyAsync);
        await this.GuardAsync("social", this.SocialAsync);
        await this.GuardAsync("placeables", this.PlaceablesAsync);
        await this.GuardAsync("buildings", this.BuildingsAsync);
        await this.GuardAsync("nodes", this.NodesAsync);
        await this.GuardAsync("fishing", this.FishingAsync);
        await this.GuardAsync("walk", this.WalkAsync);
        await this.GuardAsync("warp", this.WarpAsync);
        await this.GuardAsync("admin", this.AdminAsync);
        await this.GuardAsync("blacksmith", this.BlacksmithAsync);
        await this.GuardAsync("mine", this.MineAsync);
        await this.GuardAsync("area", this.AreaAsync);
        await this.GuardAsync("requests", this.RequestsAsync);
        await this.GuardAsync("finished", this.FinishedChainAsync);
        await this.GuardAsync("coopupgrade", this.CoopUpgradeAsync);
        await this.GuardAsync("background", this.BackgroundAsync);

        // The tester tools exist only in Development builds and are never mapped in Production.
        _rig.Skip("Dev", Nosum.Contracts.Dev.DevOpCode.DevGrant, "DevGrant", "tester tool, Development only; its cost is test set-up");
        _rig.Skip("Dev", Nosum.Contracts.Dev.DevOpCode.DevAdvanceClock, "DevAdvanceClock", "tester tool, Development only; the day rollover it triggers is the DayClock row");
        _rig.Skip("Dev", Nosum.Contracts.Dev.DevOpCode.DevTeleport, "DevTeleport", "tester tool, Development only; the arrival is the travel steps, measured by the warp rows");
        _rig.Skip("Dev", Nosum.Contracts.Dev.DevOpCode.DevSeason, "DevSeason", "tester tool, Development only; the day rollovers it triggers are the DayClock row");
        _rig.Skip("Dev", Nosum.Contracts.Dev.DevOpCode.DevLevelUp, "DevLevelUp", "tester tool, Development only; it pushes one banner and writes no row");
        _rig.Skip("Dev", Nosum.Contracts.Dev.DevOpCode.DevFishingBite, "DevFishingBite", "tester tool, Development only; it changes the open cast in memory, no row");

        // Combat opcodes operate on in-memory instance monsters and cached player vitals (docs/adr/ADR-045 D3, ADR-056).
        _rig.Skip("Combat", Nosum.Contracts.Combat.CombatOpCode.GetVitals, "GetVitals", "reads in-memory vitals seeded from the shared player_states cache");
        _rig.Skip("Combat", Nosum.Contracts.Combat.CombatOpCode.GetMonsters, "GetMonsters", "reads in-memory monster and hazard spawns for the active map instance");
        _rig.Skip("Combat", Nosum.Contracts.Combat.CombatOpCode.AttackMonster, "AttackMonster", "validates weapon from cached inventory and updates vitals in player_states");
        _rig.Skip("Combat", Nosum.Contracts.Combat.CombatOpCode.DrinkPotion, "DrinkPotion", "removes one potion from cached inventory and updates vitals in player_states");
        _rig.Skip("Combat", Nosum.Contracts.Combat.CombatOpCode.CastSkill, "CastSkill", "spends MP on in-memory instance monsters and updates vitals in player_states");
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        if (_rig is not null)
        {
            await _rig.DisposeAsync();
        }

        await _database.DisposeAsync();
    }

    // A scenario that cannot run must not take the others down: its rows are missing and the failure is listed.
    private async Task GuardAsync(string name, Func<Task> scenario)
    {
        try
        {
            await scenario();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !_rig.Timeout.IsCancellationRequested)
        {
            this.Failures.Add(name + ": " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // A nickname the rules accept and no other scenario used.
    private string NextNickname() => "budget" + Interlocked.Increment(ref _nickname).ToString(System.Globalization.CultureInfo.InvariantCulture);

    // The nearest tillable tile within reach of where the player stands on the farm.
    private static (int X, int Y) TileInReachOf(float feetX, float feetY)
    {
        IMapCatalog maps = BudgetRig.Service<IMapCatalog>();
        _ = maps.TryGet(Program.FarmMapId, out MapDefinition? map);
        int tx = (int)(feetX / 16);
        int ty = (int)(feetY / 16);
        return Enumerable.Range(-2, 5)
            .SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: tx + dx, Y: ty + dy)))
            .Where(t => FarmRules.InReach(feetX, feetY, t.X, t.Y, 16) && map!.Tillable!.IsTillable(t.X, t.Y))
            .OrderBy(t => Math.Abs(((t.X + 0.5) * 16) - feetX) + Math.Abs(((t.Y + 0.5) * 16) - feetY))
            .First();
    }
}
