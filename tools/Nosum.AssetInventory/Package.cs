// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.AssetInventory;

/// <summary>
/// The work packages of the art replacement (docs/adr/ADR-040), in priority order. A sheet cell that several packages use
/// belongs to the first of them, so every cell is counted once.
/// </summary>
internal enum Package
{
    /// <summary>The player's layered body, pants, shirts, hairstyles and hats.</summary>
    Farmer,

    /// <summary>The tiles placed on Farm, Town and FarmHouse.</summary>
    CoreTerrain,

    /// <summary>Item icons, ground items, furniture and crafted objects.</summary>
    Items,

    /// <summary>Crop growth stages and tilled soil.</summary>
    Crops,

    /// <summary>Tool and weapon icons.</summary>
    Tools,

    /// <summary>Trees, stones, ores, weeds, grass and the mine ladder.</summary>
    Nodes,

    /// <summary>Farm buildings and their props.</summary>
    Buildings,

    /// <summary>Animal sprite sheets.</summary>
    Animals,

    /// <summary>Menu frames, buttons, cursors and the start-screen picture.</summary>
    Ui,

    /// <summary>Tiles placed on every other hosted map: the remaining outdoor maps, the coop and barn interiors and the mine.</summary>
    RemainingMaps,
}

/// <summary>Names of the packages.</summary>
internal static class PackageNames
{
    /// <summary>Returns the heading of a package.</summary>
    /// <param name="package">The package.</param>
    /// <returns>The heading.</returns>
    public static string Title(Package package) => package switch
    {
        Package.Farmer => "Player farmer layers",
        Package.CoreTerrain => "Core terrain (Farm, Town, FarmHouse)",
        Package.Items => "Items (icons, ground items, furniture, crafted objects)",
        Package.Crops => "Crops and soil",
        Package.Tools => "Tools and weapons",
        Package.Nodes => "Resource nodes",
        Package.Buildings => "Buildings and props",
        Package.Animals => "Animals",
        Package.Ui => "UI",
        Package.RemainingMaps => "Remaining maps",
        _ => package.ToString(),
    };
}
