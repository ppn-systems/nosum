// Copyright (c) 2026 PPN Systems. All rights reserved.

#if NOSUM_DEV_TOOLS
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Dev;
using Nosum.Wasm.ViewModels.Fishing;

namespace Nosum.Wasm.Game;

/// <summary>
/// Tester tools, compiled only into a build made with <c>-p:NosumDevTools=true</c>. A tester calls
/// <c>(await getDotnetRuntime(0).getAssemblyExports("Nosum.Wasm.dll")).Nosum.Wasm.Game.DevTools.Grant(300, 5, 0)</c> from the browser console to
/// get 5 oak chairs (<c>AdvanceClock(days)</c> skips game days); the server grants it only when it runs in Development. The UI capture harness
/// reaches the same calls through <c>window.nosumQa</c> on a page opened with <c>?qa=1</c> (<see cref="QaEntry"/>, docs/adr/ADR-079).
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class DevTools
{
    private static DevChannel? s_channel;
    private static FishingViewModel? s_fishing;
    private static CombatChannel? s_combat;

    /// <summary>Connects the tools to the server channel and the fishing banner.</summary>
    /// <param name="channel">The dev channel.</param>
    /// <param name="fishing">The fishing banner, whose waiting line the bite tool makes bite.</param>
    /// <param name="combat">The combat channel, whose monsters the teleport next to a monster reads.</param>
    public static void Use(DevChannel channel, FishingViewModel fishing, CombatChannel combat)
    {
        s_channel = channel;
        s_fishing = fishing;
        s_combat = combat;
    }

    /// <summary>Asks a Development server for items and/or coins.</summary>
    /// <param name="itemId">The item id, or zero for coins only.</param>
    /// <param name="quantity">How many items.</param>
    /// <param name="coins">Coins to add.</param>
    [JSExport]
    internal static void Grant(int itemId, int quantity, int coins) => s_channel?.Grant(itemId, quantity, coins);

    /// <summary>Asks a Development server to skip whole game days (crop growth, day rollover, rain, regrowth).</summary>
    /// <param name="days">Game days to skip, 1 to 60.</param>
    [JSExport]
    internal static void AdvanceClock(int days) => s_channel?.AdvanceClock(days);

    /// <summary>Moves the player to a tile of a map (the server refuses an unknown map or a tile off it).</summary>
    /// <param name="mapId">The map id, for example <c>town</c>.</param>
    /// <param name="tileX">The tile column.</param>
    /// <param name="tileY">The tile row.</param>
    [JSExport]
    internal static void Teleport(string mapId, int tileX, int tileY) => s_channel?.Teleport(mapId, tileX, tileY);

    /// <summary>Asks a Development server for a new registered test friend of the player (docs/adr/ADR-105). Resolves to the nickname and the
    /// session token separated by a tab, or to an empty string when the server made none.</summary>
    [JSExport]
    internal static async Task<string> Befriend()
    {
        if (s_channel is null)
        {
            return string.Empty;
        }

        TestFriend? friend = await s_channel.BefriendAsync().ConfigureAwait(false);
        return friend is null ? string.Empty : friend.Nickname + "\t" + friend.SessionToken;
    }

    /// <summary>Bytes the frame thread has allocated so far. The frame benchmark (bench/wasm-perf/frame.cjs) reads the difference across a run.</summary>
    [JSExport]
    internal static double AllocatedBytes() => GC.GetAllocatedBytesForCurrentThread();

    /// <summary>Managed bytes the frame's own work allocated since the page started (<see cref="FrameAllocations"/>).</summary>
    [JSExport]
    internal static double FrameBodyBytes() => FrameAllocations.Bytes;

    /// <summary>Frames counted by <see cref="FrameBodyBytes"/>.</summary>
    [JSExport]
    internal static double FrameBodyFrames() => FrameAllocations.Frames;

    /// <summary>Passes season starts on the server clock.</summary>
    /// <param name="seasons">Season starts to pass, 1 to 4.</param>
    [JSExport]
    internal static void Season(int seasons) => s_channel?.Season(seasons);

    /// <summary>Passes season starts until the calendar reads Spring: the start of a QA run (docs/adr/ADR-079 D3). Zero passes on the server.</summary>
    [JSExport]
    internal static void ToSpring() => s_channel?.Season(0);

    /// <summary>Moves the player onto the tile of a living monster of the current map; the server places the player next to it. Call it after the
    /// map has loaded its monsters (the Woods, the Forest, the Mountain or the Backwoods), for the combat keys.</summary>
    /// <param name="mapId">The map id the player is on, for example <c>woods</c>.</param>
    /// <param name="dx">Tiles from the monster's tile, to the right (negative to the left). Round 5 uses a tile beside it to see the telegraph.</param>
    /// <param name="dy">Tiles from the monster's tile, down (negative up). A farmer one tile below a monster is drawn over it (docs/adr/ADR-097 D4).</param>
    [JSExport]
    internal static void TeleportToMonster(string mapId, int dx = 0, int dy = 0)
    {
        if (s_channel is not null && s_combat is not null && s_combat.TryGetLivingMonsterTile(out int x, out int y))
        {
            s_channel.Teleport(mapId, x + dx, y + dy);
        }
    }

    /// <summary>Shows a level-up banner for a skill without changing it.</summary>
    /// <param name="skill">A <c>SkillKind</c> value: 1 farming, 2 foraging, 3 mining, 4 combat, 5 fishing.</param>
    /// <param name="level">The level to show, 1 to 10.</param>
    [JSExport]
    internal static void LevelUp(int skill, int level) => s_channel?.LevelUp(skill, level);

    /// <summary>Makes the line that is waiting for a bite bite now: the server is asked first, then the bite cue shows.</summary>
    [JSExport]
    internal static void FishingBite()
    {
        uint castId = s_fishing?.OpenCastId ?? 0;
        if (castId == 0 || s_channel is null || s_fishing is null)
        {
            return;
        }

        s_channel.FishingBite(castId);
        s_fishing.BiteNow();
    }
}
#endif
