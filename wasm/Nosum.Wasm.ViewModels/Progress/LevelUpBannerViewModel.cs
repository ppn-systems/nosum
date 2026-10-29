// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Gameplay.Skills;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Progress;

/// <summary>
/// The fanfare banner across the top of the screen that names the skill and the level reached (docs/adr/ADR-062).
/// It follows the same two sources as the message line: gathering skills from <see cref="ProgressChannel"/>, and Combat from <see cref="CombatChannel"/>.
/// </summary>
public sealed class LevelUpBannerViewModel : IObservableViewModel, IDisposable
{
    /// <summary>How long the banner stays; it matches the world beacon's celebration.</summary>
    public static readonly TimeSpan BannerDuration = TimeSpan.FromSeconds(3.8);

    private readonly ProgressChannel _progress;
    private readonly CombatChannel _combat;
    private readonly IStringLocalizer<Language> _text;
    private int _version;

    /// <summary>Initializes the view model and starts following the level-up sources.</summary>
    /// <param name="progress">The energy and skills channel (gathering skills).</param>
    /// <param name="combat">The combat channel (Combat XP and the level-up celebration).</param>
    /// <param name="text">UI text.</param>
    public LevelUpBannerViewModel(ProgressChannel progress, CombatChannel combat, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(text);

        _progress = progress;
        _combat = combat;
        _text = text;
        progress.LevelledUp += this.OnGatheringLevelUp;
        combat.LevelledUp += this.OnCombatLevelUp;
    }

    /// <summary>Raised when <see cref="Title"/> or <see cref="Message"/> changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Gets the banner heading, <c>LEVEL UP!</c>, or <see langword="null"/> when no banner is shown.</summary>
    public string? Title { get; private set; }

    /// <summary>Gets the banner line naming the skill and the level, or <see langword="null"/> when no banner is shown.</summary>
    public string? Message { get; private set; }

    /// <inheritdoc/>
    public void Dispose()
    {
        _progress.LevelledUp -= this.OnGatheringLevelUp;
        _combat.LevelledUp -= this.OnCombatLevelUp;
    }

    private void OnGatheringLevelUp(byte skill, int level) => this.Show(skill, level);

    private void OnCombatLevelUp(int level)
    {
        // The combat channel also raises this for gathering level-ups, which the progress channel already reported.
        if (!_combat.IsCombatSkillLevelUp)
        {
            return;
        }

        this.Show((byte)SkillKind.Combat, level);
    }

    private void Show(byte skill, int level)
    {
        int version = ++_version;
        this.Title = _text["Combat_Label_LevelUp"].Value;
        this.Message = string.Format(CultureInfo.CurrentCulture, _text["Skill_LevelUp"].Value, _text["Skill_" + (SkillKind)skill].Value, level);
        this.Changed?.Invoke();
        _ = this.ClearLaterAsync(version);
    }

    private async Task ClearLaterAsync(int version)
    {
        await Task.Delay(BannerDuration).ConfigureAwait(false);
        if (version == _version)
        {
            this.Title = null;
            this.Message = null;
            this.Changed?.Invoke();
        }
    }
}
