// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Nosum.Contracts.Combat;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.GroundItem;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Audio;

namespace Nosum.Wasm.Game;

/// <summary>Start-up wiring of the sound hooks (docs/adr/ADR-039 D3): each channel event that deserves a sound asks the <see cref="GameAudio"/> queue for it. Nothing here touches the browser.</summary>
[SupportedOSPlatform("browser")]
internal static class AudioWiring
{
    /// <summary>Hands the audio director to the frame loop and connects the hooks.</summary>
    /// <param name="audio">The audio director.</param>
    /// <param name="services">The built service provider.</param>
    public static void Connect(GameAudio audio, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(services);
        GameLoop.UseAudio(audio);

        // What the server answered to a use of an item: the harvest, the planting, the refill.
        services.GetRequiredService<FarmChannel>().UseCompleted += (_, action) => audio.Play(AudioCatalog.OutcomeSound(action));

        services.GetRequiredService<GroundItemChannel>().PickedUp += audio.PlayPickup;

        // The balance moving after the first known value: coins earned or spent.
        long coins = -1;
        WalletChannel wallet = services.GetRequiredService<WalletChannel>();
        wallet.Changed += () =>
        {
            long now = wallet.Coins;
            if (coins >= 0 && now != coins)
            {
                audio.Play(SoundIds.Coin);
            }

            coins = now;
        };

        // A panel opening or closing, by key or by button; moving from one panel to another counts as opening.
        ToolbarViewModel toolbar = services.GetRequiredService<ToolbarViewModel>();
        PanelKind open = toolbar.Active;
        toolbar.Changed += () =>
        {
            PanelKind now = toolbar.Active;
            if (now != open)
            {
                audio.Play(now == PanelKind.None ? SoundIds.UiClose : SoundIds.UiOpen);
                open = now;
            }
        };

        // Combat sounds (docs/adr/ADR-053): strikes, monster damage grunts, defeat bursts, and potion drinking.
        CombatChannel combat = services.GetRequiredService<CombatChannel>();
        combat.AttackCompleted += attack =>
        {
            if (attack.Status == CombatStatus.Ok)
            {
                audio.Play(attack.MonsterHp == 0 ? SoundIds.MonsterDefeat : SoundIds.MonsterHit);
            }
        };
        combat.KnockedOut += () => audio.Play(SoundIds.MonsterHit);

        combat.PotionCompleted += status =>
        {
            if (status == CombatStatus.Ok)
            {
                audio.Play(SoundIds.PotionDrink);
            }
        };

        combat.LevelledUp += _ => audio.Play(SoundIds.LevelUp);
        services.GetRequiredService<ProgressChannel>().LevelledUp += (_, level) => combat.TriggerLevelUp(level);
    }
}
