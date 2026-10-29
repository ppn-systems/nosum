// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Combat.S2C;
using PlayerState = Nosum.Domain.Accounts.PlayerState;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Skills;
using Nosum.Infrastructure.Concurrency;
using Nosum.Infrastructure.Progress;

namespace Nosum.Infrastructure.Combat;

public sealed partial class CombatService
{
    /// <summary>Evicts per-player transient combat caches when a player disconnects.</summary>
    /// <param name="playerId">The disconnected player ID.</param>
    public void OnPlayerDisconnected(Guid playerId)
    {
        _ = _vitals.TryRemove(playerId, out _);
        _ = _playerCombos.TryRemove(playerId, out _);

        foreach ((Guid PlayerId, byte SkillId) key in _skillCooldowns.Keys)
        {
            if (key.PlayerId == playerId)
            {
                _ = _skillCooldowns.TryRemove(key, out _);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _presence.PlayerLeft -= this.OnPlayerLeftAsync;

    private async ValueTask<PlayerVitalsState> GetOrCreateVitalsAsync(Guid playerId, CancellationToken cancellationToken)
    {
        if (_vitals.TryGetValue(playerId, out PlayerVitalsState? existing))
        {
            return existing;
        }

        PlayerStateCache.Snapshot? loaded = null;
        if (_stateCache is not null && _stateCache.TryPeek(playerId, out PlayerStateCache.Snapshot peeked) && peeked.Exists)
        {
            loaded = peeked;
        }
        else if (_unitOfWork is not null)
        {
            loaded = _stateCache is not null
                ? await _stateCache.GetAsync(_unitOfWork, playerId, cancellationToken).ConfigureAwait(false)
                : await PlayerStateCache.ReadRowAsync(_unitOfWork, playerId, cancellationToken).ConfigureAwait(false);
        }

        return _vitals.GetOrAdd(playerId, _ => CreateVitalsFromSnapshot(loaded));
    }

    private static PlayerVitalsState CreateVitalsFromSnapshot(PlayerStateCache.Snapshot? snapshot)
    {
        if (snapshot is not { Exists: true } s)
        {
            return new PlayerVitalsState();
        }

        int xp = Math.Clamp(s.CombatXp, 0, SkillRules.XpCap);
        int level = SkillRules.LevelOf(xp);
        int maxHp = CombatRules.MaxHpForLevel(level);
        int maxMp = CombatRules.MaxMpForLevel(level);
        int hp = s.Hp > 0 ? Math.Clamp(s.Hp, 1, maxHp) : maxHp;
        int mp = Math.Clamp(s.Mp, 0, maxMp);
        return new PlayerVitalsState
        {
            CombatXp = xp,
            MaxHp = maxHp,
            MaxMp = maxMp,
            Hp = hp,
            Mp = mp,
        };
    }

    [SuppressMessage("Design", "CA1031", Justification = "Vitals persistence is best-effort alongside in-memory authoritative state.")]
    private async ValueTask SaveVitalsAsync(Guid playerId, VitalsView view, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _stateCache?.Apply(playerId, s => s with { CombatXp = view.CombatXp, Hp = view.Hp, Mp = view.Mp, UpdatedAt = now });

        if (_unitOfWork is null)
        {
            return;
        }

        try
        {
            StripedAsyncLock.Releaser gate = _stateCache is not null
                ? await _stateCache.Locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false)
                : default;
            try
            {
                IUnitOfWork unit = _unitOfWork.Create();
                await using (unit.ConfigureAwait(false))
                {
                    IRepository<PlayerState> repo = unit.Repository<PlayerState>();
                    PlayerState? row = await repo.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                    if (row is null)
                    {
                        repo.Add(new PlayerState
                        {
                            PlayerId = playerId,
                            MapName = string.Empty,
                            Energy = EnergyRules.MaxEnergy,
                            EnergyRefilledDay = -1,
                            CombatXp = view.CombatXp,
                            Hp = view.Hp,
                            Mp = view.Mp,
                            UpdatedAt = now,
                        });
                    }
                    else
                    {
                        row.CombatXp = view.CombatXp;
                        row.Hp = view.Hp;
                        row.Mp = view.Mp;
                        row.UpdatedAt = now;
                        repo.Update(
                            row,
                            nameof(PlayerState.CombatXp),
                            nameof(PlayerState.Hp),
                            nameof(PlayerState.Mp),
                            nameof(PlayerState.UpdatedAt));
                    }

                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                _stateCache?.Apply(playerId, s => s with { Exists = true, CombatXp = view.CombatXp, Hp = view.Hp, Mp = view.Mp, UpdatedAt = now });
            }
            finally
            {
                if (_stateCache is not null)
                {
                    gate.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _stateCache?.Forget(playerId);
        }
    }

    private InstanceMonsters GetOrCreateInstance(PlayerPresence player)
    {
        long today = _dayClock.Today;
        string key = $"{player.Key}:{today}";
        return _instances.GetOrAdd(key, k =>
        {
            if (!_maps.TryGet(player.MapId, out MapDefinition? map))
            {
                return new InstanceMonsters([], []);
            }

            int floor = GameMaps.TryGet(player.MapId, out MapSpec? spec) ? spec.MineFloor : 0;
            ulong seed = floor >= 1
                ? MineRules.FloorSeed(player.AccountId, today, floor)
                : NodeRandom.Mix(NodeRandom.HashOf(player.MapId), today, 0x4D4F4E);

            HashSet<(int X, int Y)> occupied = [];
            foreach ((int rx, int ry) in map.ReservedTiles)
            {
                _ = occupied.Add((rx, ry));
            }

            int tileSize = Math.Max(1, map.Grid.TileSize);
            int spawnX = (int)(map.SpawnX / tileSize);
            int spawnY = (int)(map.SpawnY / tileSize);
            if (floor >= 1)
            {
                MineFloorPlanner.Plan nodePlan = MineFloorPlanner.Build(floor, seed, map.Grid, map.ReservedTiles, spawnX, spawnY);
                foreach (NodePlacement node in nodePlan.Nodes)
                {
                    _ = occupied.Add((node.X, node.Y));
                }
            }
            else
            {
                foreach (NodeSite site in map.NodeSites)
                {
                    _ = occupied.Add((site.X, site.Y));
                }
            }

            ExcludeUnreachableTiles(map.Grid, spawnX, spawnY, occupied);

            List<MonsterSpawn> planned = CombatRules.PlanMonsters(player.MapId, seed, map.Grid, occupied, spawnX, spawnY);
            foreach (MonsterSpawn m in planned)
            {
                _ = occupied.Add((m.X, m.Y));
            }

            List<DungeonHazard> hazards = CombatRules.PlanHazards(player.MapId, seed, map.Grid, spawnX, spawnY, occupied);
            foreach (DungeonHazard h in hazards)
            {
                _ = occupied.Add((h.X, h.Y));
            }

            List<DungeonHazard> shrines = CombatRules.PlanShrines(player.MapId, seed, map.Grid, spawnX, spawnY, occupied);
            hazards.AddRange(shrines);
            return new InstanceMonsters(planned, hazards);
        });
    }

    private static void ExcludeUnreachableTiles(WalkGrid grid, int spawnX, int spawnY, HashSet<(int X, int Y)> occupied)
    {
        if (grid.Width <= 0 || grid.Height <= 0 || grid.IsBlocked(spawnX, spawnY))
        {
            return;
        }

        bool[,] reachable = new bool[grid.Width, grid.Height];
        Queue<(int X, int Y)> queue = new();
        reachable[spawnX, spawnY] = true;
        queue.Enqueue((spawnX, spawnY));

        ReadOnlySpan<(int Dx, int Dy)> dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (queue.Count > 0)
        {
            (int cx, int cy) = queue.Dequeue();
            foreach ((int dx, int dy) in dirs)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if ((uint)nx < (uint)grid.Width && (uint)ny < (uint)grid.Height && !reachable[nx, ny] && !grid.IsBlocked(nx, ny))
                {
                    reachable[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (!grid.IsBlocked(x, y) && !reachable[x, y])
                {
                    _ = occupied.Add((x, y));
                }
            }
        }
    }

    private async ValueTask BroadcastMonsterChangedAsync(string instanceKey, MonsterSpawn monster, CancellationToken cancellationToken)
    {
        List<PlayerPresence> present = [];
        _presence.CopyPlayers(instanceKey, present);
        if (present.Count == 0 || _notifier is null)
        {
            return;
        }

        foreach (PlayerPresence p in present)
        {
            using PacketScope<MonsterChanged> lease = PacketFactory<MonsterChanged>.Acquire();
            lease.Value.MonsterId = monster.MonsterId;
            lease.Value.Kind = (byte)monster.Kind;
            lease.Value.X = (ushort)monster.X;
            lease.Value.Y = (ushort)monster.Y;
            lease.Value.Hp = (ushort)monster.Hp;
            lease.Value.MaxHp = (ushort)monster.MaxHp;
            lease.Value.Modifier = (byte)monster.Modifier;
            lease.Value.Enraged = (byte)(CombatRules.IsEnraged(monster) ? 1 : 0);
            _ = await _notifier.TrySendAsync(p.AccountId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private ValueTask OnPlayerLeftAsync(PlayerPresence player)
    {
        if (!player.Instance.IsPrivate)
        {
            return ValueTask.CompletedTask;
        }

        List<PlayerPresence> remaining = [];
        _presence.CopyPlayers(player.Key, remaining);
        if (remaining.Count == 0)
        {
            string prefix = player.Key + ":";
            foreach (string key in _instances.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    _ = _instances.TryRemove(key, out _);
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}
