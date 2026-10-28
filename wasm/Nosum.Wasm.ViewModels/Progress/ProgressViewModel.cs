// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Skills;
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

/// <summary>
/// View model of the HUD vitals bars (HP, MP, Energy) beside the hotbar and of the Skills page (docs/adr/ADR-023 D6, docs/adr/ADR-045 D2).
/// </summary>
public sealed class ProgressViewModel : PanelViewModel, IDisposable
{
    private readonly ProgressChannel _progress;
    private readonly CombatChannel? _combat;

    /// <summary>Initializes the view model.</summary>
    /// <param name="progress">The energy and experience mirror.</param>
    /// <param name="text">UI text.</param>
    /// <param name="combat">Optional combat vitals and Combat XP mirror.</param>
    public ProgressViewModel(ProgressChannel progress, IStringLocalizer<Language> text, CombatChannel? combat = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(progress);

        _progress = progress;
        _combat = combat;
        progress.Changed += this.Raise;
        if (combat is not null)
        {
            combat.Changed += this.Raise;
        }
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

    /// <summary>Gets the active status effect kind (0 = None, 1 = Regen, 2 = Poison, 3 = Swiftness, docs/adr/ADR-051).</summary>
    public int StatusEffect => _combat?.StatusEffect ?? 0;

    /// <summary>Gets the badge identifier for active status effects.</summary>
    public string StatusEffectBadge => this.StatusEffect switch
    {
        1 => "regen",
        2 => "poison",
        3 => "swift",
        _ => string.Empty,
    };

    /// <summary>Gets the energy now.</summary>
    public int Energy => _progress.Energy;

    /// <summary>Gets the largest energy.</summary>
    public int MaxEnergy => _progress.MaxEnergy;

    /// <summary>Gets how full the energy bar is, zero to one.</summary>
    public double EnergyFraction => _progress.MaxEnergy > 0 ? Math.Clamp((double)_progress.Energy / _progress.MaxEnergy, 0, 1) : 0;

    /// <summary>Gets whether the energy is low enough for the bar to warn.</summary>
    public bool EnergyLow => this.HasEnergy && this.EnergyFraction <= 0.2;

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

    /// <summary>Gets the localized name of a skill by its wire number.</summary>
    /// <param name="skill">1 farming, 2 foraging, 3 mining, 4 combat.</param>
    /// <returns>The name, or an empty string for an unknown skill.</returns>
    public string SkillName(byte skill) => (SkillKind)skill switch
    {
        SkillKind.Farming => this.Text("Skill_Farming"),
        SkillKind.Foraging => this.Text("Skill_Foraging"),
        SkillKind.Mining => this.Text("Skill_Mining"),
        SkillKind.Combat => this.Text("Skill_Combat"),
        _ => string.Empty,
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        _progress.Changed -= this.Raise;
        if (_combat is not null)
        {
            _combat.Changed -= this.Raise;
        }
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
}
