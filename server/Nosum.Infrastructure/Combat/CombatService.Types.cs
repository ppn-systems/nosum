// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Combat;

namespace Nosum.Infrastructure.Combat;

public sealed partial class CombatService
{
    private sealed class PlayerVitalsState
    {
        public object Gate { get; } = new();

        public int Hp { get; set; } = CombatRules.BaseMaxHp;

        public int MaxHp { get; set; } = CombatRules.BaseMaxHp;

        public int Mp { get; set; } = CombatRules.BaseMaxMp;

        public int MaxMp { get; set; } = CombatRules.BaseMaxMp;

        public int CombatXp { get; set; }

        public StatusEffectKind ActiveEffect { get; set; }

        public int EffectRemainingTicks { get; set; }

        public int EffectMagnitude { get; set; }

        /// <summary>Speed buff in its own slot, separate from <see cref="ActiveEffect"/> (issue #146). Guarded by <see cref="Gate"/>.</summary>
        public StatusEffectKind ActiveBuff { get; set; }

        /// <summary>Tick count (ms) at which <see cref="ActiveBuff"/> runs out.</summary>
        public long BuffExpiresAt { get; set; }

        public VitalsView ToView() => new(Hp, MaxHp, Mp, MaxMp, CombatXp, (byte)ActiveEffect, (byte)ActiveBuff);
    }

    /// <summary>A monster contact strike collected under the instance gate and applied after it is released (issue #149).</summary>
    private readonly record struct PendingContact(PlayerPresence Player, int Damage, MonsterKind Kind, int MonsterId);

    /// <summary>A hazard hit collected under the instance gate and applied after it is released (issue #149).</summary>
    private readonly record struct PendingHazard(PlayerPresence Player, int Damage, DungeonHazardKind Kind);

    /// <summary>A shrine blessing collected under the instance gate and applied after it is released (issue #149).</summary>
    private readonly record struct PendingShrine(Guid PlayerId, DungeonHazard Shrine, long Now);

    private sealed class InstanceMonsters
    {
        public InstanceMonsters(List<MonsterSpawn> monsters, List<DungeonHazard> hazards)
        {
            Monsters = monsters;
            Hazards = hazards;
        }

        public object Gate { get; } = new();

        public List<MonsterSpawn> Monsters { get; }

        public List<DungeonHazard> Hazards { get; }

        /// <summary>Tick time (ms) before which each boss may start its next ground slam, keyed by monster id. Guarded by <see cref="Gate"/>.</summary>
        public Dictionary<int, long> SlamReadyAt { get; } = [];

        /// <summary>Ordinary monsters with a melee strike in its wind-up (docs/adr/ADR-097), keyed by monster id. Guarded by <see cref="Gate"/>.</summary>
        public HashSet<int> StrikeWindingUp { get; } = [];
    }
}
