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
    private const int MaxPreInternedValue = 250;

    private readonly IStringLocalizer<Language>? _text;
    private readonly Dictionary<(int Value, byte Style), int> _cache = new(MaxPreInternedValue * 5);

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

    /// <summary>Gets the pre-interned label id for Ancient Thunder Pylons (docs/adr/ADR-064).</summary>
    public int ShrineThunderLabelId { get; }

    /// <summary>Gets the pre-interned label id for Sylvan Solar Wayshrines (docs/adr/ADR-064).</summary>
    public int ShrineSolarLabelId { get; }

    /// <summary>Gets the pre-interned label id for Glacial Frost Monoliths (docs/adr/ADR-064).</summary>
    public int ShrineFrostLabelId { get; }

    public CombatLabels(LabelRegistry registry, IStringLocalizer<Language>? text = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
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
        this.ShrineThunderLabelId = registry.Intern(this.Lookup("Combat_Label_Shrine_Thunder"), 1);
        this.ShrineSolarLabelId = registry.Intern(this.Lookup("Combat_Label_Shrine_Solar"), 2);
        this.ShrineFrostLabelId = registry.Intern(this.Lookup("Combat_Label_Shrine_Frost"), 0);

        // Pre-intern combat numbers 1..MaxPreInternedValue for all 5 styles outside the frame loop so GetLabelId never allocates or calls JS mid-frame.
        for (int i = 1; i <= MaxPreInternedValue; i++)
        {
            string num = i.ToString(CultureInfo.InvariantCulture);
            _cache[(i, 0)] = registry.Intern("-" + num, 0);
            _cache[(i, 1)] = registry.Intern(this.FormatCrit(num), 1);
            _cache[(i, 2)] = registry.Intern("-" + num, 2);
            _cache[(i, 3)] = registry.Intern("+" + num, 3);
            _cache[(i, 4)] = registry.Intern("-" + num, 4);
        }
    }

    /// <summary>Returns the pre-rendered label id for a combat number without allocating or crossing JS interop.</summary>
    public int GetLabelId(int value, byte style)
    {
        int clamped = Math.Clamp(value, 1, MaxPreInternedValue);
        byte clampedStyle = style <= 4 ? style : (byte)0;
        return _cache.TryGetValue((clamped, clampedStyle), out int id) ? id : 0;
    }

    private string Lookup(string key) => _text is not null ? _text[key].Value : key;

    private string FormatCrit(string num) => _text is not null
        ? string.Format(CultureInfo.CurrentCulture, _text["Combat_Label_Crit"].Value, num)
        : "-" + num;
}
