// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Shop;
using Nosum.Gameplay.Calendar;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The season names, the calendar formats and the season outcome codes exist in both languages (docs/adr/ADR-018, i18n).</summary>
public sealed class SeasonTextTests
{
    [Fact]
    public void Language_SeasonNamesCalendarFormatsAndSeasonOutcomes_ExistInBothLanguages()
    {
        string[] needed =
        [
            .. Enum.GetValues<Season>().Select(s => "Season_" + s),
            "Calendar_Day",
            "Calendar_Year",
            "Calendar_Summary",
            .. Enum.GetValues<FarmStatus>().Where(s => s != FarmStatus.Ok).Select(s => "Farm_Status_" + s),
            .. Enum.GetValues<ShopStatus>().Where(s => s != ShopStatus.Ok).Select(s => "Shop_Status_" + s),
        ];

        Keys("Language.resx").Should().Contain(needed);
        Keys("Language.en.resx").Should().Contain(needed);
    }

    private static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }
}
