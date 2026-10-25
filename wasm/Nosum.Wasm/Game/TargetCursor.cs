// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Game;

/// <summary>
/// The 16 x 16 cursor drawn on the tile the action key will work on: four light corner brackets with a
/// dark edge, so it shows on grass and on soil. Embedded because it is Nosum's own, tiny, and not part of the placeholder content.
/// </summary>
internal static class TargetCursor
{
    /// <summary>The PNG as a data URL, loaded like any texture.</summary>
    public const string DataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAWElEQVR42mNkYGA4wYAKLBjwAxT1jAwMDCf+//+vzMDAwMDIyCjKQAT4////a6j6u0xwk4jUjK6WiVTN5Fg4CkYGOEGJXibktE0KgOlhQhcgRTM8N1KSnQFJ/h30aSaPoQAAAABJRU5ErkJggg==";
}
