// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Rendering;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.Game;

/// <summary>
/// Pre-interns and caches floating combat numbers (damage, crits, heals) for zero-allocation rendering (docs/adr/ADR-055).
/// </summary>
internal sealed class CombatLabels
{
    private readonly LabelRegistry _registry;
    private readonly IStringLocalizer<Language>? _text;
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

    /// <summary>Gets the pre-interned label id for SporeVent dungeon hazards (docs/adr/ADR-061).</summary>
    public int HazardSporeLabelId { get; }

    /// <summary>Gets the pre-interned label id for SpikeTrap dungeon hazards (docs/adr/ADR-061).</summary>
    public int HazardSpikeLabelId { get; }

    /// <summary>Gets the pre-interned label id for MagmaFissure dungeon hazards (docs/adr/ADR-061).</summary>
    public int HazardMagmaLabelId { get; }

    /// <summary>Gets the pre-interned label id for the celebratory level-up overhead banner (docs/adr/ADR-062).</summary>
    public int LevelUpLabelId { get; }

    /// <summary>Gets the pre-interned label id for Vitality Shrines (docs/adr/ADR-063).</summary>
    public int ShrineVitalityLabelId { get; }

    /// <summary>Gets the pre-interned label id for Arcane Obelisks (docs/adr/ADR-063).</summary>
    public int ShrineArcaneLabelId { get; }

    /// <summary>Gets the pre-interned label id for Sanctuary Empowerment Altars (docs/adr/ADR-063).</summary>
    public int ShrineEmpowermentLabelId { get; }

    public CombatLabels(LabelRegistry registry, IStringLocalizer<Language>? text = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        _text = text;
        this.PortalLabelId = registry.Intern(this.Lookup("Combat_Label_Portal"), 1);
        this.EliteFlameLabelId = registry.Intern(this.Lookup("Combat_Label_Elite_Flame"), 2);
        this.EliteSwiftLabelId = registry.Intern(this.Lookup("Combat_Label_Elite_Swift"), 0);
        this.EliteShieldedLabelId = registry.Intern(this.Lookup("Combat_Label_Elite_Shielded"), 1);
        this.EliteVampiricLabelId = registry.Intern(this.Lookup("Combat_Label_Elite_Vampiric"), 4);
        this.HazardSporeLabelId = registry.Intern(this.Lookup("Combat_Label_Hazard_Spore"), 3);
        this.HazardSpikeLabelId = registry.Intern(this.Lookup("Combat_Label_Hazard_Spike"), 0);
        this.HazardMagmaLabelId = registry.Intern(this.Lookup("Combat_Label_Hazard_Magma"), 2);
        this.LevelUpLabelId = registry.Intern(this.Lookup("Combat_Label_LevelUp"), 1);
        this.ShrineVitalityLabelId = registry.Intern(this.Lookup("Combat_Label_Shrine_Vitality"), 3);
        this.ShrineArcaneLabelId = registry.Intern(this.Lookup("Combat_Label_Shrine_Arcane"), 1);
        this.ShrineEmpowermentLabelId = registry.Intern(this.Lookup("Combat_Label_Shrine_Empowerment"), 1);

        // Pre-intern common values 1..50 for all 5 styles
        for (int i = 1; i <= 50; i++)
        {
            string num = i.ToString(CultureInfo.InvariantCulture);
            _cache[(i, 0)] = registry.Intern("-" + num, 0);
            _cache[(i, 1)] = registry.Intern(this.FormatCrit(num), 1);
            _cache[(i, 2)] = registry.Intern("-" + num, 2);
            _cache[(i, 3)] = registry.Intern("+" + num, 3);
            _cache[(i, 4)] = registry.Intern("-" + num, 4);
        }
    }

    /// <summary>Returns the pre-rendered label id for a combat number.</summary>
    public int GetLabelId(int value, byte style)
    {
        if (_cache.TryGetValue((value, style), out int id))
        {
            return id;
        }

        string num = value.ToString(CultureInfo.InvariantCulture);
        string text = style switch
        {
            1 => this.FormatCrit(num),
            3 => "+" + num,
            _ => "-" + num,
        };

        id = _registry.Intern(text, style);
        _cache[(value, style)] = id;
        return id;
    }

    private string Lookup(string key) => _text is not null ? _text[key].Value : key;

    private string FormatCrit(string num) => _text is not null
        ? string.Format(CultureInfo.CurrentCulture, _text["Combat_Label_Crit"].Value, num)
        : "-" + num;
}
