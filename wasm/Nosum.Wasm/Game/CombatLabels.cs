// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nosum.Rendering;

namespace Nosum.Wasm.Game;

/// <summary>
/// Pre-interns and caches floating combat numbers (damage, crits, heals) for zero-allocation rendering (docs/adr/ADR-055).
/// </summary>
internal sealed class CombatLabels
{
    private readonly LabelRegistry _registry;
    private readonly Dictionary<(int Value, byte Style), int> _cache = new(256);

    /// <summary>Gets the pre-interned label id for the Ancient Dungeon portal (docs/adr/ADR-057).</summary>
    public int PortalLabelId { get; }

    /// <summary>Gets the pre-interned label id for Flame-Aura Elite monsters (docs/adr/ADR-058).</summary>
    public int EliteFlameLabelId { get; }

    /// <summary>Gets the pre-interned label id for Swift Elite monsters (docs/adr/ADR-058).</summary>
    public int EliteSwiftLabelId { get; }

    /// <summary>Gets the pre-interned label id for Shielded Elite monsters (docs/adr/ADR-058).</summary>
    public int EliteShieldedLabelId { get; }

    /// <summary>Gets the pre-interned label id for Vampiric Elite monsters (docs/adr/ADR-058).</summary>
    public int EliteVampiricLabelId { get; }

    public CombatLabels(LabelRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        this.PortalLabelId = registry.Intern("HẦM NGỤC CỔ ĐẠI (TẦNG 10)", 1);
        this.EliteFlameLabelId = registry.Intern("★ CUỒNG HỎA ★", 2);
        this.EliteSwiftLabelId = registry.Intern("★ TỐC BIẾN ★", 0);
        this.EliteShieldedLabelId = registry.Intern("★ HỘ GIÁP ★", 1);
        this.EliteVampiricLabelId = registry.Intern("★ HUYẾT THỆ ★", 4);

        // Pre-intern common values 1..50 for all 5 styles
        for (int i = 1; i <= 50; i++)
        {
            _cache[(i, 0)] = registry.Intern($"-{i}", 0); // Normal monster hit (white)
            _cache[(i, 1)] = registry.Intern($"CRIT! -{i}", 1); // Crit hit (gold)
            _cache[(i, 2)] = registry.Intern($"-{i}", 2); // Player damage (red)
            _cache[(i, 3)] = registry.Intern($"+{i}", 3); // Heal / regen (green)
            _cache[(i, 4)] = registry.Intern($"-{i}", 4); // Poison damage (purple)
        }
    }

    /// <summary>Returns the pre-rendered label id for a combat number.</summary>
    public int GetLabelId(int value, byte style)
    {
        if (_cache.TryGetValue((value, style), out int id))
        {
            return id;
        }

        string text = style switch
        {
            1 => $"CRIT! -{value}",
            3 => $"+{value}",
            _ => $"-{value}",
        };

        id = _registry.Intern(text, style);
        _cache[(value, style)] = id;
        return id;
    }
}
