// Copyright (c) 2026 PPN Systems. All rights reserved.

#if NOSUM_DEV_TOOLS
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
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

    /// <summary>Connects the tools to the server channel and the fishing banner.</summary>
    /// <param name="channel">The dev channel.</param>
    /// <param name="fishing">The fishing banner, whose waiting line the bite tool makes bite.</param>
    public static void Use(DevChannel channel, FishingViewModel fishing)
    {
        s_channel = channel;
        s_fishing = fishing;
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

    /// <summary>Passes season starts on the server clock.</summary>
    /// <param name="seasons">Season starts to pass, 1 to 4.</param>
    [JSExport]
    internal static void Season(int seasons) => s_channel?.Season(seasons);

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
