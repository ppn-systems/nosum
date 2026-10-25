// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Resources;

/// <summary>
/// Marker type for <c>IStringLocalizer&lt;Catalogue&gt;</c>: the localization catalogue of game data in
/// <c>Catalogue.resx</c> (Vietnamese, neutral) and <c>Catalogue.en.resx</c>. Names and descriptions are
/// looked up by the stable keys of the item and quest catalogues (docs/adr/ADR-010 D4):
/// <c>Item_{key}_Name</c>, <c>Item_{key}_Description</c>, <c>Quest_{key}_Title</c>, <c>Quest_{key}_Hint</c>
/// and <c>Quest_{key}_Objective_{index}</c>. The server never needs this text, so it lives with the client until it does.
/// </summary>
public sealed class Catalogue
{
}
