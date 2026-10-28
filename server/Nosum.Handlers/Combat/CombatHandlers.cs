// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Combat;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Combat.S2C;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Inventory;
using Nosum.Handlers.Security;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Combat;

/// <summary>Packet handlers for player vitals (HP/MP), monster rosters, weapon/staff attacks, and potion drinking (docs/adr/ADR-045 D3).</summary>
[PacketHandler("Nosum.Combat")]
public static partial class CombatHandlers
{
    [Inject] private static ICombatService s_combat = null!;

    /// <summary>Replies with the player's current HP, MaxHp, MP, MaxMp and Combat XP.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CombatOpCode.GetVitals)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 4, PolicyId = "combat-vitals")]
    public static async ValueTask HandleGetVitalsAsync(IPacketContext<GetVitals> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();
        VitalsView view = await s_combat.GetVitalsAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<GetVitals, VitalsState>(context, sequence, r =>
        {
            r.Hp = (ushort)view.Hp;
            r.MaxHp = (ushort)view.MaxHp;
            r.Mp = (ushort)view.Mp;
            r.MaxMp = (ushort)view.MaxMp;
            r.CombatXp = view.CombatXp;
            r.StatusEffect = view.StatusEffect;
        }).ConfigureAwait(false);
    }

    /// <summary>Replies with the live monsters on the player's current map or mine floor.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CombatOpCode.GetMonsters)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 4, PolicyId = "combat-monsters")]
    public static async ValueTask HandleGetMonstersAsync(IPacketContext<GetMonsters> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        MonstersView view = await s_combat.GetMonstersAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<GetMonsters, MonstersSnapshot>(context, sequence, r =>
        {
            r.MapId = view.MapId;
            r.Monsters.Clear();
            foreach (MonsterSpawn m in view.Monsters)
            {
                MonsterRow row = MonsterRow.Create();
                row.MonsterId = m.MonsterId;
                row.Kind = (byte)m.Kind;
                row.X = (ushort)m.X;
                row.Y = (ushort)m.Y;
                row.Hp = (ushort)m.Hp;
                row.MaxHp = (ushort)m.MaxHp;
                row.Modifier = (byte)m.Modifier;
                r.Monsters.Add(row);
            }

            r.Hazards.Clear();
            foreach (DungeonHazard h in view.Hazards)
            {
                HazardRow row = HazardRow.Create();
                row.HazardId = h.HazardId;
                row.Kind = (byte)h.Kind;
                row.X = (ushort)h.X;
                row.Y = (ushort)h.Y;
                row.Damage = h.Damage;
                r.Hazards.Add(row);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Attacks a monster on the player's current map.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CombatOpCode.AttackMonster)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(12, 3, PolicyId = "combat-attack")]
    public static async ValueTask HandleAttackMonsterAsync(IPacketContext<AttackMonster> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int monsterId = context.Packet.MonsterId;
        int slot = context.Packet.Slot;
        if (!PacketBounds.IsWithinBounds(context.Packet))
        {
            CombatStatus badStatus = (uint)slot >= (uint)InventoryLimits.DefaultCapacity
                ? CombatStatus.WrongItem
                : CombatStatus.UnknownMonster;
            await Replies.ReplyAsync<AttackMonster, AttackMonsterResult>(context, sequence, r =>
            {
                r.Status = badStatus;
                r.MonsterId = monsterId;
            }).ConfigureAwait(false);
            return;
        }

        AttackOutcome outcome = await s_combat.AttackMonsterAsync(context.Connection, monsterId, slot, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<AttackMonster, AttackMonsterResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.MonsterId = outcome.MonsterId;
            r.DamageDealt = (ushort)outcome.DamageDealt;
            r.MonsterHp = (ushort)outcome.MonsterHp;
            r.DamageTaken = (ushort)outcome.DamageTaken;
            r.Hp = (ushort)outcome.Vitals.Hp;
            r.MaxHp = (ushort)outcome.Vitals.MaxHp;
            r.Mp = (ushort)outcome.Vitals.Mp;
            r.MaxMp = (ushort)outcome.Vitals.MaxMp;
            r.CombatXp = outcome.Vitals.CombatXp;
            r.KnockedOut = outcome.KnockedOut;
            r.ComboCount = outcome.ComboCount;
        }).ConfigureAwait(false);
    }

    /// <summary>Drinks a potion in a hotbar/inventory slot.</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CombatOpCode.DrinkPotion)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(8, 3, PolicyId = "combat-potion")]
    public static async ValueTask HandleDrinkPotionAsync(IPacketContext<DrinkPotion> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int slot = context.Packet.Slot;
        if (!PacketBounds.IsWithinBounds(context.Packet))
        {
            await Replies.ReplyAsync<DrinkPotion, DrinkPotionResult>(context, sequence, r => r.Status = CombatStatus.WrongItem).ConfigureAwait(false);
            return;
        }

        DrinkPotionOutcome outcome = await s_combat.DrinkPotionAsync(context.Connection, slot, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<DrinkPotion, DrinkPotionResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.Hp = (ushort)outcome.Vitals.Hp;
            r.MaxHp = (ushort)outcome.Vitals.MaxHp;
            r.Mp = (ushort)outcome.Vitals.Mp;
            r.MaxMp = (ushort)outcome.Vitals.MaxMp;
        }).ConfigureAwait(false);
    }

    /// <summary>Casts a combat skill (Whirlwind, Fireball, Divine Heal) (docs/adr/ADR-056).</summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(CombatOpCode.CastSkill)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(12, 3, PolicyId = "combat-skill")]
    public static async ValueTask HandleCastSkillAsync(IPacketContext<CastSkill> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        byte skillId = context.Packet.SkillId;
        int targetMonsterId = context.Packet.TargetMonsterId;
        if (!PacketBounds.IsWithinBounds(context.Packet))
        {
            CombatStatus badStatus = !SkillCatalog.TryGet((CombatSkillKind)skillId, out _)
                ? CombatStatus.UnknownSkill
                : CombatStatus.UnknownMonster;
            await Replies.ReplyAsync<CastSkill, CastSkillResult>(context, sequence, r =>
            {
                r.Status = badStatus;
                r.SkillId = skillId;
            }).ConfigureAwait(false);
            return;
        }

        CastSkillOutcome outcome = await s_combat.CastSkillAsync(context.Connection, skillId, targetMonsterId, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<CastSkill, CastSkillResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.SkillId = outcome.SkillId;
            r.DamageDealt = (ushort)outcome.DamageDealt;
            r.TargetsHit = outcome.TargetsHit;
            r.HealAmount = (ushort)outcome.HealAmount;
            r.Hp = (ushort)outcome.Vitals.Hp;
            r.MaxHp = (ushort)outcome.Vitals.MaxHp;
            r.Mp = (ushort)outcome.Vitals.Mp;
            r.MaxMp = (ushort)outcome.Vitals.MaxMp;
            r.CombatXp = outcome.Vitals.CombatXp;
            r.CooldownMs = (ushort)outcome.CooldownMs;
            r.ComboCount = outcome.ComboCount;
        }).ConfigureAwait(false);
    }
}
