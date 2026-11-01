// Copyright (c) 2026 PPN Systems. All rights reserved.

#if NOSUM_DEV_TOOLS
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Nosum.Wasm.Game;

/// <summary>
/// The QA entry point for the UI capture harness (docs/adr/ADR-079). Compiled only into a build made with <c>-p:NosumDevTools=true</c>; a production
/// build has no such code. The script it runs does nothing unless the page URL carries <c>?qa=1</c>; then <c>window.nosumQa</c> offers the tester
/// calls of <see cref="DevTools"/> (grant, clock, teleport, season, the Spring reset, the teleport next to a monster, level-up banner, fishing
/// bite, the clock freeze and the sunny-day skip). Nothing is drawn; the server still decides.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class QaEntry
{
    private const string Script = """
        (async () => {
          if (new URLSearchParams(globalThis.location.search).get("qa") !== "1") {
            return;
          }

          try {
            const runtime = await globalThis.getDotnetRuntime(0);
            const exports = await runtime.getAssemblyExports("Nosum.Wasm.dll");
            const dev = exports.Nosum.Wasm.Game.DevTools;
            globalThis.nosumQa = Object.freeze({
              grant: (itemId, quantity, coins) => dev.Grant(itemId, quantity, coins),
              advanceClock: days => dev.AdvanceClock(days),
              teleport: (mapId, tileX, tileY) => dev.Teleport(mapId, tileX, tileY),
              season: seasons => dev.Season(seasons),
              levelUp: (skill, level) => dev.LevelUp(skill, level),
              fishingBite: () => dev.FishingBite(),
              springReset: () => dev.ToSpring(),
              teleportToMonster: mapId => dev.TeleportToMonster(mapId),
              freezeClock: frozen => dev.FreezeClock(frozen === true),
              sunnyDay: () => dev.SunnyDay(),
            });
          } catch (error) {
            // The hook is a tester aid: a failure here must not take the game down with it.
            console.error("nosumQa: the QA hook did not install", error);
          }
        })();
        """;

    /// <summary>Runs the QA script in the page; it exposes <c>window.nosumQa</c> only when the URL asks for it.</summary>
    public static void Install() => Eval(Script);

    [JSImport("globalThis.eval")]
    private static partial void Eval(string script);
}
#endif
