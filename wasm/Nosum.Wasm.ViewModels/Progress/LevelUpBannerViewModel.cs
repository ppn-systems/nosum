// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Skills;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Progress;

/// <summary>
/// The fanfare banner across the top of the screen that names the skill and the level reached (docs/adr/ADR-062).
/// Every level-up, Combat included, arrives as the server's <c>LevelUp</c> packet on <see cref="ProgressChannel"/>, the one source the banner follows.
/// The combat channel's own level-up event is not followed: it fires for the same level after the vitals update, and would show it twice.
/// </summary>
public sealed class LevelUpBannerViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long the banner stays; it matches the world beacon's celebration.</summary>
    public static readonly TimeSpan BannerDuration = TimeSpan.FromSeconds(3.8);

    private readonly ProgressChannel _progress;
    private readonly IStringLocalizer<Language> _text;
    private int _version;

    /// <summary>Initializes the view model and starts following the level-up sources.</summary>
    /// <param name="progress">The energy and skills channel (gathering skills).</param>
    /// <param name="text">UI text.</param>
    public LevelUpBannerViewModel(ProgressChannel progress, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(text);

        _progress = progress;
        _text = text;
        progress.LevelledUp += this.OnGatheringLevelUp;
    }

    /// <summary>Raised when <see cref="Title"/> or <see cref="Message"/> changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets the banner heading, <c>LEVEL UP!</c>, or <see langword="null"/> when no banner is shown.</summary>
    public string? Title { get; private set; }

    /// <summary>Gets the banner line naming the skill and the level, or <see langword="null"/> when no banner is shown.</summary>
    public string? Message { get; private set; }

    /// <summary>
    /// Gets the perk line for a gathering level that unlocks one (docs/adr/ADR-089 D3): the energy the skill's tools now save per use. It is
    /// <see langword="null"/> for every other level and skill, and when no banner is shown.
    /// </summary>
    public string? Perk { get; private set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        _progress.LevelledUp -= this.OnGatheringLevelUp;
    }

    private void OnGatheringLevelUp(byte skill, int level) => this.Show(skill, level);

    private void Show(byte skill, int level)
    {
        int version = ++_version;
        string skillName = _text["Skill_" + (SkillKind)skill].Value;
        this.Title = _text["Combat_Label_LevelUp"].Value;
        this.Message = string.Format(CultureInfo.CurrentCulture, _text["Skill_LevelUp"].Value, skillName, level);
        this.Perk = PerkAt((SkillKind)skill, level) is { } discount
            ? string.Format(CultureInfo.CurrentCulture, _text["Skill_Perk"].Value, skillName, discount)
            : null;
        this.Changed?.Invoke();
        _ = this.ClearLaterAsync(version);
    }

    /// <summary>
    /// Returns the energy discount a level unlocks for a gathering skill's tools, or <see langword="null"/> when the level unlocks none
    /// (docs/adr/ADR-089 D3). Combat and fishing tools cost no gathering energy, so they have no perk.
    /// </summary>
    /// <param name="skill">The skill.</param>
    /// <param name="level">The level reached.</param>
    /// <returns>The discount the level now gives, or <see langword="null"/>.</returns>
    public static int? PerkAt(SkillKind skill, int level)
    {
        bool gathering = skill is SkillKind.Farming or SkillKind.Foraging or SkillKind.Mining;
        return gathering && level > 0 && level % EnergyRules.LevelsPerDiscount == 0 ? level / EnergyRules.LevelsPerDiscount : null;
    }

    private async Task ClearLaterAsync(int version)
    {
        await Task.Delay(BannerDuration).ConfigureAwait(false);
        if (version == _version)
        {
            this.Title = null;
            this.Message = null;
            this.Perk = null;
            this.Changed?.Invoke();
        }
    }
}
