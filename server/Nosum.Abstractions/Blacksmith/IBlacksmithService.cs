// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Shop;

namespace Nosum.Abstractions.Blacksmith;

/// <summary>
/// The blacksmith's tool upgrades (docs/adr/ADR-026 D2): a tool, coins and bars go in, the tool of the next tier comes out two game days later. The
/// server prices and times everything with <c>BlacksmithRules</c>; coins, bars and the old tool move in one transaction together with the row that
/// remembers the job, so a crash can neither keep the tool and the price nor lose both.
/// </summary>
public interface IBlacksmithService
{
    /// <summary>Reads the player's tool at the blacksmith.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The state; <see cref="BlacksmithView.ToolItemId"/> is zero when the smith holds nothing.</returns>
    Task<BlacksmithView> GetAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Hands a tool in for an upgrade.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="toolItemId">The item id of the tool to upgrade.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome and the state after it.</returns>
    Task<BlacksmithView> UpgradeAsync(IConnection connection, int toolItemId, CancellationToken cancellationToken = default);

    /// <summary>Collects the upgraded tool once it is ready.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome and the state after it.</returns>
    Task<BlacksmithView> CollectAsync(IConnection connection, CancellationToken cancellationToken = default);
}

/// <summary>A player's tool at the blacksmith.</summary>
/// <param name="Status">Ok, or why a request was refused.</param>
/// <param name="ToolItemId">The tool the smith is making; zero when none.</param>
/// <param name="ReadyDay">The game day number it can be collected; zero when none.</param>
/// <param name="Today">The game day number now.</param>
public readonly record struct BlacksmithView(BlacksmithStatus Status, int ToolItemId, long ReadyDay, long Today);
