// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Wasm.Settings;

/// <summary>The calls into <c>nosum-settings.js</c>: local storage and the page switches. Used when a setting changes, never from the frame.</summary>
[SupportedOSPlatform("browser")]
internal static partial class BrowserSettings
{
    private const string ModuleName = "nosum-settings";
    private const string ModuleUrl = "/_content/Nosum.Wasm.Controls/nosum-settings.js";

    /// <summary>Loads the script; call once at start-up, before any other member.</summary>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>A task that completes when the script is ready.</returns>
    public static Task ImportAsync(CancellationToken cancellationToken = default) => JSHost.ImportAsync(ModuleName, ModuleUrl, cancellationToken);

    [JSImport("readSettings", ModuleName)]
    public static partial string? Read();

    [JSImport("writeSettings", ModuleName)]
    public static partial void Write(string text);

    [JSImport("applyDisplay", ModuleName)]
    public static partial void ApplyDisplay(bool highContrast, bool reduceMotion);

    [JSImport("applyScale", ModuleName)]
    public static partial void ApplyScale(int scale);
}
