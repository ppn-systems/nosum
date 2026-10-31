// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Nosum.Wasm.ViewModels.Npc;

/// <summary>
/// The sixteen lines each merchant can say (docs/adr/ADR-080 D2), in <c>Language.resx</c> as <c>Npc_{Id}_Line_{nn}</c>, with the moments each one fits.
/// <see cref="NpcGreeting"/> chooses among the lines whose moment is now; a line with no tag fits any moment, so every merchant always has something to say.
/// The text is not here: a line is only its number and its tags, and the key is built from the merchant's id.
/// </summary>
public static class NpcLineCatalog
{
    private static readonly Dictionary<string, NpcLine[]> s_pools = new(StringComparer.Ordinal)
    {
        ["seed_keeper"] =
        [
            new(1), new(2), new(3), new(4),
            new(5, NpcLineTags.Spring), new(6, NpcLineTags.Summer), new(7, NpcLineTags.Fall), new(8, NpcLineTags.Winter),
            new(9, NpcLineTags.Wet), new(10, NpcLineTags.Snow), new(11, NpcLineTags.Morning), new(12, NpcLineTags.Evening),
            new(13, NpcLineTags.Newcomer), new(14, NpcLineTags.Harvested), new(15, NpcLineTags.Spring | NpcLineTags.Morning), new(16, NpcLineTags.Summer | NpcLineTags.Day),
        ],
        ["tailor"] =
        [
            new(1), new(2), new(3), new(4),
            new(5, NpcLineTags.Spring), new(6, NpcLineTags.Summer), new(7, NpcLineTags.Fall), new(8, NpcLineTags.Winter),
            new(9, NpcLineTags.Wet), new(10, NpcLineTags.Snow), new(11, NpcLineTags.Morning), new(12, NpcLineTags.Evening),
            new(13, NpcLineTags.Newcomer), new(14, NpcLineTags.Harvested), new(15, NpcLineTags.Spring | NpcLineTags.Morning), new(16, NpcLineTags.Summer | NpcLineTags.Day),
        ],
        ["furnisher"] =
        [
            new(1), new(2), new(3), new(4),
            new(5, NpcLineTags.Spring), new(6, NpcLineTags.Summer), new(7, NpcLineTags.Fall), new(8, NpcLineTags.Winter),
            new(9, NpcLineTags.Wet), new(10, NpcLineTags.Snow), new(11, NpcLineTags.Morning), new(12, NpcLineTags.Evening),
            new(13, NpcLineTags.Newcomer), new(14, NpcLineTags.Harvested), new(15, NpcLineTags.Spring | NpcLineTags.Morning), new(16, NpcLineTags.DeepMiner),
        ],
        ["blacksmith"] =
        [
            new(1), new(2), new(3), new(4),
            new(5, NpcLineTags.Spring), new(6, NpcLineTags.Summer), new(7, NpcLineTags.Fall), new(8, NpcLineTags.Winter),
            new(9, NpcLineTags.Wet), new(10, NpcLineTags.Snow), new(11, NpcLineTags.Morning), new(12, NpcLineTags.Evening),
            new(13, NpcLineTags.Newcomer), new(14, NpcLineTags.Harvested), new(15, NpcLineTags.DeepMiner), new(16, NpcLineTags.Day),
        ],
        ["carpenter"] =
        [
            new(1), new(2), new(3), new(4),
            new(5, NpcLineTags.Spring), new(6, NpcLineTags.Summer), new(7, NpcLineTags.Fall), new(8, NpcLineTags.Winter),
            new(9, NpcLineTags.Wet), new(10, NpcLineTags.Snow), new(11, NpcLineTags.Morning), new(12, NpcLineTags.Evening),
            new(13, NpcLineTags.Newcomer), new(14, NpcLineTags.Harvested), new(15, NpcLineTags.DeepMiner), new(16, NpcLineTags.Day),
        ],
        ["trader"] =
        [
            new(1), new(2), new(3), new(4),
            new(5, NpcLineTags.Spring), new(6, NpcLineTags.Summer), new(7, NpcLineTags.Fall), new(8, NpcLineTags.Winter),
            new(9, NpcLineTags.Wet), new(10, NpcLineTags.Snow), new(11, NpcLineTags.Morning), new(12, NpcLineTags.Evening),
            new(13, NpcLineTags.Newcomer), new(14, NpcLineTags.Harvested), new(15, NpcLineTags.DeepMiner), new(16, NpcLineTags.Day),
        ],
        ["board_keeper"] =
        [
            new(1), new(2), new(3), new(4),
            new(5, NpcLineTags.Spring), new(6, NpcLineTags.Summer), new(7, NpcLineTags.Fall), new(8, NpcLineTags.Winter),
            new(9, NpcLineTags.Wet), new(10, NpcLineTags.Snow), new(11, NpcLineTags.Morning), new(12, NpcLineTags.Evening),
            new(13, NpcLineTags.Newcomer), new(14, NpcLineTags.Harvested), new(15, NpcLineTags.DeepMiner), new(16, NpcLineTags.Day),
        ],
    };

    /// <summary>Gets the lines a merchant can say, in the order of their numbers.</summary>
    /// <param name="npcId">The merchant's id.</param>
    /// <returns>The merchant's pool, or none for an unknown id.</returns>
    public static IReadOnlyList<NpcLine> PoolOf(string npcId) => npcId is not null && s_pools.TryGetValue(npcId, out NpcLine[]? pool) ? pool : [];
}
