// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.S2C;
using Nosum.Core;
using Nosum.Gameplay.Movement;

namespace Nosum.Infrastructure.Players;

/// <summary>
/// Places players on maps (where they were last, if saved), validates their movement with
/// <see cref="MoveValidator"/> and saves their place when they leave. Pushes the map's state to its
/// players while anyone is on it (see <c>PlayerService.Snapshots.cs</c>).
/// </summary>
public sealed partial class PlayerService : IPlayerService, IAsyncDisposable
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IPlayerStateService _states;
    private readonly TimeProvider _time;

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands on which map.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="states">Saved player positions.</param>
    /// <param name="time">Clock for movement budgets and snapshot timestamps.</param>
    /// <param name="snapshotInterval">Time between map snapshots.</param>
    public PlayerService(IMapPresence presence, IMapCatalog maps, IPlayerStateService states, TimeProvider time, TimeSpan snapshotInterval)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(snapshotInterval, TimeSpan.Zero);

        _presence = presence;
        _maps = maps;
        _states = states;
        _time = time;
        _snapshotInterval = snapshotInterval;
        _presence.PlayerLeft += this.OnPlayerLeftAsync;
    }

    /// <inheritdoc/>
    public async ValueTask<JoinOutcome> JoinAsync(IConnection connection, Guid accountId, string mapId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_maps.TryGet(mapId, out MapDefinition? map))
        {
            return new JoinOutcome(JoinStatus.UnknownMap, 0, 0, 0);
        }

        MotionState start = await this.StartingMotionAsync(accountId, map, cancellationToken).ConfigureAwait(false);
        PlayerPresence player = await _presence.JoinAsync(connection, accountId, map.Id, start, cancellationToken).ConfigureAwait(false);

        this.EnsureSnapshotLoop();
        return new JoinOutcome(JoinStatus.Ok, player.PlayerId, (float)start.X, (float)start.Y);
    }

    /// <inheritdoc/>
    public MoveOutcome Move(IConnection connection, float x, float y, byte facing, bool moving)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map))
        {
            return new MoveOutcome(MoveStatus.NotJoined, 0, 0, 0);
        }

        double now = this.NowSeconds();
        lock (player.Gate)
        {
            MoveVerdict verdict = MoveValidator.Validate(ref player.Motion, x, y, facing, moving, now, map.Grid);
            if (verdict == MoveVerdict.Accepted)
            {
                return new MoveOutcome(MoveStatus.Accepted, 0, x, y);
            }

            MoveRefusal reason = verdict switch
            {
                MoveVerdict.TooFast => MoveRefusal.TooFast,
                MoveVerdict.OutOfMap => MoveRefusal.OutOfMap,
                MoveVerdict.Blocked => MoveRefusal.Blocked,
                _ => MoveRefusal.Invalid,
            };
            return new MoveOutcome(MoveStatus.Refused, reason, (float)player.Motion.X, (float)player.Motion.Y);
        }
    }

    // The saved place when it is on this map and still legal (the map may have changed since), else the spawn.
    private async ValueTask<MotionState> StartingMotionAsync(Guid accountId, MapDefinition map, CancellationToken cancellationToken)
    {
        double now = this.NowSeconds();
        PlayerStateRow? saved = await _states.LoadAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (saved is { } row
            && string.Equals(row.MapName, map.Id, StringComparison.Ordinal)
            && double.IsFinite(row.X)
            && double.IsFinite(row.Y)
            && row.X >= 0
            && row.Y >= 0
            && row.X < map.Grid.Width * map.Grid.TileSize
            && row.Y < map.Grid.Height * map.Grid.TileSize
            && !MoveValidator.FootBlocked(map.Grid, row.X, row.Y))
        {
            return new MotionState(row.X, row.Y, now, (int)row.Facing);
        }

        return new MotionState(map.SpawnX, map.SpawnY, now);
    }

    private double NowSeconds() => _time.GetTimestamp() / (double)_time.TimestampFrequency;

    [SuppressMessage("Design", "CA1031", Justification = "Saving is best effort on the disconnect path; the leave notice to the map must still go out.")]
    private async ValueTask OnPlayerLeftAsync(PlayerPresence player)
    {
        try
        {
            PlayerStateRow state;
            lock (player.Gate)
            {
                state = new PlayerStateRow(player.MapId, player.Motion.X, player.Motion.Y, (Facing)player.Motion.Facing);
            }

            await _states.SaveAsync(player.AccountId, state, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The player keeps their previously saved place.
        }

        using PacketScope<PlayerLeft> lease = PacketFactory<PlayerLeft>.Acquire();
        lease.Value.PlayerId = player.PlayerId;
        await _presence.MulticastAsync(player.MapId, lease.Value).ConfigureAwait(false);
    }
}
