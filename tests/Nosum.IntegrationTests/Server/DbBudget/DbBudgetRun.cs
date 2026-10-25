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

    private string NextEmail() => "budget.player" + _nickname.ToString(System.Globalization.CultureInfo.InvariantCulture) + "@gmail.com";

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
