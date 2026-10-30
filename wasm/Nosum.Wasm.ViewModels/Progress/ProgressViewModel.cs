// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Skills;
using Nosum.Wasm.Socket.Bestiary;
using Nosum.Contracts.Player;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Progress;

/// <summary>One skill as the Skills page shows it.</summary>
/// <param name="Name">The localized skill name.</param>
/// <param name="Level">The level, zero to ten.</param>
/// <param name="Xp">Cumulative experience.</param>
/// <param name="Fraction">How far the skill is to its next level, zero to one; one at the top level.</param>
/// <param name="Caption">The localized line under the bar, for example <c>45 / 100</c> or the max-level text.</param>
public sealed record SkillDisplay(string Name, int Level, int Xp, double Fraction, string Caption);

/// <summary>One monster entry in the bestiary and hunting log (docs/adr/ADR-059).</summary>
/// <param name="Name">Monster species display name.</param>
/// <param name="Category">Monster classification (e.g. Regular, Boss).</param>
/// <param name="Location">Encounter locations and mine floors.</param>
/// <param name="Hp">Base health points.</param>
/// <param name="Attack">Base attack power.</param>
/// <param name="Xp">Combat experience rewarded.</param>
/// <param name="PrimaryDrop">Primary loot item.</param>
/// <param name="RareDrop">Rare loot item.</param>
/// <param name="IsBoss">Whether the monster is a floor milestone boss.</param>
/// <param name="Species">The species, to equip its title.</param>
/// <param name="Kills">How many of the species the player has slain.</param>
/// <param name="Fraction">How far the player is to the next hunting milestone, zero to one; one once every milestone is reached.</param>
/// <param name="Progress">The localized line under the bar, for example <c>12 / 50 slain</c>.</param>
/// <param name="TitleName">The localized slayer title of the species.</param>
/// <param name="TitleEarned">Whether the slayer title is earned (the last milestone is reached).</param>
/// <param name="TitleEquipped">Whether the slayer title is the one the player wears.</param>
public sealed record BestiaryEntryDisplay(
    string Name,
    string Category,
    string Location,
    int Hp,
    int Attack,
    int Xp,
    string PrimaryDrop,
    string RareDrop,
    bool IsBoss,
    MonsterKind Species,
    int Kills,
    double Fraction,
    string Progress,
    string TitleName,
    bool TitleEarned,
    bool TitleEquipped);

/// <summary>
/// View model of the HUD vitals bars (HP, MP, Energy) beside the hotbar and of the Skills page (docs/adr/ADR-023 D6, docs/adr/ADR-045 D2).
/// </summary>
public sealed class ProgressViewModel : PanelViewModel, IDisposable
{
    /// <summary>How long the energy bar pulses after food restored energy.</summary>
    public static readonly TimeSpan EnergyPulseDuration = TimeSpan.FromMilliseconds(700);

    private readonly ProgressChannel _progress;
    private readonly CombatChannel? _combat;
    private readonly BestiaryChannel? _bestiary;
    private readonly IStringLocalizer<Catalogue>? _catalogue;
    private int _pulseVersion;

    /// <summary>Initializes the view model.</summary>
    /// <param name="progress">The energy and experience mirror.</param>
    /// <param name="text">UI text.</param>
    /// <param name="combat">Optional combat vitals and Combat XP mirror.</param>
    /// <param name="catalogue">Optional item and monster catalogue localizer.</param>
    /// <param name="bestiary">Optional hunting log (kill counts and slayer titles, docs/adr/ADR-068).</param>
    public ProgressViewModel(
        ProgressChannel progress,
        IStringLocalizer<Language> text,
        CombatChannel? combat = null,
        IStringLocalizer<Catalogue>? catalogue = null,
        BestiaryChannel? bestiary = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(progress);

        _progress = progress;
        _combat = combat;
        _bestiary = bestiary;
        _catalogue = catalogue;
        progress.Changed += this.Raise;
        progress.EatCompleted += this.OnEatCompleted;
        if (combat is not null)
        {
            combat.Changed += this.Raise;
            combat.ComboChanged += this.Raise;
            combat.KnockedOut += this.Raise;
            combat.LevelledUp += this.OnCombatLevelUp;
        }

        if (bestiary is not null)
        {
            bestiary.Changed += this.Raise;
        }
    }

    /// <summary>Asks the server for the hunting log; the Skills page calls it when it opens.</summary>
    public void LoadBestiary() => _bestiary?.Load();

    /// <summary>Gets the kill count that earns a slayer title: the last milestone.</summary>
    public static int TitleGoal => BestiaryRules.Milestones[^1];

    /// <summary>Equips the slayer title of an entry, or takes it off when the player wears it already.</summary>
    /// <param name="entry">The entry whose title is toggled; only an earned title may be equipped.</param>
    public void ToggleTitle(BestiaryEntryDisplay entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _bestiary?.Equip(entry.TitleEquipped ? MonsterKind.None : entry.Species);
    }

    /// <summary>Gets whether the server has told the player their energy yet; the bar is hidden before.</summary>
    public bool HasEnergy => _progress.MaxEnergy > 0;

    /// <summary>Gets current HP.</summary>
    public int Hp => _combat is { MaxHp: > 0 } c ? c.Hp : CombatRules.BaseMaxHp;

    /// <summary>Gets maximum HP.</summary>
    public int MaxHp => _combat is { MaxHp: > 0 } c ? c.MaxHp : CombatRules.BaseMaxHp;

    /// <summary>Gets how full the HP bar is, zero to one.</summary>
    public double HpFraction => this.MaxHp > 0 ? Math.Clamp((double)this.Hp / this.MaxHp, 0, 1) : 0;

    /// <summary>Gets whether HP is critically low.</summary>
    public bool HpLow => this.HpFraction <= 0.25;

    /// <summary>Gets the localized label of the HP bar: <c>HP 100 / 100</c>.</summary>
    public string HpLabel => this.Text("Hp_Label", Number(this.Hp), Number(this.MaxHp));

    /// <summary>Gets current MP.</summary>
    public int Mp => _combat is { MaxMp: > 0 } c ? c.Mp : CombatRules.BaseMaxMp;

    /// <summary>Gets maximum MP.</summary>
    public int MaxMp => _combat is { MaxMp: > 0 } c ? c.MaxMp : CombatRules.BaseMaxMp;

    /// <summary>Gets how full the MP bar is, zero to one.</summary>
    public double MpFraction => this.MaxMp > 0 ? Math.Clamp((double)this.Mp / this.MaxMp, 0, 1) : 0;

    /// <summary>Gets whether MP is low.</summary>
    public bool MpLow => this.MpFraction <= 0.2;

    /// <summary>Gets the localized label of the MP bar: <c>MP 50 / 50</c>.</summary>
    public string MpLabel => this.Text("Mp_Label", Number(this.Mp), Number(this.MaxMp));

    /// <summary>Gets the active status effect kind (0 = None, 1 = Regen, 2 = Poison, docs/adr/ADR-051).</summary>
    public int StatusEffect => _combat?.StatusEffect ?? 0;

    /// <summary>Gets the active speed buff kind (0 = None, 3 = Swiftness, 4 = Brotherhood in Arms; issue #146).</summary>
    public int Buff => _combat?.Buff ?? 0;

    /// <summary>Gets the badge identifier for the active effect, or else the active buff.</summary>
    public string StatusEffectBadge => this.StatusEffect switch
    {
        1 => "regen",
        2 => "poison",
        _ => this.Buff switch
        {
            3 => "swift",
            4 => "brotherhood",
            _ => string.Empty,
        },
    };

    /// <summary>Gets the energy now.</summary>
    public int Energy => _progress.Energy;

    /// <summary>Gets the largest energy.</summary>
    public int MaxEnergy => _progress.MaxEnergy;

    /// <summary>Gets how full the energy bar is, zero to one.</summary>
    public double EnergyFraction => _progress.MaxEnergy > 0 ? Math.Clamp((double)_progress.Energy / _progress.MaxEnergy, 0, 1) : 0;

    /// <summary>Gets whether the energy is low enough for the bar to warn.</summary>
    public bool EnergyLow => this.HasEnergy && this.EnergyFraction <= 0.2;

    /// <summary>Gets whether the energy bar is pulsing after food restored energy; it stops by itself after <see cref="EnergyPulseDuration"/>.</summary>
    public bool EnergyPulsing { get; private set; }

    /// <summary>Gets the localized label of the bar, for the tooltip and the screen reader: <c>Energy 268 / 270</c>.</summary>
    public string EnergyLabel => this.Text("Energy_Label", Number(_progress.Energy), Number(_progress.MaxEnergy));

    /// <summary>Gets the three gathering skills in the page's order: farming, foraging, mining.</summary>
    public IReadOnlyList<SkillDisplay> Skills =>
    [
        this.Describe("Skill_Farming", _progress.FarmingXp),
        this.Describe("Skill_Foraging", _progress.ForagingXp),
        this.Describe("Skill_Mining", _progress.MiningXp),
    ];

    /// <summary>Gets the Combat skill progression display.</summary>
    public SkillDisplay CombatSkill => this.Describe("Skill_Combat", _combat?.CombatXp ?? 0);

    /// <summary>Gets the localized Monster Bestiary &amp; Hunting Log entries (docs/adr/ADR-059).</summary>
    public IReadOnlyList<BestiaryEntryDisplay> BestiaryEntries
    {
        get
        {
            List<BestiaryEntryDisplay> list = new(MonsterCatalog.All.Count);
            foreach (MonsterDefinition def in MonsterCatalog.All)
            {
                bool isBoss = CombatRules.IsBoss(def.Kind);
                string name = _catalogue is not null ? _catalogue[$"Monster_{def.Key}_Name"].Value : def.Key;
                string location = _catalogue is not null ? _catalogue[$"Monster_{def.Key}_Location"].Value : def.Key;
                string category = this.Text(isBoss ? "Bestiary_Category_Boss" : "Bestiary_Category_Regular");
                string primaryDrop = this.ItemNameOf(def.PrimaryDropItemId);
                string rareDrop = this.ItemNameOf(def.RareDropItemId);
                int kills = _bestiary?.Current.KillsOf(def.Kind) ?? 0;
                (double fraction, string progress) = this.MilestoneOf(kills);
                string titleName = _catalogue is not null ? _catalogue[$"Monster_{def.Key}_Title"].Value : def.Key;
                bool earned = BestiaryRules.EarnsTitle(kills);
                bool equipped = _bestiary?.Current.Equipped == def.Kind;
                list.Add(new BestiaryEntryDisplay(name, category, location, def.MaxHp, def.AttackDamage, def.XpReward, primaryDrop, rareDrop, isBoss, def.Kind, kills, fraction, progress, titleName, earned, equipped));
            }

            return list;
        }
    }

    /// <summary>Gets the localized line of the bestiary progress bar for a kill count: how far to the next milestone, or that every milestone is reached.</summary>
    /// <param name="kills">How many of the species were slain.</param>
    /// <returns>The bar's fraction and its caption.</returns>
    private (double Fraction, string Caption) MilestoneOf(int kills)
    {
        int next = BestiaryRules.NextMilestone(kills);
        if (next == 0)
        {
            return (1, this.Text("Bestiary_Milestones_Done", Number(kills)));
        }

        int tier = BestiaryRules.TierOf(kills);
        int from = tier == 0 ? 0 : BestiaryRules.Milestones[tier - 1];
        double fraction = Math.Clamp((double)(kills - from) / (next - from), 0, 1);
        return (fraction, this.Text("Bestiary_Progress", Number(kills), Number(next)));
    }

    /// <summary>Gets the localized aria-label of the combat skills strip.</summary>
    public string CombatSkillsAriaLabel => this.Text("Combat_Skills_Aria");

    /// <summary>Gets the localized Whirlwind skill label.</summary>
    public string WhirlwindLabel => this.FormatSkillLabel("Combat_Skill_Whirlwind", (byte)CombatSkillKind.Whirlwind);

    /// <summary>Gets the localized Whirlwind skill tooltip.</summary>
    public string WhirlwindTip => this.Text("Combat_Skill_Whirlwind_Tip");

    /// <summary>Gets whether the Whirlwind skill button should be disabled.</summary>
    public bool WhirlwindDisabled => !this.CanCastSkill((byte)CombatSkillKind.Whirlwind);

    /// <summary>Gets the localized Fireball skill label.</summary>
    public string FireballLabel => this.FormatSkillLabel("Combat_Skill_Fireball", (byte)CombatSkillKind.Fireball);

    /// <summary>Gets the localized Fireball skill tooltip.</summary>
    public string FireballTip => this.Text("Combat_Skill_Fireball_Tip");

    /// <summary>Gets whether the Fireball skill button should be disabled.</summary>
    public bool FireballDisabled => !this.CanCastSkill((byte)CombatSkillKind.Fireball);

    /// <summary>Gets the localized Divine Heal skill label.</summary>
    public string DivineHealLabel => this.FormatSkillLabel("Combat_Skill_DivineHeal", (byte)CombatSkillKind.DivineHeal);

    /// <summary>Gets the localized Divine Heal skill tooltip.</summary>
    public string DivineHealTip => this.Text("Combat_Skill_DivineHeal_Tip");

    /// <summary>Gets whether the Divine Heal skill button should be disabled.</summary>
    public bool DivineHealDisabled => !this.CanCastSkill((byte)CombatSkillKind.DivineHeal);

    /// <summary>Gets the remaining cooldown in seconds for a combat skill (0 when ready) (docs/adr/ADR-056).</summary>
    /// <param name="skillId">1 Whirlwind, 2 Fireball, 3 DivineHeal.</param>
    /// <returns>Seconds remaining until ready.</returns>
    public float GetSkillCooldown(byte skillId) => _combat?.GetSkillCooldownRemaining(skillId) ?? 0f;

    /// <summary>Gets whether a combat skill is ready to cast (off cooldown and sufficient MP).</summary>
    /// <param name="skillId">1 Whirlwind, 2 Fireball, 3 DivineHeal.</param>
    /// <returns><see langword="true"/> when the skill can be cast.</returns>
    public bool CanCastSkill(byte skillId)
    {
        if (this.GetSkillCooldown(skillId) > 0f)
        {
            return false;
        }

        if (!SkillCatalog.TryGet((CombatSkillKind)skillId, out SkillDefinition def))
        {
            return false;
        }

        return this.Mp >= def.MpCost;
    }

    /// <summary>Gets the localized combo streak badge text.</summary>
    public string ComboBadgeText => this.Text("Combat_Combo_Badge", Number(this.CurrentCombo), Number(this.ComboBonusPercent));

    /// <summary>Gets the localized level-up badge text.</summary>
    public string LevelUpBadgeText => this.Text("Combat_LevelUp_Badge", Number(this.ActiveLevelUpValue));

    /// <summary>Casts a combat skill (1 = Whirlwind, 2 = Fireball, 3 = DivineHeal) (docs/adr/ADR-056).</summary>
    /// <param name="skillId">Skill kind.</param>
    public void CastSkill(byte skillId) => _combat?.CastSkill(skillId);

    /// <summary>Gets the localized name of a skill by its wire number.</summary>
    /// <param name="skill">1 farming, 2 foraging, 3 mining, 4 combat, 5 fishing.</param>
    /// <returns>The name, or an empty string for an unknown skill.</returns>
    public string SkillName(byte skill) => (SkillKind)skill switch
    {
        SkillKind.Farming => this.Text("Skill_Farming"),
        SkillKind.Foraging => this.Text("Skill_Foraging"),
        SkillKind.Mining => this.Text("Skill_Mining"),
        SkillKind.Combat => this.Text("Skill_Combat"),
        SkillKind.Fishing => this.Text("Skill_Fishing"),
        _ => string.Empty,
    };

    /// <summary>Gets the current combat combo streak (docs/adr/ADR-060).</summary>
    public int CurrentCombo => _combat?.CurrentCombo ?? 0;

    /// <summary>Gets the combo damage bonus percentage (e.g. 20 for +20%).</summary>
    public int ComboBonusPercent => (int)(CombatRules.ComboBonusPercent(this.CurrentCombo) * 100);

    /// <summary>Gets whether the celebratory level-up radiance banner is currently active (docs/adr/ADR-062).</summary>
    public bool IsLevelUpActive => _combat?.IsLevelUpActive ?? false;

    /// <summary>Gets the level currently being celebrated.</summary>
    public int ActiveLevelUpValue => _combat?.ActiveLevelUpValue ?? 0;

    /// <inheritdoc/>
    public void Dispose()
    {
        _progress.Changed -= this.Raise;
        _progress.EatCompleted -= this.OnEatCompleted;
        if (_bestiary is not null)
        {
            _bestiary.Changed -= this.Raise;
        }

        if (_combat is not null)
        {
            _combat.Changed -= this.Raise;
            _combat.ComboChanged -= this.Raise;
            _combat.KnockedOut -= this.Raise;
            _combat.LevelledUp -= this.OnCombatLevelUp;
        }
    }

    private void OnEatCompleted(EatStatus status)
    {
        if (status != EatStatus.Ok)
        {
            return;
        }

        int version = ++_pulseVersion;
        this.EnergyPulsing = true;
        this.Raise();
        _ = this.EndPulseAsync(version);
    }

    private async Task EndPulseAsync(int version)
    {
        await Task.Delay(EnergyPulseDuration).ConfigureAwait(false);
        if (version == _pulseVersion)
        {
            this.EnergyPulsing = false;
            this.Raise();
        }
    }

    private void OnCombatLevelUp(int level)
    {
        _ = level;
        this.Raise();
    }

    private SkillDisplay Describe(string nameKey, int xp)
    {
        int level = SkillRules.LevelOf(xp);
        if (level >= SkillRules.MaxLevel)
        {
            return new SkillDisplay(this.Text(nameKey), level, xp, 1, this.Text("Skill_MaxLevel"));
        }

        int from = SkillRules.XpForLevel(level);
        int to = SkillRules.XpForLevel(level + 1);
        double fraction = Math.Clamp((double)(xp - from) / (to - from), 0, 1);
        return new SkillDisplay(this.Text(nameKey), level, xp, fraction, string.Format(CultureInfo.CurrentCulture, "{0} / {1}", Number(xp - from), Number(to - from)));
    }

    private string ItemNameOf(int itemId)
    {
        if (itemId <= 0 || !ItemCatalog.TryGet(itemId, out ItemDefinition? item))
        {
            return string.Empty;
        }

        return _catalogue is not null ? _catalogue[$"Item_{item.Key}_Name"].Value : item.Key;
    }

    private string FormatSkillLabel(string key, byte skillId)
    {
        string label = this.Text(key);
        float cd = this.GetSkillCooldown(skillId);
        if (cd > 0f)
        {
            int seconds = (int)Math.Ceiling(cd);
            return string.Create(CultureInfo.InvariantCulture, $"{label} ({seconds}s)");
        }

        return label;
    }
}
