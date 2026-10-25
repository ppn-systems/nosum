// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Settings;

/// <summary>Where the settings text lives on this browser (the web implementation uses local storage).</summary>
public interface ISettingsStore
{
    /// <summary>Reads the stored text.</summary>
    /// <returns>The text, or <see langword="null"/> when nothing is stored or storage is blocked.</returns>
    string? Read();

    /// <summary>Stores the text; does nothing, without an error, when storage is blocked (the settings then last for this visit only).</summary>
    /// <param name="text">The text.</param>
    void Write(string text);
}
