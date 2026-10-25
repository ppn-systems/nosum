// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Movement;

namespace Nosum.Infrastructure.Player;

/// <summary>
/// Places players on maps and validates their movement with <see cref="MoveValidator"/>. Pushes the
/// map's state to its players while anyone is on it (see <c>PlayerService.Snapshots.cs</c>).
/// </summary>
public sealed partial class PlayerService : IPlayerService, IAsyncDisposable
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly TimeProvider _time;

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Who stands on which map.</param>
    /// <param name="maps">The hosted maps.</param>
    /// <param name="time">Clock for movement budgets and snapshot timestamps.</param>
    /// <param name="snapshotInterval">Time between map snapshots.</param>
    public PlayerService(IMapPresence presence, IMapCatalog maps, TimeProvider time, TimeSpan snapshotInterval)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(snapshotInterval, TimeSpan.Zero);

        _presence = presence;
        _maps = maps;
        _time = time;
        _snapshotInterval = snapshotInterval;
        _presence.PlayerLeft += this.OnPlayerLeftAsync;
    }

    /// <inheritdoc/>
    public async ValueTask<JoinOutcome> JoinAsync(IConnection connection, string mapId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_maps.TryGet(mapId, out MapDefinition? map))
        {
            return new JoinOutcome(JoinStatus.UnknownMap, 0, 0, 0);
        }

        PlayerPresence player = await _presence
            .JoinAsync(connection, map.Id, map.SpawnX, map.SpawnY, this.NowSeconds(), cancellationToken)
            .ConfigureAwait(false);

        this.EnsureSnapshotLoop();
        return new JoinOutcome(JoinStatus.Ok, player.PlayerId, (float)map.SpawnX, (float)map.SpawnY);
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

    private double NowSeconds() => _time.GetTimestamp() / (double)_time.TimestampFrequency;

    private async ValueTask OnPlayerLeftAsync(PlayerPresence player)
    {
        using PacketScope<PlayerLeft> lease = PacketFactory<PlayerLeft>.Acquire();
        lease.Value.PlayerId = player.PlayerId;
        await _presence.MulticastAsync(player.MapId, lease.Value).ConfigureAwait(false);
    }
}
