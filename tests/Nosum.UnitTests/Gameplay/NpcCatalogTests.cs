// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using Nosum.Core;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Npcs;
using Nosum.Gameplay.Shops;
using Nosum.Rendering;
using Nosum.UnitTests.Maps;
using Nosum.Wasm.ViewModels.Npc;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The merchants (docs/adr/ADR-043 D1): one per counter, standing behind it, with art and text in both languages.</summary>
public sealed class NpcCatalogTests
{
    private static readonly string[] s_parts = ["Name", "Role"];

    public static TheoryData<string> MerchantIds()
    {
        TheoryData<string> ids = [];
        foreach (NpcDefinition npc in NpcCatalog.All)
        {
            ids.Add(npc.Id);
        }

        return ids;
    }

    [Fact]
    public void All_EveryCounterOfEveryMap_HasExactlyOneMerchant()
    {
        foreach (MapSpec map in GameMaps.All)
        {
            foreach (ShopPoint counter in map.Shops)
            {
                NpcCatalog.All.Where(n => n.MapId == map.Id && n.CounterId == counter.ShopId).Should().ContainSingle($"{map.Id} counter {counter.ShopId} needs a merchant");
            }
        }

        foreach (NpcDefinition npc in NpcCatalog.All)
        {
            GameMaps.TryGet(npc.MapId, out MapSpec? map).Should().BeTrue();
            map!.Shops.Should().Contain(p => p.ShopId == npc.CounterId, $"{npc.Id} serves a counter of {npc.MapId}");
        }
    }

    [Fact]
    public void All_TheTownHasTheSevenMerchants()
    {
        NpcCatalog.All.Select(n => n.CounterId).Should().BeEquivalentTo(
        [
            ShopCatalog.SeedShop, ShopCatalog.ClothingShop, ShopCatalog.FurnitureShop, ShopCatalog.Blacksmith, ShopCatalog.CarpenterShop,
            MarketRules.MarketCounterId, CommunityGoalCatalog.BoardCounterId,
        ]);
        NpcCatalog.All.Should().OnlyContain(n => n.MapId == GameMaps.Town);
    }

    [Fact]
    public void All_IdsSpritesAndPortraitsAreUnique_AndKeysAreSnakeCase()
    {
        NpcCatalog.All.Select(n => n.Id).Should().OnlyHaveUniqueItems().And.OnlyContain(id => id.All(c => char.IsAsciiLetterLower(c) || c == '_'));
        NpcCatalog.All.Select(n => n.SpriteKey).Should().OnlyHaveUniqueItems();
        NpcCatalog.All.Select(n => n.PortraitKey).Should().OnlyHaveUniqueItems();
        NpcCatalog.All.Select(n => (n.TileX, n.TileY)).Should().OnlyHaveUniqueItems("two merchants never share a tile");
    }

    [Theory]
    [MemberData(nameof(MerchantIds))]
    public void Merchant_StandsBesideItsCounter_WithinReach(string id)
    {
        _ = NpcCatalog.TryGet(id, out NpcDefinition? npc);
        _ = GameMaps.TryGet(npc!.MapId, out MapSpec? map);
        ShopPoint counter = map!.Shops.Single(p => p.ShopId == npc.CounterId);

        (npc.TileX, npc.TileY).Should().NotBe((counter.TileX, counter.TileY), "the farmer stands on the counter tile");
        npc.TileY.Should().Be(counter.TileY, "the merchant stands on the counter's row");
        Math.Abs(npc.TileX - counter.TileX).Should().BeInRange(1, 2, "the merchant stands beside the counter's tile");

        // A farmer on the counter tile can reach the counter, and a click on the merchant is only taken from there.
        ShopRules.IsAtCounter(map, npc.CounterId, (counter.TileX * 16) + 8, (counter.TileY * 16) + 8, 16).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(MerchantIds))]
    public void Merchant_StandsOnOpenGround_WithNoAwningOrRoofOverTheSprite(string id)
    {
        _ = NpcCatalog.TryGet(id, out NpcDefinition? npc);
        _ = GameMaps.TryGet(npc!.MapId, out MapSpec? spec);
        TileMap map = TmxReader.Parse(File.ReadAllText(RepoFiles.Path("art", "own", "Maps", spec!.TmxFile)));

        spec.BuildWalkGrid(map).IsBlocked(npc.TileX, npc.TileY).Should().BeFalse("the merchant does not stand in a wall or on a crate");

        // The sprite is two tiles tall: its feet tile and the one above. A front-layer tile over either would draw over the merchant (the shop awnings do).
        foreach (TileLayer layer in TileLayerPlan.For(map).Front.ToArray())
        {
            layer.Gids[(npc.TileY * map.Width) + npc.TileX].Should().Be(0, $"{layer.Name} must be empty at the merchant's feet");
            layer.Gids[((npc.TileY - 1) * map.Width) + npc.TileX].Should().Be(0, $"{layer.Name} must be empty where the merchant's head is");
        }
    }

    [Theory]
    [MemberData(nameof(MerchantIds))]
    public void Merchant_ArtExists_AtTheSizesTheClientDraws(string id)
    {
        _ = NpcCatalog.TryGet(id, out NpcDefinition? npc);

        PngSize(npc!.SpriteKey).Should().Be((32, 32), "two 16 x 32 idle frames side by side");
        PngSize(npc.PortraitKey).Should().Be((32, 32));
    }

    [Theory]
    [MemberData(nameof(MerchantIds))]
    public void Merchant_HasNameRoleAndLinesInVietnameseAndEnglish(string id)
    {
        _ = NpcCatalog.TryGet(id, out NpcDefinition? npc);

        foreach (string file in new[] { "Language.resx", "Language.en.resx" })
        {
            System.Collections.Generic.Dictionary<string, string> text = Texts(file);
            foreach (string part in s_parts)
            {
                text.Should().ContainKey(npc!.TextKey(part), $"{file} needs {part} of {id}");
                text[npc.TextKey(part)].Should().NotBeNullOrWhiteSpace();
            }

            // The pool (docs/adr/ADR-080 D2): every line that NpcLineCatalog names has text in this language.
            foreach (NpcLine line in NpcLineCatalog.PoolOf(id))
            {
                string key = npc!.TextKey("Line_" + line.Number.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                text.Should().ContainKey(key, $"{file} needs {key}");
                text[key].Should().NotBeNullOrWhiteSpace();
            }
        }

        string firstLine = npc!.TextKey("Line_01");
        Texts("Language.resx")[firstLine].Should().NotBe(Texts("Language.en.resx")[firstLine], "the lines are translated");
        Texts("Language.resx")[npc.TextKey("Role")].Should().NotBe(Texts("Language.en.resx")[npc.TextKey("Role")]);
    }

    [Fact]
    public void Actions_EveryMerchantTalksAndOffersOnlyWhatItsCounterSupports()
    {
        NpcCatalog.All.Should().OnlyContain(n => (n.Actions & NpcActions.Talk) != 0);
        NpcCatalog.All.Where(n => (n.Actions & NpcActions.Sell) != 0).Select(n => n.CounterId).Should().BeEquivalentTo([MarketRules.MarketCounterId]);
        NpcCatalog.All.Where(n => (n.Actions & NpcActions.Build) != 0).Select(n => n.CounterId).Should().BeEquivalentTo([ShopCatalog.CarpenterShop]);
        NpcCatalog.All.Where(n => (n.Actions & NpcActions.Upgrade) != 0).Select(n => n.CounterId).Should().BeEquivalentTo([ShopCatalog.Blacksmith]);
        NpcCatalog.All.Where(n => (n.Actions & NpcActions.Goals) != 0).Select(n => n.CounterId).Should().BeEquivalentTo([CommunityGoalCatalog.BoardCounterId]);
        NpcCatalog.All.Where(n => (n.Actions & NpcActions.Coop) != 0).Select(n => n.CounterId).Should().BeEquivalentTo([CommunityGoalCatalog.BoardCounterId]);
        NpcCatalog.All.Where(n => (n.Actions & (NpcActions.Wardrobe | NpcActions.Backpack)) != 0).Select(n => n.CounterId).Should().BeEquivalentTo([ShopCatalog.ClothingShop]);
        NpcCatalog.All.Where(n => ShopCatalog.TryGet(n.CounterId, out _)).Should().OnlyContain(n => (n.Actions & NpcActions.Buy) != 0, "every shop is bought from");
    }

    [Fact]
    public void Lookups_FindTheMerchantByIdCounterAndMap()
    {
        NpcCatalog.TryGet("blacksmith", out NpcDefinition? smith).Should().BeTrue();
        NpcCatalog.TryGetByCounter(ShopCatalog.Blacksmith, out NpcDefinition? same).Should().BeTrue();
        same.Should().BeSameAs(smith);
        NpcCatalog.TryGet("nobody", out _).Should().BeFalse();
        NpcCatalog.TryGetByCounter(999, out _).Should().BeFalse();
        NpcCatalog.OnMap(GameMaps.Town).Should().HaveCount(7);
        NpcCatalog.OnMap(GameMaps.Farm).Should().BeEmpty();
    }

    private static (int Width, int Height) PngSize(string key)
    {
        byte[] head = File.ReadAllBytes(RepoFiles.Path("assets", "sprites", "npcs", key + ".png"));

        // The IHDR chunk follows the 8-byte signature: length, "IHDR", then width and height as big-endian 32-bit numbers.
        return (ReadInt(head, 16), ReadInt(head, 20));

        static int ReadInt(byte[] bytes, int at) => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
    }

    private static System.Collections.Generic.Dictionary<string, string> Texts(string file)
    {
        string path = RepoFiles.Path("wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? string.Empty);
    }
}
