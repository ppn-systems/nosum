// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Combat;

/// <summary>
/// Deterministic monster species selection, boss floor assignment, reachability mask calculation, and spawn planning
/// for mine floors and wilderness maps (docs/adr/ADR-045 D3, ADR-047, ADR-058).
/// </summary>
public static class MonsterSpawnPlanner
{
    /// <summary>Returns whether a monster species is a dungeon floor boss (docs/adr/ADR-047).</summary>
    /// <param name="kind">Monster species.</param>
    /// <returns><see langword="true"/> when the monster is a boss.</returns>
    public static bool IsBoss(MonsterKind kind) => kind is MonsterKind.BossBroodmother
        or MonsterKind.BossGoblinChieftain
        or MonsterKind.BossMagmaColossus
        or MonsterKind.BossGolemKing;

    /// <summary>Returns whether a mine floor is a milestone boss floor (10, 20, 30, 40).</summary>
    /// <param name="floor">Mine floor (1..40).</param>
    /// <returns><see langword="true"/> when a floor holds a boss encounter.</returns>
    public static bool IsBossFloor(int floor) => floor is 10 or 20 or 30 or 40;

    /// <summary>Returns the designated boss species for a milestone mine floor.</summary>
    /// <param name="floor">Mine floor (1..40).</param>
    /// <returns>The boss species or <see cref="MonsterKind.None"/>.</returns>
    public static MonsterKind BossForFloor(int floor) => floor switch
    {
        10 => MonsterKind.BossBroodmother,
        20 => MonsterKind.BossGoblinChieftain,
        30 => MonsterKind.BossMagmaColossus,
        40 => MonsterKind.BossGolemKing,
        _ => MonsterKind.None,
    };

    /// <summary>Selects a monster species for a given mine floor and spawn index.</summary>
    /// <param name="floor">Mine floor (1..40).</param>
    /// <param name="index">Spawn slot index.</param>
    /// <returns>The monster species.</returns>
    public static MonsterKind SpeciesForMineFloor(int floor, int index)
    {
        if (index == 0 && IsBossFloor(floor))
        {
            return BossForFloor(floor);
        }

        if (floor <= 10)
        {
            return (index % 3) switch
            {
                0 => MonsterKind.GreenSlime,
                1 => MonsterKind.CaveBat,
                _ => MonsterKind.SporeShroom,
            };
        }

        if (floor <= 25)
        {
            return (index % 3) switch
            {
                0 => MonsterKind.FrostSlime,
                1 => MonsterKind.GoblinScout,
                _ => MonsterKind.SporeShroom,
            };
        }

        return (index % 3) switch
        {
            0 => MonsterKind.MagmaSlime,
            1 => MonsterKind.SkeletonWarrior,
            _ => MonsterKind.StoneGolem,
        };
    }

    /// <summary>Computes a 2D reachability mask via 4-directional BFS from <paramref name="spawnX"/>, <paramref name="spawnY"/> so callers can share one flood-fill across monster, hazard, and shrine planners.</summary>
    /// <param name="grid">The map's walk grid.</param>
    /// <param name="spawnX">Player arrival tile X.</param>
    /// <param name="spawnY">Player arrival tile Y.</param>
    /// <returns>A <c>bool[width, height]</c> mask of reachable tiles, or <see langword="null"/> when the start tile is blocked or isolated.</returns>
    public static bool[,]? ComputeReachableMask(WalkGrid grid, int spawnX, int spawnY)
    {
        ArgumentNullException.ThrowIfNull(grid);

        int startX = Math.Clamp(spawnX, 0, Math.Max(0, grid.Width - 1));
        int startY = Math.Clamp(spawnY, 0, Math.Max(0, grid.Height - 1));
        if (grid.IsBlocked(startX, startY))
        {
            return null;
        }

        bool[,] mask = new bool[grid.Width, grid.Height];
        mask[startX, startY] = true;
        int count = 1;
        Queue<(int X, int Y)> queue = new();
        queue.Enqueue((startX, startY));
        ReadOnlySpan<(int Dx, int Dy)> dirs = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        while (queue.Count > 0)
        {
            (int cx, int cy) = queue.Dequeue();
            foreach ((int dx, int dy) in dirs)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if (nx >= 0 && nx < grid.Width && ny >= 0 && ny < grid.Height && !grid.IsBlocked(nx, ny) && !mask[nx, ny])
                {
                    mask[nx, ny] = true;
                    count++;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        return count > 4 ? mask : null;
    }

    /// <summary>Plans deterministic monster spawns for a mine floor or wilderness map.</summary>
    /// <param name="mapId">The map id (<c>mine1</c>..<c>mine40</c>, <c>Woods</c>, <c>Mountain</c>, <c>Backwoods</c>).</param>
    /// <param name="seed">Deterministic seed.</param>
    /// <param name="grid">The map's walk grid.</param>
    /// <param name="occupied">Tiles already occupied by nodes or arrival points.</param>
    /// <param name="spawnX">Player arrival X to keep clear.</param>
    /// <param name="spawnY">Player arrival Y to keep clear.</param>
    /// <param name="reachableMask">Optional precomputed reachability mask from <see cref="ComputeReachableMask"/>.</param>
    /// <returns>The spawned monsters.</returns>
    public static List<MonsterSpawn> PlanMonsters(
        string mapId,
        ulong seed,
        WalkGrid grid,
        IReadOnlySet<(int X, int Y)> occupied,
        int spawnX,
        int spawnY,
        bool[,]? reachableMask = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(occupied);

        int floor = 0;
        bool isMine = mapId.StartsWith("mine", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(mapId.AsSpan(4), out floor)
            && floor >= 1
            && floor <= MineRules.MaxFloor;
        bool isWoods = string.Equals(mapId, GameMaps.Woods, StringComparison.OrdinalIgnoreCase) || string.Equals(mapId, GameMaps.Forest, StringComparison.OrdinalIgnoreCase);
        bool isMountain = string.Equals(mapId, GameMaps.Mountain, StringComparison.OrdinalIgnoreCase) || string.Equals(mapId, GameMaps.Backwoods, StringComparison.OrdinalIgnoreCase);

        if (!isMine && !isWoods && !isMountain)
        {
            return [];
        }

        bool[,]? reachable = reachableMask ?? ComputeReachableMask(grid, spawnX, spawnY);
        List<(int X, int Y)> nearCandidates = [];
        List<(int X, int Y)> allCandidates = [];
        for (int y = 2; y < grid.Height - 2; y++)
        {
            for (int x = 2; x < grid.Width - 2; x++)
            {
                int dist = Math.Max(Math.Abs(x - spawnX), Math.Abs(y - spawnY));
                if (!grid.IsBlocked(x, y)
                    && !occupied.Contains((x, y))
                    && (reachable is null || reachable[x, y])
                    && dist >= 3)
                {
                    if (isMine && dist <= 7)
                    {
                        nearCandidates.Add((x, y));
                    }

                    allCandidates.Add((x, y));
                }
            }
        }

        List<(int X, int Y)> candidates = nearCandidates.Count >= 4 ? nearCandidates : allCandidates;
        if (candidates.Count == 0)
        {
            return [];
        }

        NodeRandom random = new(NodeRandom.Mix(seed, 0x4D4F4E53, floor));
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int count = isMine
            ? Math.Clamp(3 + (floor / 8), 3, 8)
            : 5;

        List<MonsterSpawn> spawns = new(Math.Min(count, candidates.Count));
        for (int i = 0; i < count && i < candidates.Count; i++)
        {
            MonsterKind kind = isMine
                ? SpeciesForMineFloor(floor, i)
                : isWoods
                    ? (i % 2 == 0 ? MonsterKind.GreenSlime : MonsterKind.SporeShroom)
                    : (i % 2 == 0 ? MonsterKind.GoblinScout : MonsterKind.FrostSlime);

            if (!MonsterCatalog.TryGet(kind, out MonsterDefinition? def))
            {
                continue;
            }

            MonsterModifier modifier = MonsterModifier.None;
            if (!IsBoss(kind) && random.Next(100) < 35)
            {
                modifier = (MonsterModifier)(1 + random.Next(4));
            }

            int maxHp = def.MaxHp;
            if (modifier == MonsterModifier.Shielded)
            {
                maxHp = (int)(maxHp * 1.5);
            }
            else if (modifier == MonsterModifier.FlameAura)
            {
                maxHp = (int)(maxHp * 1.25);
            }
            else if (modifier == MonsterModifier.Vampiric)
            {
                maxHp = (int)(maxHp * 1.2);
            }

            (int x, int y) = candidates[i];
            spawns.Add(new MonsterSpawn(i + 1, kind, x, y, maxHp, maxHp, modifier));
        }

        return spawns;
    }
}
