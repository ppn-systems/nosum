// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nosum.Abstractions.Combat;
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

        public VitalsView ToView() => new(Hp, MaxHp, Mp, MaxMp, CombatXp, (byte)ActiveEffect);
    }

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
    }
}
