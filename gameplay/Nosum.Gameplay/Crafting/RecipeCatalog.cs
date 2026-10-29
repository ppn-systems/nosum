// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Items;

namespace Nosum.Gameplay.Crafting;

/// <summary>
/// The recipes of the game (docs/adr/ADR-024 D2). Ids are append-only. The cooking recipes use the produce that exists now; swap their
/// inputs for potato and pumpkin when the crop roster (docs/adr/ADR-025) adds them - the output items and ids stay.
/// </summary>
public static class RecipeCatalog
{
    /// <summary>Chest.</summary>
    public const int Chest = 1;

    /// <summary>Wood fence.</summary>
    public const int WoodFence = 2;

    /// <summary>Stone path.</summary>
    public const int StonePath = 3;

    /// <summary>Scarecrow.</summary>
    public const int Scarecrow = 4;

    /// <summary>Sprinkler.</summary>
    public const int Sprinkler = 5;

    /// <summary>Scarecrow stuffed with wool instead of fiber (docs/adr/ADR-028 amendment 2).</summary>
    public const int ScarecrowWool = 27;

    /// <summary>Farm guestbook.</summary>
    public const int Guestbook = 26;

    /// <summary>Furnace.</summary>
    public const int Furnace = 6;

    /// <summary>Salad.</summary>
    public const int Salad = 7;

    /// <summary>Baked potato.</summary>
    public const int BakedPotato = 8;

    /// <summary>Pumpkin soup.</summary>
    public const int PumpkinSoup = 9;

    /// <summary>Health potion.</summary>
    public const int HealthPotion = 10;

    /// <summary>Mana potion.</summary>
    public const int ManaPotion = 11;

    /// <summary>Golden elixir.</summary>
    public const int Elixir = 12;

    /// <summary>Bronze sword.</summary>
    public const int BronzeSword = 13;

    /// <summary>Iron sword.</summary>
    public const int IronSword = 14;

    /// <summary>Steel sword.</summary>
    public const int SteelSword = 15;

    /// <summary>Gold sword.</summary>
    public const int GoldSword = 16;

    /// <summary>Arcane staff.</summary>
    public const int ArcaneStaff = 17;

    /// <summary>Iron helmet (docs/adr/ADR-049).</summary>
    public const int IronHelmet = 18;

    /// <summary>Leather armor.</summary>
    public const int LeatherArmor = 19;

    /// <summary>Iron chestplate.</summary>
    public const int IronChestplate = 20;

    /// <summary>Iron greaves.</summary>
    public const int IronGreaves = 21;

    /// <summary>Ranger tunic (docs/adr/ADR-065).</summary>
    public const int RangerTunic = 22;

    /// <summary>Arcane grimoire (docs/adr/ADR-065).</summary>
    public const int ArcaneGrimoire = 23;

    /// <summary>Heart crystal (docs/adr/ADR-065).</summary>
    public const int HeartCrystal = 24;

    /// <summary>Sanctuary candelabra (docs/adr/ADR-065).</summary>
    public const int SanctuaryCandelabra = 25;

    /// <summary>Most items one craft request may make in a batch (so a request cannot ask for an absurd amount).</summary>
    public const int MaxBatch = 99;

    private static readonly RecipeDefinition[] s_all =
    [
        new(Chest, "chest", ItemCatalog.Chest, 1, RecipeUnlock.Known, new RecipeInput(ItemCatalog.Wood, 50)),
        new(WoodFence, "wood_fence", ItemCatalog.WoodFence, 1, RecipeUnlock.Known, new RecipeInput(ItemCatalog.Wood, 2)),
        new(StonePath, "stone_path", ItemCatalog.StonePath, 1, RecipeUnlock.Known, new RecipeInput(ItemCatalog.Stone, 1)),
        new(Scarecrow, "scarecrow", ItemCatalog.Scarecrow, 1, RecipeUnlock.Of(CraftSkill.Farming, 1), new RecipeInput(ItemCatalog.Wood, 50), new RecipeInput(ItemCatalog.Coal, 1), new RecipeInput(ItemCatalog.Fiber, 20)),
        new(ScarecrowWool, "scarecrow_wool", ItemCatalog.Scarecrow, 1, RecipeUnlock.Of(CraftSkill.Farming, 1), new RecipeInput(ItemCatalog.Wood, 50), new RecipeInput(ItemCatalog.Coal, 1), new RecipeInput(ItemCatalog.Wool, 5)),
        new(Sprinkler, "sprinkler", ItemCatalog.Sprinkler, 1, RecipeUnlock.Of(CraftSkill.Farming, 2), new RecipeInput(ItemCatalog.CopperBar, 1), new RecipeInput(ItemCatalog.IronBar, 1)),
        new(Guestbook, "guestbook", ItemCatalog.Guestbook, 1, RecipeUnlock.Of(CraftSkill.Farming, 1), new RecipeInput(ItemCatalog.Wood, 30), new RecipeInput(ItemCatalog.Fiber, 10)),
        new(Furnace, "furnace", ItemCatalog.Furnace, 1, RecipeUnlock.Of(CraftSkill.Mining, 1), new RecipeInput(ItemCatalog.CopperOre, 20), new RecipeInput(ItemCatalog.Stone, 25)),
        new(Salad, "salad", ItemCatalog.Salad, 1, RecipeUnlock.Known, new RecipeInput(ItemCatalog.Radish, 1), new RecipeInput(ItemCatalog.Cauliflower, 1)),
        new(BakedPotato, "baked_potato", ItemCatalog.BakedPotato, 1, RecipeUnlock.Of(CraftSkill.Farming, 1), new RecipeInput(ItemCatalog.Potato, 2), new RecipeInput(ItemCatalog.Wood, 1)),
        new(PumpkinSoup, "pumpkin_soup", ItemCatalog.PumpkinSoup, 1, RecipeUnlock.Of(CraftSkill.Farming, 3), new RecipeInput(ItemCatalog.Pumpkin, 2), new RecipeInput(ItemCatalog.Radish, 1), new RecipeInput(ItemCatalog.Wood, 1)),
        new(HealthPotion, "health_potion", ItemCatalog.HealthPotion, 1, RecipeUnlock.Known, new RecipeInput(ItemCatalog.SlimeGel, 2), new RecipeInput(ItemCatalog.Fiber, 1)),
        new(ManaPotion, "mana_potion", ItemCatalog.ManaPotion, 1, RecipeUnlock.Of(CraftSkill.Combat, 1), new RecipeInput(ItemCatalog.SporeCap, 1), new RecipeInput(ItemCatalog.ManaCrystal, 1)),
        new(Elixir, "elixir", ItemCatalog.Elixir, 1, RecipeUnlock.Of(CraftSkill.Combat, 4), new RecipeInput(ItemCatalog.HealthPotion, 1), new RecipeInput(ItemCatalog.ManaPotion, 1), new RecipeInput(ItemCatalog.TreasureGem, 1)),
        new(BronzeSword, "bronze_sword", ItemCatalog.BronzeSword, 1, RecipeUnlock.Of(CraftSkill.Combat, 1), new RecipeInput(ItemCatalog.CopperBar, 2), new RecipeInput(ItemCatalog.Wood, 5)),
        new(IronSword, "iron_sword", ItemCatalog.IronSword, 1, RecipeUnlock.Of(CraftSkill.Combat, 2), new RecipeInput(ItemCatalog.IronBar, 2), new RecipeInput(ItemCatalog.BatWing, 2)),
        new(SteelSword, "steel_sword", ItemCatalog.SteelSword, 1, RecipeUnlock.Of(CraftSkill.Combat, 4), new RecipeInput(ItemCatalog.IronBar, 4), new RecipeInput(ItemCatalog.MonsterBone, 3), new RecipeInput(ItemCatalog.Coal, 2)),
        new(GoldSword, "gold_sword", ItemCatalog.GoldSword, 1, RecipeUnlock.Of(CraftSkill.Combat, 6), new RecipeInput(ItemCatalog.GolemCore, 2), new RecipeInput(ItemCatalog.TreasureGem, 2), new RecipeInput(ItemCatalog.IronBar, 4)),
        new(ArcaneStaff, "arcane_staff", ItemCatalog.ArcaneStaff, 1, RecipeUnlock.Of(CraftSkill.Combat, 3), new RecipeInput(ItemCatalog.ManaCrystal, 3), new RecipeInput(ItemCatalog.SporeCap, 2), new RecipeInput(ItemCatalog.Wood, 10)),
        new(LeatherArmor, "leather_armor", ItemCatalog.LeatherArmor, 1, RecipeUnlock.Of(CraftSkill.Combat, 1), new RecipeInput(ItemCatalog.SlimeGel, 4), new RecipeInput(ItemCatalog.Fiber, 10)),
        new(IronHelmet, "iron_helmet", ItemCatalog.IronHelmet, 1, RecipeUnlock.Of(CraftSkill.Combat, 2), new RecipeInput(ItemCatalog.IronBar, 3), new RecipeInput(ItemCatalog.MonsterBone, 2)),
        new(IronChestplate, "iron_chestplate", ItemCatalog.IronChestplate, 1, RecipeUnlock.Of(CraftSkill.Combat, 3), new RecipeInput(ItemCatalog.IronBar, 5), new RecipeInput(ItemCatalog.Coal, 2)),
        new(IronGreaves, "iron_greaves", ItemCatalog.IronGreaves, 1, RecipeUnlock.Of(CraftSkill.Combat, 2), new RecipeInput(ItemCatalog.IronBar, 4), new RecipeInput(ItemCatalog.CopperBar, 2)),
        new(RangerTunic, "ranger_tunic", ItemCatalog.RangerTunic, 1, RecipeUnlock.Of(CraftSkill.Combat, 2), new RecipeInput(ItemCatalog.LeatherArmor, 1), new RecipeInput(ItemCatalog.BatWing, 2), new RecipeInput(ItemCatalog.Fiber, 5)),
        new(ArcaneGrimoire, "arcane_grimoire", ItemCatalog.ArcaneGrimoire, 1, RecipeUnlock.Of(CraftSkill.Combat, 3), new RecipeInput(ItemCatalog.ManaCrystal, 2), new RecipeInput(ItemCatalog.SporeCap, 2), new RecipeInput(ItemCatalog.Wood, 5)),
        new(HeartCrystal, "heart_crystal", ItemCatalog.HeartCrystal, 1, RecipeUnlock.Of(CraftSkill.Combat, 3), new RecipeInput(ItemCatalog.HealthPotion, 1), new RecipeInput(ItemCatalog.GolemCore, 1), new RecipeInput(ItemCatalog.SlimeGel, 2)),
        new(SanctuaryCandelabra, "sanctuary_candelabra", ItemCatalog.SanctuaryCandelabra, 1, RecipeUnlock.Of(CraftSkill.Combat, 4), new RecipeInput(ItemCatalog.IronBar, 2), new RecipeInput(ItemCatalog.ManaCrystal, 2), new RecipeInput(ItemCatalog.Coal, 2)),
    ];

    private static readonly Dictionary<int, RecipeDefinition> s_byId = Index();

    /// <summary>Gets every recipe, in id order.</summary>
    public static IReadOnlyList<RecipeDefinition> All => s_all;

    /// <summary>Looks up a recipe.</summary>
    /// <param name="recipeId">Recipe id.</param>
    /// <param name="recipe">The recipe when found.</param>
    /// <returns><see langword="true"/> when the id names a recipe.</returns>
    public static bool TryGet(int recipeId, [NotNullWhen(true)] out RecipeDefinition? recipe) => s_byId.TryGetValue(recipeId, out recipe);

    private static Dictionary<int, RecipeDefinition> Index()
    {
        Dictionary<int, RecipeDefinition> index = new(s_all.Length);
        foreach (RecipeDefinition recipe in s_all)
        {
            index.Add(recipe.Id, recipe);
        }

        return index;
    }
}
