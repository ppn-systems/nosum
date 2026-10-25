// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Runtime.Versioning;
using Nosum.Wasm.ViewModels.Settings;

namespace Nosum.Wasm.Settings;

/// <summary>Keeps the settings in the browser's local storage (blocked storage reads as empty and drops writes, inside the script).</summary>
[SupportedOSPlatform("browser")]
internal sealed class LocalSettingsStore : ISettingsStore
{
    /// <inheritdoc/>
    public string? Read() => BrowserSettings.Read();

    /// <inheritdoc/>
    public void Write(string text) => BrowserSettings.Write(text);
}
