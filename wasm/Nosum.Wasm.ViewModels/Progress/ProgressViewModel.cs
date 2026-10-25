// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Skills;
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
/// View model of the energy bar beside the hotbar and of the Skills page (docs/adr/ADR-023 D6). It shows what the server pushed; levels are derived
/// from the experience with the same table the server uses (<see cref="SkillRules"/>), so nothing is stored twice.
/// </summary>
public sealed class ProgressViewModel : PanelViewModel, IDisposable
{
    private readonly ProgressChannel _progress;

    /// <summary>Initializes the view model.</summary>
    /// <param name="progress">The energy and experience mirror.</param>
    /// <param name="text">UI text.</param>
    public ProgressViewModel(ProgressChannel progress, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(progress);

        _progress = progress;
        progress.Changed += this.Raise;
    }

    /// <summary>Gets whether the server has told the player their energy yet; the bar is hidden before.</summary>
    public bool HasEnergy => _progress.MaxEnergy > 0;

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

    /// <summary>Gets the three skills in the page's order: farming, foraging, mining.</summary>
    public IReadOnlyList<SkillDisplay> Skills =>
    [
        this.Describe("Skill_Farming", _progress.FarmingXp),
        this.Describe("Skill_Foraging", _progress.ForagingXp),
        this.Describe("Skill_Mining", _progress.MiningXp),
    ];

    /// <summary>Gets the localized name of a skill by its wire number.</summary>
    /// <param name="skill">1 farming, 2 foraging, 3 mining.</param>
    /// <returns>The name, or an empty string for an unknown skill.</returns>
    public string SkillName(byte skill) => (SkillKind)skill switch
    {
        SkillKind.Farming => this.Text("Skill_Farming"),
        SkillKind.Foraging => this.Text("Skill_Foraging"),
        SkillKind.Mining => this.Text("Skill_Mining"),
        _ => string.Empty,
    };

    /// <inheritdoc/>
    public void Dispose() => _progress.Changed -= this.Raise;

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
