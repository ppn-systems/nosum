// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Mine;

namespace Nosum.Abstractions.Mines;

/// <summary>
/// The mine (docs/adr/ADR-026 D3): the elevator, the ladders and the one thing stored about it, how deep a player has been. Floors are per-player
/// instances (<c>mine&lt;N&gt;@&lt;player&gt;</c>) whose nodes are built in memory when the player enters and dropped when they leave. The server
/// decides everything: the player's place, the floor and the depth never come from the client.
/// </summary>
public interface IMineService
{
    /// <summary>Reads the player's mine progress.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The floor they stand on (zero outside the mine floors) and the deepest floor they reached.</returns>
    Task<MineView> GetAsync(IConnection connection, CancellationToken cancellationToken = default);

    /// <summary>Rides the elevator to a stop: the entrance (0) or an elevator floor (5, 10 ... 40) the player has reached.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="floor">The stop.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome and the state after it.</returns>
    Task<MineView> RideElevatorAsync(IConnection connection, int floor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the player down the ladder they face to the next floor. The caller (the tool-use path) has already checked the ladder stands on the tile in
    /// front of the player and within reach.
    /// </summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns><see langword="true"/> when the player went down.</returns>
    Task<bool> DescendAsync(IConnection connection, CancellationToken cancellationToken = default);
}

/// <summary>A player's place in the mine.</summary>
/// <param name="Status">Ok, or why a request was refused.</param>
/// <param name="Floor">The floor the player stands on; zero on the entrance cave and outside the mine.</param>
/// <param name="Deepest">The deepest floor the player ever reached.</param>
public readonly record struct MineView(MineStatus Status, int Floor, int Deepest);
