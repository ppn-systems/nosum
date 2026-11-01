// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Combat;
using Nosum.Core;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.ViewModels.Audio;
using Nosum.Wasm.ViewModels.Progress;

namespace Nosum.Wasm.Game;

internal sealed partial class PlayerActions
{
    /// <summary>Gets the speed buff in the buff slot, or <see cref="StatusEffectKind.None"/>; the walker combines it with the boots (docs/adr/ADR-084 D3).</summary>
    public StatusEffectKind SpeedBuff => _combat is null ? StatusEffectKind.None : (StatusEffectKind)_combat.Buff;

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

    /// <summary>Raised with a resource key when a skill press is refused on the client before it is sent (no monster in reach, a cooldown, too little
    /// MP, full health for the heal), so the press is never silent (docs/adr/ADR-056 D5).</summary>
    public event Action<string>? SkillPressRefused;

    /// <summary>Raised with the resource key of the line when a farm use worked and the line says so (docs/adr/ADR-095 D2 and D4).</summary>
    public event Action<string>? FarmUseSucceeded;

    private void TryCastSkillFromEdges(InputEdges edges, int x, int y, double nowSeconds)
    {
        if (_combat is null)
        {
            return;
        }

        byte skillId = (edges & InputEdges.Skill1) != 0 ? (byte)1
            : (edges & InputEdges.Skill2) != 0 ? (byte)2
            : (edges & InputEdges.Skill3) != 0 ? (byte)3
            : (byte)0;
        if (skillId == 0 || _combat.IsSkillPending(skillId))
        {
            // No skill key, or a cast still in flight: nothing to start; the reply shows the result.
            return;
        }

        string? refusal = this.SkillRefusalKey(skillId, x, y);
        if (refusal is not null)
        {
            this.SkillPressRefused?.Invoke(refusal);
            return;
        }

        this.BeginSwing(this.HeldTool(), this.HeldItemId, nowSeconds);
        if (skillId == 2)
        {
            int targetId = 0;
            if (_combat.TryFindTarget(x, y, 6, out MonsterClientRow tm))
            {
                targetId = tm.MonsterId;
            }

            _combat.CastSkill(2, targetId);
        }
        else
        {
            _combat.CastSkill(skillId);
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
            // Facing the bin with a crop in hand: sell the stack (the server checks the distance and prices it). The coins pop over this tile when it pays.
            _shipTileX = this.TargetX;
            _shipTileY = this.TargetY;
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

    // A press that the server would refuse starts no swing: the same rules as the server's, checked on the client first, and the reason is named
    // (SkillPressRefused). The server still decides.
    private string? SkillRefusalKey(byte skillId, int x, int y)
    {
        if (_combat is null || !SkillCatalog.TryGet((CombatSkillKind)skillId, out SkillDefinition skill))
        {
            return null;
        }

        bool targetInReach = skill.Kind is not (CombatSkillKind.Whirlwind or CombatSkillKind.Fireball)
            || _combat.TryFindTarget(x, y, skill.Range, out _);
        return SkillPressRefusal.KeyFor(skill.Kind, _combat.GetSkillCooldownRemaining(skillId) > 0f, _combat.Mp, skill.MpCost, targetInReach, _combat.Hp >= _combat.MaxHp);
    }

    // The server refused a cast: a wind-up still in progress stops, so no skill animation plays for a cast that did nothing (docs/adr/ADR-056 D5).
    private void OnSkillCompleted(SkillInfo info)
    {
        if (info.Status == CombatStatus.Ok || _nowSeconds >= _swingStartSeconds + _swingSeconds)
        {
            return;
        }

        _swingStartSeconds = double.NegativeInfinity;
        _nextUseSeconds = _nowSeconds;
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
