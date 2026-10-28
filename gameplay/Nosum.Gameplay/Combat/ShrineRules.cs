// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Combat;

/// <summary>
/// Pure rules and deterministic placement for beneficial Dungeon Blessing Shrines, Overworld Wayshrines, and Elemental Resonance Pylons
/// (docs/adr/ADR-063, ADR-064).
/// </summary>
public static class ShrineRules
{
    /// <summary>Cooldown in milliseconds per player per sanctuary shrine blessing (docs/adr/ADR-063).</summary>
    public const int ShrineCooldownMs = 45000;

    /// <summary>Returns <see langword="true"/> when <paramref name="kind"/> is a beneficial dungeon blessing shrine or elemental resonance pylon (docs/adr/ADR-063, ADR-064).</summary>
    /// <param name="kind">Hazard or shrine kind.</param>
    /// <returns><see langword="true"/> for sanctuary shrines and resonance pylons; <see langword="false"/> for damaging traps.</returns>
    public static bool IsShrine(DungeonHazardKind kind) => kind switch
    {
        DungeonHazardKind.VitalityShrine or
        DungeonHazardKind.ArcaneObelisk or
        DungeonHazardKind.EmpowermentAltar or
        DungeonHazardKind.ThunderPylon or
        DungeonHazardKind.SolarWayshrine or
        DungeonHazardKind.FrostMonolith => true,
        _ => false,
    };

    /// <summary>Returns <see langword="true"/> when <paramref name="kind"/> is an elemental resonance pylon that also discharges damage against nearby monsters (docs/adr/ADR-064).</summary>
    /// <param name="kind">Hazard or shrine kind.</param>
    /// <returns><see langword="true"/> for elemental resonance pylons and wayshrines.</returns>
    public static bool IsResonancePylon(DungeonHazardKind kind) => kind switch
    {
        DungeonHazardKind.ThunderPylon or DungeonHazardKind.SolarWayshrine or DungeonHazardKind.FrostMonolith => true,
        _ => false,
    };

    /// <summary>Returns the elemental discharge parameters against nearby monsters when a resonance pylon is triggered (docs/adr/ADR-064).</summary>
    /// <param name="kind">Pylon kind.</param>
    /// <returns>Tuple of <c>(MonsterDamage, RadiusTiles, SkillKind)</c>.</returns>
    public static (int MonsterDamage, int RadiusTiles, CombatSkillKind SkillKind) PylonDischarge(DungeonHazardKind kind) => kind switch
    {
        DungeonHazardKind.ThunderPylon => (18, 3, CombatSkillKind.Fireball),
        DungeonHazardKind.SolarWayshrine => (14, 3, CombatSkillKind.Whirlwind),
        DungeonHazardKind.FrostMonolith => (16, 3, CombatSkillKind.Fireball),
        _ => (0, 0, CombatSkillKind.None),
    };

    /// <summary>Returns the blessing rewards granted by a sanctuary shrine or resonance pylon (docs/adr/ADR-063, ADR-064).</summary>
    /// <param name="kind">Shrine kind.</param>
    /// <returns>Tuple of <c>(HealHp, RestoreMp, BonusXp, Buff)</c>.</returns>
    public static (int HealHp, int RestoreMp, int BonusXp, StatusEffectKind Buff) ShrineBlessing(DungeonHazardKind kind) => kind switch
    {
        DungeonHazardKind.VitalityShrine => (10, 0, 0, StatusEffectKind.Regeneration),
        DungeonHazardKind.ArcaneObelisk => (0, 15, 10, StatusEffectKind.None),
        DungeonHazardKind.EmpowermentAltar => (12, 12, 15, StatusEffectKind.Swiftness),
        DungeonHazardKind.ThunderPylon => (5, 12, 12, StatusEffectKind.Swiftness),
        DungeonHazardKind.SolarWayshrine => (12, 10, 12, StatusEffectKind.Regeneration),
        DungeonHazardKind.FrostMonolith => (8, 12, 12, StatusEffectKind.Regeneration),
        _ => (0, 0, 0, StatusEffectKind.None),
    };

    /// <summary>
    /// Deterministically plans beneficial Dungeon Blessing Shrines, Overworld Wayshrines, and Elemental Resonance Pylons on a combat map (docs/adr/ADR-063, ADR-064).
    /// </summary>
    /// <param name="mapId">Map id (e.g., <c>mine10</c>, <c>forest</c>, <c>mountain</c>).</param>
    /// <param name="seed">Deterministic instance seed.</param>
    /// <param name="grid">Map walkability grid.</param>
    /// <param name="spawnX">Player arrival tile X.</param>
    /// <param name="spawnY">Player arrival tile Y.</param>
    /// <param name="occupied">Optional set of already-occupied tiles to avoid.</param>
    /// <param name="reachableMask">Optional precomputed reachability mask from <see cref="MonsterSpawnPlanner.ComputeReachableMask"/>.</param>
    /// <returns>Planned shrines and resonance pylons for the map.</returns>
    public static List<DungeonHazard> PlanShrines(
        string mapId,
        ulong seed,
        WalkGrid grid,
        int spawnX,
        int spawnY,
        IReadOnlySet<(int X, int Y)>? occupied = null,
        bool[,]? reachableMask = null)
    {
        ArgumentNullException.ThrowIfNull(mapId);
        ArgumentNullException.ThrowIfNull(grid);

        int floor = 0;
        bool isMine = mapId.StartsWith("mine", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(mapId.AsSpan(4), out floor)
            && floor >= 1
            && floor <= MineRules.MaxFloor;
        bool isWoods = string.Equals(mapId, GameMaps.Woods, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapId, GameMaps.Forest, StringComparison.OrdinalIgnoreCase);
        bool isMountain = string.Equals(mapId, GameMaps.Mountain, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mapId, GameMaps.Backwoods, StringComparison.OrdinalIgnoreCase);

        if (!isMine && !isWoods && !isMountain)
        {
            return [];
        }

        int centerX = Math.Clamp(spawnX, 2, Math.Max(2, grid.Width - 3));
        int centerY = Math.Clamp(spawnY, 2, Math.Max(2, grid.Height - 3));

        bool[,]? reachable = reachableMask ?? MonsterSpawnPlanner.ComputeReachableMask(grid, spawnX, spawnY);
        List<(int X, int Y)> chamberCandidates = [];
        List<(int X, int Y)> fallbackCandidates = [];
        for (int y = 2; y < grid.Height - 2; y++)
        {
            for (int x = 2; x < grid.Width - 2; x++)
            {
                if (grid.IsBlocked(x, y)
                    || (occupied is not null && occupied.Contains((x, y)))
                    || (reachable is not null && !reachable[x, y]))
                {
                    continue;
                }

                if (x == spawnX && y == spawnY)
                {
                    continue;
                }

                int chamberDist = Math.Max(Math.Abs(x - centerX), Math.Abs(y - centerY));
                int maxDist = isMine ? 6 : 8;
                if (chamberDist >= 3 && chamberDist <= maxDist)
                {
                    chamberCandidates.Add((x, y));
                }

                fallbackCandidates.Add((x, y));
            }
        }

        List<(int X, int Y)> candidates = chamberCandidates.Count >= 3 ? chamberCandidates : fallbackCandidates;
        if (candidates.Count == 0)
        {
            return [];
        }

        NodeRandom random = new(NodeRandom.Mix(seed, 0x5348524E, floor));
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        if (!isMine)
        {
            int wildCount = Math.Min(2, candidates.Count);
            List<DungeonHazard> wildShrines = new(wildCount);
            for (int i = 0; i < wildCount; i++)
            {
                (int x, int y) = candidates[i];
                DungeonHazardKind kind = i == 0
                    ? (isWoods ? DungeonHazardKind.SolarWayshrine : DungeonHazardKind.FrostMonolith)
                    : DungeonHazardKind.ThunderPylon;
                wildShrines.Add(new DungeonHazard((byte)(100 + i + 1), kind, (short)x, (short)y, 0));
            }

            return wildShrines;
        }

        bool isBossFloor = (floor % 10) == 0;
        int sanctuaryCount = isBossFloor ? 2 : 1;
        int totalCount = sanctuaryCount + 1;
        List<DungeonHazard> shrines = new(Math.Min(totalCount, candidates.Count));
        for (int i = 0; i < totalCount && i < candidates.Count; i++)
        {
            (int x, int y) = candidates[i];
            DungeonHazardKind kind;
            if (i < sanctuaryCount)
            {
                kind = isBossFloor
                    ? (i == 0 ? DungeonHazardKind.EmpowermentAltar : DungeonHazardKind.VitalityShrine)
                    : ((floor % 2 == 0) ? DungeonHazardKind.ArcaneObelisk : DungeonHazardKind.VitalityShrine);
            }
            else
            {
                kind = (floor % 3) switch
                {
                    1 => DungeonHazardKind.ThunderPylon,
                    2 => DungeonHazardKind.SolarWayshrine,
                    _ => DungeonHazardKind.FrostMonolith,
                };
            }

            shrines.Add(new DungeonHazard((byte)(100 + i + 1), kind, (short)x, (short)y, 0));
        }

        return shrines;
    }
}
