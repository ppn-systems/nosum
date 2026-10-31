// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Shops;

/// <summary>One thing a shop sells: an item at a fixed price in coins per unit. Stock is unlimited.</summary>
/// <param name="ItemId">The item, from <see cref="ItemCatalog"/>.</param>
/// <param name="CoinsEach">Coins for one unit.</param>
/// <param name="Sold">The seasons the counter has the item in stock; <see langword="null"/> for every season (docs/adr/ADR-080 D1).</param>
public readonly record struct ShopOffer(int ItemId, int CoinsEach, SeasonSet? Sold = null);

/// <summary>
/// A shop: a stable id (the only shop name that crosses the wire), a stable key (localization entry <c>shop.&lt;key&gt;.name</c>) and
/// its price list. Where a shop stands is data of the map (<c>MapSpec.Shops</c>), so another shop is one more catalogue entry and
/// one more map point. Prices are balance data, banded by docs/adr/ADR-080 and docs/design/economy.md.
/// </summary>
public sealed class ShopDefinition
{
    private readonly Dictionary<int, int> _prices;
    private readonly Dictionary<int, SeasonSet> _seasons = [];

    /// <summary>Initializes a shop.</summary>
    /// <param name="id">Stable numeric id, greater than zero.</param>
    /// <param name="key">Stable key, for example <c>seed_shop</c>.</param>
    /// <param name="offers">What it sells, in display order.</param>
    public ShopDefinition(int id, string key, IReadOnlyList<ShopOffer> offers)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(offers);

        this.Id = id;
        this.Key = key;
        this.Offers = offers;
        _prices = new Dictionary<int, int>(offers.Count);
        foreach (ShopOffer offer in offers)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offer.CoinsEach);
            _prices.Add(offer.ItemId, offer.CoinsEach);
            if (offer.Sold is { } sold)
            {
                _seasons.Add(offer.ItemId, sold);
            }
        }
    }

    /// <summary>Gets the stable numeric id.</summary>
    public int Id { get; }

    /// <summary>Gets the stable key.</summary>
    public string Key { get; }

    /// <summary>Gets what the shop sells, in display order.</summary>
    public IReadOnlyList<ShopOffer> Offers { get; }

    /// <summary>Looks up what an item costs here.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="coinsEach">Coins for one unit when sold here.</param>
    /// <returns><see langword="true"/> when this shop sells the item.</returns>
    public bool TryGetPrice(int itemId, out int coinsEach) => _prices.TryGetValue(itemId, out coinsEach);

    /// <summary>Tells whether this counter has an item in stock in a season: always, unless the offer names its seasons (docs/adr/ADR-080 D1).</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="season">The season of the server clock.</param>
    /// <returns><see langword="false"/> only for an offer that is not sold in that season.</returns>
    public bool IsSoldIn(int itemId, Season season) => !_seasons.TryGetValue(itemId, out SeasonSet sold) || sold.Contains(season);
}

/// <summary>
/// The shops of the world. Ids are append-only and each shop sells one kind of thing (docs/adr/ADR-013 amendments): seeds in the seed shop,
/// clothing in the clothing shop, furniture in the furniture shop (which new players no longer start with). A shop is one entry here and one
/// <see cref="Maps.ShopPoint"/> on a map.
/// </summary>
public static class ShopCatalog
{
    /// <summary>The seed shop in the town. It was the town's only shop, the general store, before the split, so the id is unchanged.</summary>
    public const int SeedShop = 1;

    /// <summary>The clothing shop.</summary>
    public const int ClothingShop = 2;

    /// <summary>The furniture shop.</summary>
    public const int FurnitureShop = 3;

    /// <summary>Id of the carpenter's shop: hay and the milk pail (docs/adr/ADR-028). Buildings and animals are bought through their own requests at the same counter.</summary>
    public const int CarpenterShop = 4;

    /// <summary>The blacksmith's counter in the town. It sells coal and ore at high prices; upgrading tools is its own flow (docs/adr/ADR-026 D2).</summary>
    public const int Blacksmith = 5;

    private static readonly ShopDefinition[] s_all =
    [
        new(SeedShop, "seed_shop",
        [
            // The shop lists every seed; the server sells only the seeds of crops that grow in the current season (ShopRules.IsInSeason, docs/adr/ADR-018 and ADR-025 D5).
            // Every seed costs less than the first harvest it yields at the bin, so one harvest always pays for its seed (docs/adr/ADR-025 amendment).
            new(ItemCatalog.ParsnipSeeds, 12),
            new(ItemCatalog.RadishSeeds, 20),
            new(ItemCatalog.CauliflowerSeeds, 30),
            new(ItemCatalog.PotatoSeeds, 18),
            new(ItemCatalog.GreenBeanSeeds, 25),
            new(ItemCatalog.KaleSeeds, 22),
            new(ItemCatalog.GarlicSeeds, 16),
            new(ItemCatalog.StrawberrySeeds, 40),
            new(ItemCatalog.TomatoSeeds, 40),
            new(ItemCatalog.BlueberrySeeds, 50),
            new(ItemCatalog.MelonSeeds, 50),
            new(ItemCatalog.HotPepperSeeds, 25),
            new(ItemCatalog.CornSeeds, 55),
            new(ItemCatalog.WheatSeeds, 10),
            new(ItemCatalog.SunflowerSeeds, 22),
            new(ItemCatalog.PumpkinSeeds, 80),
            new(ItemCatalog.CranberriesSeeds, 55),
            new(ItemCatalog.EggplantSeeds, 20),
            new(ItemCatalog.YamSeeds, 30),
            new(ItemCatalog.BokChoySeeds, 20),
            new(ItemCatalog.GrapeSeeds, 35),
        ]),
        new(ClothingShop, "clothing_shop",
        [
            new(ItemCatalog.ShirtRed, 60),
            new(ItemCatalog.ShirtGreen, 60),
            new(ItemCatalog.ShirtBlue, 60),
            new(ItemCatalog.PantsBlue, 80),
            new(ItemCatalog.PantsKhaki, 80),
            // The hats rotate with the seasons (docs/adr/ADR-080 D1): straw for the sunny half of the year, a cap for the cold half.
            new(ItemCatalog.HatStraw, 50, SeasonSet.Spring | SeasonSet.Summer),
            new(ItemCatalog.HatCap, 40, SeasonSet.Fall | SeasonSet.Winter),
        ]),
        new(FurnitureShop, "furniture_shop",
        [
            new(ItemCatalog.OakChair, 100),
            new(ItemCatalog.HousePlant, 80),
            new(ItemCatalog.FloorLamp, 140),
            new(ItemCatalog.OakTable, 220),
            new(ItemCatalog.Bookcase, 260),
            new(ItemCatalog.BlueArmchair, 300),
            new(ItemCatalog.BlueCouch, 400),

            // Seating, tables and beds: the furnisher sells the whole room at once.
            new(ItemCatalog.WoodenStool, 60),
            new(ItemCatalog.CushionedChair, 110),
            new(ItemCatalog.WoodenBench, 150),
            new(ItemCatalog.GreenArmchair, 320),
            new(ItemCatalog.RoundTable, 180),
            new(ItemCatalog.SideTable, 70),
            new(ItemCatalog.LongTable, 260),
            new(ItemCatalog.SingleBed, 350),
            new(ItemCatalog.DoubleBed, 520),

            // Storage and lighting. Lamps give light at night (PlaceableDefinition.LightRadius).
            new(ItemCatalog.Wardrobe, 400),
            new(ItemCatalog.ChestOfDrawers, 200),
            new(ItemCatalog.KitchenCabinet, 130),
            new(ItemCatalog.WallShelf, 90),
            new(ItemCatalog.Candelabra, 120),
            new(ItemCatalog.TableLamp, 90),
            new(ItemCatalog.WallSconce, 80),
            new(ItemCatalog.PaperLantern, 110),

            // Plants, decoration and rugs. The seasonal pieces are sold only in their season (PlaceableDefinition.SoldIn).
            new(ItemCatalog.PottedFern, 60),
            new(ItemCatalog.CactusPot, 50),
            new(ItemCatalog.FlowerBox, 70),
            new(ItemCatalog.Bonsai, 110),
            new(ItemCatalog.LandscapePainting, 150),
            new(ItemCatalog.PortraitPainting, 130),
            new(ItemCatalog.Window, 180),
            new(ItemCatalog.WallClock, 90),
            new(ItemCatalog.Mirror, 140),
            new(ItemCatalog.RedRug, 120),
            new(ItemCatalog.BlueRug, 150),
            new(ItemCatalog.RoundRug, 130),
            new(ItemCatalog.Doormat, 40),
            new(ItemCatalog.PumpkinDecor, 45),
            new(ItemCatalog.WinterWreath, 70),
        ]),
        new(CarpenterShop, "carpenter_shop",
        [
            new(ItemCatalog.Hay, 30),
            new(ItemCatalog.MilkPail, 300),
        ]),
        new(Blacksmith, "blacksmith",
        [
            // Several times what a player gets for the ore elsewhere: the counter is for the player who is a few ore short of a bar, not a way to skip the mine.
            new(ItemCatalog.Coal, 150),
            new(ItemCatalog.CopperOre, 75),
            new(ItemCatalog.IronOre, 150),
        ]),
    ];

    /// <summary>Gets every shop.</summary>
    public static IReadOnlyList<ShopDefinition> All => s_all;

    /// <summary>Looks up a shop by its id.</summary>
    /// <param name="shopId">Shop id as sent by a client; not trusted.</param>
    /// <param name="shop">The shop when found.</param>
    /// <returns><see langword="true"/> when the id names a shop.</returns>
    public static bool TryGet(int shopId, [NotNullWhen(true)] out ShopDefinition? shop)
    {
        foreach (ShopDefinition candidate in s_all)
        {
            if (candidate.Id == shopId)
            {
                shop = candidate;
                return true;
            }
        }

        shop = null;
        return false;
    }
}
