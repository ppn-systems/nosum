// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Placeables;

namespace Nosum.Gameplay.Buildings;

/// <summary>The farm buildings (docs/adr/ADR-028). Values are stored in the database and sent on the wire: append only, never renumber.</summary>
public enum BuildingKind : byte
{
    /// <summary>Not a building.</summary>
    None = 0,

    /// <summary>A coop: houses chickens.</summary>
    Coop = 1,

    /// <summary>A barn: houses cows.</summary>
    Barn = 2,
}

/// <summary>
/// One kind of farm building as data (docs/adr/ADR-028 D1, D2): what it costs, how it looks and what it occupies on the land, where its door is, and the interior
/// map with the tiles animals walk on and eat at. A building is commissioned at the carpenter as a blueprint item and placed like any large piece
/// (<see cref="PlaceableCatalog"/> builds its placeable from this record), so footprint, door tile and interior are replaced here and nowhere else.
/// </summary>
public sealed class BuildingDefinition
{
    /// <summary>Initializes a definition.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="key">Stable key; also the id of the interior map and the localization key suffix.</param>
    /// <param name="blueprintItemId">The inventory item the carpenter hands over and placing consumes.</param>
    /// <param name="coins">Coins the commission costs.</param>
    /// <param name="wood">Wood the commission costs.</param>
    /// <param name="stone">Stone the commission costs.</param>
    /// <param name="capacity">Animals the building holds.</param>
    /// <param name="residents">The species that live in it.</param>
    /// <param name="sheet">The picture's sheet name: a file in <c>sprites/</c> without extension, written <c>buildings/Name</c> (docs/adr/ADR-007).</param>
    /// <param name="view">Sprite rectangle and footprint on the land; the footprint is the bottom rows of the sprite.</param>
    /// <param name="doorX">Door tile, relative to the footprint's left tile.</param>
    /// <param name="doorY">Door tile, relative to the footprint's top tile (the bottom row).</param>
    /// <param name="arrivalX">Interior tile a player stands on after walking in.</param>
    /// <param name="arrivalY">Interior tile a player stands on after walking in.</param>
    /// <param name="wanderLeft">Left tile of the rectangle the animals walk in.</param>
    /// <param name="wanderTop">Top tile of the rectangle the animals walk in.</param>
    /// <param name="wanderWidth">Width in tiles of the rectangle the animals walk in.</param>
    /// <param name="wanderHeight">Height in tiles of the rectangle the animals walk in.</param>
    /// <param name="troughLeft">Left tile of the feeding bench.</param>
    /// <param name="troughTop">Tile row of the feeding bench.</param>
    /// <param name="troughWidth">Tiles the feeding bench is wide.</param>
    public BuildingDefinition(
        BuildingKind kind,
        string key,
        int blueprintItemId,
        long coins,
        int wood,
        int stone,
        int capacity,
        AnimalKind[] residents,
        string sheet,
        PlaceableView view,
        int doorX,
        int doorY,
        int arrivalX,
        int arrivalY,
        int wanderLeft,
        int wanderTop,
        int wanderWidth,
        int wanderHeight,
        int troughLeft,
        int troughTop,
        int troughWidth)
    {
        this.Kind = kind;
        this.Key = key;
        this.BlueprintItemId = blueprintItemId;
        this.Coins = coins;
        this.Wood = wood;
        this.Stone = stone;
        this.Capacity = capacity;
        this.Residents = residents;
        this.Sheet = sheet;
        this.View = view;
        this.DoorX = doorX;
        this.DoorY = doorY;
        this.ArrivalX = arrivalX;
        this.ArrivalY = arrivalY;
        this.WanderLeft = wanderLeft;
        this.WanderTop = wanderTop;
        this.WanderWidth = wanderWidth;
        this.WanderHeight = wanderHeight;
        this.TroughLeft = troughLeft;
        this.TroughTop = troughTop;
        this.TroughWidth = troughWidth;
    }

    /// <summary>Gets the kind.</summary>
    public BuildingKind Kind { get; }

    /// <summary>Gets the stable key, also the interior map's id.</summary>
    public string Key { get; }

    /// <summary>Gets the blueprint item.</summary>
    public int BlueprintItemId { get; }

    /// <summary>Gets the coins a commission costs.</summary>
    public long Coins { get; }

    /// <summary>Gets the wood a commission costs.</summary>
    public int Wood { get; }

    /// <summary>Gets the stone a commission costs.</summary>
    public int Stone { get; }

    /// <summary>Gets how many animals live here.</summary>
    public int Capacity { get; }

    /// <summary>Gets the species that may live here (docs/adr/ADR-028 amendment 2: a coop takes chickens and ducks, a barn cows, goats, sheep and pigs).</summary>
    public IReadOnlyList<AnimalKind> Residents { get; }

    /// <summary>Tells whether a species may live here.</summary>
    /// <param name="species">The species.</param>
    /// <returns><see langword="true"/> when it is one of <see cref="Residents"/>.</returns>
    public bool Houses(AnimalKind species)
    {
        foreach (AnimalKind resident in this.Residents)
        {
            if (resident == species)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the picture's sheet name (<c>buildings/Name</c>, a file in <c>sprites/</c>).</summary>
    public string Sheet { get; }

    /// <summary>Gets the sprite rectangle and footprint on the land.</summary>
    public PlaceableView View { get; }

    /// <summary>Gets the door tile, relative to the footprint's left tile.</summary>
    public int DoorX { get; }

    /// <summary>Gets the door tile, relative to the footprint's top tile.</summary>
    public int DoorY { get; }

    /// <summary>Gets the interior tile (x) a player arrives on.</summary>
    public int ArrivalX { get; }

    /// <summary>Gets the interior tile (y) a player arrives on.</summary>
    public int ArrivalY { get; }

    /// <summary>Gets the left tile of the wander rectangle.</summary>
    public int WanderLeft { get; }

    /// <summary>Gets the top tile of the wander rectangle.</summary>
    public int WanderTop { get; }

    /// <summary>Gets the width of the wander rectangle in tiles.</summary>
    public int WanderWidth { get; }

    /// <summary>Gets the height of the wander rectangle in tiles.</summary>
    public int WanderHeight { get; }

    /// <summary>Gets the left tile of the feeding bench.</summary>
    public int TroughLeft { get; }

    /// <summary>Gets the row of the feeding bench.</summary>
    public int TroughTop { get; }

    /// <summary>Gets how many tiles wide the feeding bench is.</summary>
    public int TroughWidth { get; }

    /// <summary>Tells whether an interior tile belongs to the feeding bench.</summary>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <returns><see langword="true"/> on the bench.</returns>
    public bool IsTrough(int tileX, int tileY) => tileY == this.TroughTop && tileX >= this.TroughLeft && tileX < this.TroughLeft + this.TroughWidth;

    /// <summary>Tells whether an interior tile is inside the rectangle animals walk in.</summary>
    /// <param name="tileX">Tile x.</param>
    /// <param name="tileY">Tile y.</param>
    /// <returns><see langword="true"/> inside the rectangle.</returns>
    public bool InWanderArea(int tileX, int tileY)
        => tileX >= this.WanderLeft && tileX < this.WanderLeft + this.WanderWidth && tileY >= this.WanderTop && tileY < this.WanderTop + this.WanderHeight;
}

/// <summary>
/// The buildings of the game. The interior maps are the early <c>Maps/Coop.tmx</c> and <c>Barn.tmx</c> (docs/adr/ADR-007); the rectangles were measured on
/// <c>sprites/buildings/coop.png</c> and <c>barn.png</c> (the body is the top 112 rows; the strip below is door animation), so replacing the art means replacing this table.
/// </summary>
public static class BuildingCatalog
{
    /// <summary>Sheet of the coop picture.</summary>
    public const string CoopSheet = "buildings/coop";

    /// <summary>Sheet of the barn picture.</summary>
    public const string BarnSheet = "buildings/barn";

    /// <summary>Game days a construction takes.</summary>
    public const int ConstructionDays = 2;

    private static readonly BuildingDefinition[] s_all =
    [
        // The coop body is 96 x 112 (6 x 7 tiles); the bottom three rows are the footprint and the door is the bottom row's second tile.
        // Interior Coop.tmx (12 x 10): the door is the open tile (2, 9) of the bottom wall; animals walk on tiles 2-9 x 4-7 and eat at the bench on row 3, tiles 6-9.
        new(BuildingKind.Coop, "coop", ItemCatalog.CoopBlueprint, 4_000, 300, 100, 4, [AnimalKind.Chicken, AnimalKind.Duck], CoopSheet, new PlaceableView(0, 0, 96, 112, 6, 3), 1, 2, 2, 8, 2, 4, 8, 4, 6, 3, 4),

        // The barn body is 112 x 112 (7 x 7 tiles). Interior Barn.tmx (18 x 15): the door is the open tile (11, 14); animals walk on tiles 6-13 x 4-10; the bench is row 3, tiles 8-11.
        new(BuildingKind.Barn, "barn", ItemCatalog.BarnBlueprint, 6_000, 350, 150, 4, [AnimalKind.Cow, AnimalKind.Goat, AnimalKind.Sheep, AnimalKind.Pig], BarnSheet, new PlaceableView(0, 0, 112, 112, 7, 3), 1, 2, 11, 13, 6, 4, 8, 7, 8, 3, 4),
    ];

    /// <summary>Gets every building.</summary>
    public static IReadOnlyList<BuildingDefinition> All => s_all;

    /// <summary>Looks up a building by kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="definition">The definition when known.</param>
    /// <returns><see langword="true"/> when the kind is a building.</returns>
    public static bool TryGet(BuildingKind kind, [NotNullWhen(true)] out BuildingDefinition? definition)
    {
        foreach (BuildingDefinition candidate in s_all)
        {
            if (candidate.Kind == kind)
            {
                definition = candidate;
                return true;
            }
        }

        definition = null;
        return false;
    }

    /// <summary>Looks up the building a blueprint item places.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="definition">The definition when the item is a blueprint.</param>
    /// <returns><see langword="true"/> for a blueprint.</returns>
    public static bool TryGetByBlueprint(int itemId, [NotNullWhen(true)] out BuildingDefinition? definition)
    {
        foreach (BuildingDefinition candidate in s_all)
        {
            if (candidate.BlueprintItemId == itemId)
            {
                definition = candidate;
                return true;
            }
        }

        definition = null;
        return false;
    }

    /// <summary>Looks up a building by the id of its interior map.</summary>
    /// <param name="mapId">Map id.</param>
    /// <param name="definition">The definition when the map is a building's interior.</param>
    /// <returns><see langword="true"/> for an interior map.</returns>
    public static bool TryGetByInterior(string? mapId, [NotNullWhen(true)] out BuildingDefinition? definition)
    {
        foreach (BuildingDefinition candidate in s_all)
        {
            if (string.Equals(candidate.Key, mapId, System.StringComparison.Ordinal))
            {
                definition = candidate;
                return true;
            }
        }

        definition = null;
        return false;
    }
}
