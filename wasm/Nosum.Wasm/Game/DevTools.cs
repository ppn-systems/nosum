// Copyright (c) 2026 PPN Systems. All rights reserved.

#if NOSUM_DEV_TOOLS
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Nosum.Wasm.Socket.Dev;

namespace Nosum.Wasm.Game;

/// <summary>
/// Tester tools, compiled only into a build made with <c>-p:NosumDevTools=true</c>. A tester calls
/// <c>(await getDotnetRuntime(0).getAssemblyExports("Nosum.Wasm.dll")).Nosum.Wasm.Game.DevTools.Grant(300, 5, 0)</c> from the browser console to
/// get 5 oak chairs; the server grants it only when it runs in Development.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class DevTools
{
    private static DevChannel? s_channel;

    /// <summary>Connects the tools to the server channel.</summary>
    /// <param name="channel">The dev channel.</param>
    public static void Use(DevChannel channel) => s_channel = channel;

    /// <summary>Asks a Development server for items and/or coins.</summary>
    /// <param name="itemId">The item id, or zero for coins only.</param>
    /// <param name="quantity">How many items.</param>
    /// <param name="coins">Coins to add.</param>
    [JSExport]
    internal static void Grant(int itemId, int quantity, int coins) => s_channel?.Grant(itemId, quantity, coins);
}
#endif
