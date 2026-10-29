// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Combat;

/// <summary>
/// Pure rules and deterministic placement for dungeon environmental hazards (spike traps, spore vents, magma fissures; docs/adr/ADR-061).
/// </summary>
public static class DungeonHazardRules
{
    /// <summary>Duration in seconds of the poison debuff applied by a <see cref="DungeonHazardKind.SporeVent"/>.</summary>
    public const int SporeVentTicks = 4;

    /// <summary>Damage per tick of the poison debuff applied by a <see cref="DungeonHazardKind.SporeVent"/>.</summary>
    public const int SporeVentMagnitude = 3;

    /// <summary>Determines whether a rhythmic <see cref="DungeonHazardKind.SpikeTrap"/> is extended and dealing damage at <paramref name="nowMs"/>.</summary>
    /// <param name="hazardId">Hazard id within the floor instance.</param>
    /// <param name="nowMs">Server or client timestamp in Unix milliseconds.</param>
    /// <returns><see langword="true"/> when the spikes are extended.</returns>
    public static bool IsSpikeTrapActive(int hazardId, long nowMs)
        => ((nowMs + (hazardId * 700L)) % 2400L) > 1560L;

    /// <summary>
    /// Minimum time in milliseconds between two contact hits from the same hazard on the same player (issue #168). A spike
    /// trap hits once per extension cycle, so a player who crosses an open spike during its extended window is struck
    /// once and never on every move; other hazards keep the one-hit-per-AI-tick cadence.
    /// </summary>
    /// <param name="kind">Hazard kind.</param>
    /// <returns>Cooldown in milliseconds.</returns>
    public static int ContactCooldownMs(DungeonHazardKind kind) => kind == DungeonHazardKind.SpikeTrap ? 2400 : 1500;

    /// <summary>
    /// Plans deterministic environmental hazards for a dungeon floor or map (docs/adr/ADR-061).
    /// </summary>
    /// <param name="mapId">Map or floor identifier.</param>
    /// <param name="seed">Deterministic world seed.</param>
    /// <param name="grid">Collision grid of the map.</param>
    /// <param name="spawnX">Player arrival tile X.</param>
    /// <param name="spawnY">Player arrival tile Y.</param>
    /// <param name="occupied">Optional set of tiles already occupied by resource nodes or monsters.</param>
    /// <param name="reachableMask">Optional precomputed reachability mask from <see cref="MonsterSpawnPlanner.ComputeReachableMask"/>.</param>
    /// <returns>A list of planned hazards.</returns>
    public static List<DungeonHazard> PlanHazards(
        string mapId,
        ulong seed,
        WalkGrid grid,
        int spawnX,
        int spawnY,
        IReadOnlySet<(int X, int Y)>? occupied = null,
        bool[,]? reachableMask = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapId);
        ArgumentNullException.ThrowIfNull(grid);

        int floor = 0;
        bool isMine = mapId.StartsWith("mine", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(mapId.AsSpan(4), out floor)
            && floor >= 1
            && floor <= MineRules.MaxFloor;

        if (!isMine)
        {
            return [];
        }

        bool[,]? reachable = reachableMask ?? MonsterSpawnPlanner.ComputeReachableMask(grid, spawnX, spawnY);
        List<(int X, int Y)> nearCandidates = [];
        List<(int X, int Y)> allCandidates = [];
        for (int y = 2; y < grid.Height - 2; y++)
        {
            for (int x = 2; x < grid.Width - 2; x++)
            {
                int dist = Math.Max(Math.Abs(x - spawnX), Math.Abs(y - spawnY));
                if (!grid.IsBlocked(x, y)
                    && (occupied is null || !occupied.Contains((x, y)))
                    && (reachable is null || reachable[x, y])
                    && dist >= 2)
                {
                    if (dist <= 6)
                    {
                        nearCandidates.Add((x, y));
                    }

                    allCandidates.Add((x, y));
                }
            }
        }

        List<(int X, int Y)> candidates = nearCandidates.Count >= 2 ? nearCandidates : allCandidates;
        if (candidates.Count == 0)
        {
            return [];
        }

        NodeRandom random = new(NodeRandom.Mix(seed, 0x48415A44, floor));
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int count = Math.Clamp(2 + (floor / 10), 2, 6);
        DungeonHazardKind kind = floor switch
        {
            <= 10 => DungeonHazardKind.SporeVent,
            <= 20 => DungeonHazardKind.SpikeTrap,
            _ => DungeonHazardKind.MagmaFissure,
        };

        byte damage = kind switch
        {
            DungeonHazardKind.SporeVent => 4,
            DungeonHazardKind.SpikeTrap => 8,
            DungeonHazardKind.MagmaFissure => 14,
            _ => 0,
        };

        List<DungeonHazard> hazards = new(Math.Min(count, candidates.Count));
        for (int i = 0; i < count && i < candidates.Count; i++)
        {
            (int x, int y) = candidates[i];
            hazards.Add(new DungeonHazard((byte)(i + 1), kind, (short)x, (short)y, damage));
        }

        return hazards;
    }
}
