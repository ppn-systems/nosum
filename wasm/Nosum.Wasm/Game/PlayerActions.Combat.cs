// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Core;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Audio;

namespace Nosum.Wasm.Game;

internal sealed partial class PlayerActions
{
    /// <summary>Gets the walking speed multiplier of the player's active speed buff (1 for none); the server validates the same factor (issue #146).</summary>
    public double MoveSpeedFactor => _combat is null ? 1 : StatusEffectRules.MoveSpeedFactor((StatusEffectKind)_combat.Buff);

    /// <summary>Gets the active tool/weapon swing animation progress in <c>[0, 1)</c>, if a swing is playing.</summary>
    /// <param name="nowSeconds">Current scene clock in seconds.</param>
    /// <param name="progress">Normalized progress from 0 (wind-up) to 1 (complete).</param>
    /// <param name="itemId">Item id being swung.</param>
    /// <returns><see langword="true"/> while a swing animation is active.</returns>
    public bool TryGetActiveSwing(double nowSeconds, out float progress, out int itemId)
    {
        double elapsed = nowSeconds - _swingStartSeconds;
        if (elapsed >= 0.0 && elapsed < _swingSeconds)
        {
            progress = (float)(elapsed / _swingSeconds);
            itemId = _swingItemId;
            return true;
        }

        progress = -1f;
        itemId = 0;
        return false;
    }

    /// <summary>Gets the active combat skill visual effect, if any skill was recently triggered.</summary>
    /// <param name="nowMs">Current tick count in milliseconds.</param>
    /// <param name="skillId">Skill id (<c>1..3</c>).</param>
    /// <param name="progress">Normalized animation progress in <c>[0, 1)</c>.</param>
    /// <param name="playerX">Farmer tile X when cast.</param>
    /// <param name="playerY">Farmer tile Y when cast.</param>
    /// <returns><see langword="true"/> while a skill effect animation is active.</returns>
    public bool TryGetActiveSkillEffect(long nowMs, out byte skillId, out float progress, out int playerX, out int playerY)
    {
        if (_combat is not null)
        {
            return _combat.TryGetActiveSkillEffect(nowMs, out skillId, out progress, out playerX, out playerY);
        }

        skillId = 0;
        progress = 0f;
        playerX = 0;
        playerY = 0;
        return false;
    }

    private void TryCastSkillFromEdges(InputEdges edges, int x, int y, double nowSeconds)
    {
        if (_combat is null)
        {
            return;
        }

        if ((edges & InputEdges.Skill1) != 0 && this.CanCastSkill(1))
        {
            this.BeginSwing(this.HeldTool(), this.HeldItemId, nowSeconds);
            _combat.CastSkill(1);
        }
        else if ((edges & InputEdges.Skill2) != 0 && this.CanCastSkill(2))
        {
            int targetId = 0;
            if (_combat.TryFindTarget(x, y, 6, out MonsterClientRow tm))
            {
                targetId = tm.MonsterId;
            }

            this.BeginSwing(this.HeldTool(), this.HeldItemId, nowSeconds);
            _combat.CastSkill(2, targetId);
        }
        else if ((edges & InputEdges.Skill3) != 0 && this.CanCastSkill(3))
        {
            this.BeginSwing(this.HeldTool(), this.HeldItemId, nowSeconds);
            _combat.CastSkill(3);
        }
    }

    private void TryExecuteActionKey(int x, int y, double nowSeconds, int tileSize)
    {
        _nextUseSeconds = nowSeconds + OpenCooldownSeconds;
        int selected = _inventory.SelectedSlot;
        SlotInfo held = _inventory.SlotAt(selected);
        if (PlaceableCatalog.TryGet(held.ItemId, out _))
        {
            // A placeable in hand is set down by the placement actions, never swung at a tile.
            return;
        }

        if (IsFishingRod(held.ItemId))
        {
            // A rod is cast at water by a click, never used on a tile: the action key does nothing with it (docs/adr/ADR-078 D2).
            return;
        }

        if (IsShippingBin(this.TargetX, this.TargetY) && ShippingPrices.TryGet(held.ItemId, out _))
        {
            // Facing the bin with a crop in hand: sell the stack (the server checks the distance and prices it).
            _market.Ship(selected, held.ItemId, 0);
        }
        else if (CombatRules.WeaponDamage(held.ItemId) > 0)
        {
            ToolKind weapon = ItemCatalog.TryGet(held.ItemId, out ItemDefinition? weaponDef) ? weaponDef.Tool : ToolKind.None;
            this.BeginSwing(weapon, held.ItemId, nowSeconds);
            this.PlaySwing(weapon);

            if (_combat is not null && _combat.TryFindTarget(x, y, CombatRules.AttackRange(held.ItemId), out MonsterClientRow targetMonster))
            {
                if (held.ItemId == ItemCatalog.ArcaneStaff)
                {
                    _combat.SpawnProjectile(x, y, targetMonster.X, targetMonster.Y, 1);
                }

                _combat.Attack(targetMonster.MonsterId, selected);
            }
        }
        else if (this.CanUsePotion(held.ItemId) && !this.FacesCrop())
        {
            _audio?.Play(SoundIds.Pickup);
            _combat?.DrinkPotion(selected);
        }
        else if (_progress is { MaxEnergy: > 0 } && _progress.Energy < _progress.MaxEnergy && ItemCatalog.TryGet(held.ItemId, out ItemDefinition? food) && food.IsEdible
            && !this.FacesCrop())
        {
            // Food in hand, room for energy and no crop in front (facing a crop the action harvests it): eat it (the server removes one and
            // restores its energy, docs/adr/ADR-023 D2).
            _progress.Eat(selected);
        }
        else
        {
            // The swing is the action: the use reaches the server at its hit frame, and the farmer is busy until the swing and its recovery are over.
            ToolKind tool = ItemCatalog.TryGet(held.ItemId, out ItemDefinition? swung) ? swung.Tool : ToolKind.None;
            this.BeginSwing(tool, held.ItemId, nowSeconds);
            this.QueueUse(selected, this.TargetX, this.TargetY, 1, tool, tileSize);
        }
    }

    private bool CanCastSkill(byte skillId)
    {
        if (_combat is null || !SkillCatalog.TryGet((CombatSkillKind)skillId, out SkillDefinition skill))
        {
            return false;
        }

        if (_combat.GetSkillCooldownRemaining(skillId) > 0f || _combat.Mp < skill.MpCost)
        {
            return false;
        }

        return skillId != (byte)CombatSkillKind.DivineHeal || _combat.Hp < _combat.MaxHp;
    }

    private bool CanUsePotion(int itemId)
    {
        if (_combat is null || !CombatRules.IsPotion(itemId))
        {
            return false;
        }

        if (itemId is ItemCatalog.ArcaneGrimoire or ItemCatalog.HeartCrystal)
        {
            return true;
        }

        if (CombatRules.PotionEffect(itemId).Effect != StatusEffectKind.None)
        {
            return true;
        }

        (int hpRestore, int mpRestore) = CombatRules.PotionRestore(itemId);
        bool healsHp = hpRestore > 0 && _combat.Hp < _combat.MaxHp;
        bool restoresMp = mpRestore > 0 && _combat.Mp < _combat.MaxMp;
        return healsHp || restoresMp;
    }

    // The swing sounds when the key is pressed, not when the server answers: the sound is feedback for the key, the server still decides what the swing did.
    private void PlaySwing(ToolKind tool) => _audio?.Play(AudioCatalog.ToolSound(tool));
}
