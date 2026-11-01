// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Nosum.Contracts.Building;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Player;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Crafting;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Friends;
using Nosum.Wasm.Socket.Furnace;
using Nosum.Wasm.Socket.GroundItem;
using Nosum.Wasm.Socket.Mail;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.Socket.Visit;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Audio;
using Nosum.Wasm.ViewModels.Calendar;

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
        services.GetRequiredService<FarmChannel>().UseCompleted += (_, action, _) => audio.Play(AudioCatalog.OutcomeSound(action));

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
        combat.ComboFinished += () => audio.Play(SoundIds.ComboFinisher);
        services.GetRequiredService<ProgressChannel>().LevelledUp += (_, level) => combat.TriggerLevelUp(level);

        // Crafting and the furnace: a craft the server accepted, and a furnace batch that reached its ready time on this client.
        services.GetRequiredService<CraftingChannel>().Completed += status =>
        {
            if (status == CraftStatus.Ok)
            {
                audio.Play(SoundIds.Craft);
            }
        };
        services.GetRequiredService<FurnaceChannel>().Finished += _ => audio.Play(SoundIds.Craft);

        // Eating food that restored energy: the crunch here, the sparkle over the farmer (EatFeedback) and the energy bar's pulse (ProgressViewModel).
        services.GetRequiredService<ProgressChannel>().EatCompleted += status =>
        {
            if (status == EatStatus.Ok)
            {
                audio.Play(SoundIds.Eat);
                EatFeedback.Request();
            }
        };

        // Animal care: an egg or milk collected, an animal petted.
        services.GetRequiredService<BuildingChannel>().Completed += info =>
        {
            if (info.Status != BuildingStatus.Ok)
            {
                return;
            }

            int sound = info.Action switch
            {
                BuildingAction.Collect => SoundIds.Pickup,
                BuildingAction.Pet => SoundIds.Plant,
                _ => 0,
            };
            if (sound != 0)
            {
                audio.Play(sound);
            }
        };

        // Mail: the unread count rising means a letter arrived; reading or deleting one lowers it and stays quiet.
        MailChannel mail = services.GetRequiredService<MailChannel>();
        int unread = -1;
        mail.UnreadChanged += () =>
        {
            int now = mail.Unread;
            if (unread >= 0 && now > unread)
            {
                audio.Play(SoundIds.MailChime);
            }

            unread = now;
        };

        // Friends: a request answered (the server's ok to an accept or a decline).
        services.GetRequiredService<FriendsChannel>().Completed += status =>
        {
            if (status == FriendStatus.Ok)
            {
                audio.Play(SoundIds.MailChime);
            }
        };

        // A visitor arriving at the farm: the visitor list grew since it was last read.
        VisitChannel visits = services.GetRequiredService<VisitChannel>();
        int visitors = -1;
        visits.Changed += () =>
        {
            int now = visits.Current.Visitors.Count;
            if (visitors >= 0 && now > visitors)
            {
                audio.Play(SoundIds.Doorbell);
            }

            visitors = now;
        };

        // The season turns: a soft fanfare under the banner (the banner itself is the SeasonBannerViewModel's).
        services.GetRequiredService<CalendarViewModel>().SeasonChanged += _ => audio.Play(SoundIds.LevelUp, 50);
    }
}
