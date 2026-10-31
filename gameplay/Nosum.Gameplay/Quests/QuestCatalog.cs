// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Quests;

/// <summary>
/// Every quest the game knows (docs/adr/ADR-036 D1). Ids are append-only. The quests are the tutorial chain: <see cref="Chain"/> is the order they
/// are shown in, which is not the id order because <see cref="FirstHarvest"/> existed before the chain did. The reward coins are product defaults, not balance.
/// </summary>
public static class QuestCatalog
{
    /// <summary>Plant a seed, then harvest a crop. The first quest ever; finishing it invites a guest to register.</summary>
    public const int FirstHarvest = 1;

    /// <summary>Till a patch of soil with the hoe.</summary>
    public const int TillSoil = 2;

    /// <summary>Water the soil with the can.</summary>
    public const int WaterCrop = 3;

    /// <summary>Put something into the shipping bin.</summary>
    public const int ShipProduce = 4;

    /// <summary>Buy seeds at the seed shop.</summary>
    public const int BuySeeds = 5;

    /// <summary>Chop a tree down with the axe.</summary>
    public const int ChopTree = 6;

    /// <summary>Craft a chest.</summary>
    public const int CraftChest = 7;

    /// <summary>Craft a fishing rod from wood and fiber.</summary>
    public const int CraftRod = 8;

    /// <summary>Reel in a fish at a pond.</summary>
    public const int CatchFish = 9;

    /// <summary>Break three copper ore nodes in the mine.</summary>
    public const int MineOre = 10;

    /// <summary>Defeat three monsters.</summary>
    public const int SlaySlime = 11;

    /// <summary>Upgrade a tool at the blacksmith and collect it.</summary>
    public const int UpgradeTool = 12;

    /// <summary>Build a coop on the farm.</summary>
    public const int BuildCoop = 13;

    /// <summary>Buy an animal for a building.</summary>
    public const int BuyAnimal = 14;

    /// <summary>Cook a salad.</summary>
    public const int CookMeal = 15;

    /// <summary>Eat something for energy.</summary>
    public const int EatFood = 16;

    /// <summary>Fill a request on the Town board.</summary>
    public const int FillRequest = 17;

    /// <summary>Decorate: set down three pieces of furniture or placeables.</summary>
    public const int Decorate = 18;

    /// <summary>Adopt a pet.</summary>
    public const int AdoptPet = 19;

    /// <summary>Send an emote to the people around you.</summary>
    public const int SendEmote = 20;

    private static readonly QuestDefinition[] s_all =
    [
        new(FirstHarvest, "first_harvest", 40, new QuestObjective(QuestEventKind.CropPlanted, 0, 1), new QuestObjective(QuestEventKind.CropHarvested, 0, 1)),
        new(TillSoil, "till_soil", 10, new QuestObjective(QuestEventKind.SoilTilled, 0, 1)),
        new(WaterCrop, "water_crop", 10, new QuestObjective(QuestEventKind.CropWatered, 0, 1)),
        new(ShipProduce, "ship_produce", 30, new QuestObjective(QuestEventKind.ItemShipped, 0, 1)),
        new(BuySeeds, "buy_seeds", 20, new QuestObjective(QuestEventKind.SeedBought, 0, 1)),
        new(ChopTree, "chop_tree", 30, new QuestObjective(QuestEventKind.NodeBroken, (int)NodeKind.Tree, 1)),
        new(CraftChest, "craft_chest", 50, new QuestObjective(QuestEventKind.ItemCrafted, ItemCatalog.Chest, 1)),
        new(CraftRod, "craft_rod", 30, new QuestObjective(QuestEventKind.ItemCrafted, ItemCatalog.FishingRod, 1)),
        new(CatchFish, "catch_fish", 50, new QuestObjective(QuestEventKind.FishCaught, 0, 1)),
        new(MineOre, "mine_ore", 60, new QuestObjective(QuestEventKind.NodeBroken, (int)NodeKind.CopperOre, 3)),
        new(SlaySlime, "slay_slime", 60, new QuestObjective(QuestEventKind.MonsterDefeated, 0, 3)),
        new(UpgradeTool, "upgrade_tool", 80, new QuestObjective(QuestEventKind.ToolUpgraded, 0, 1)),
        new(BuildCoop, "build_coop", 60, new QuestObjective(QuestEventKind.BuildingPlaced, ItemCatalog.CoopBlueprint, 1)),
        new(BuyAnimal, "buy_animal", 40, new QuestObjective(QuestEventKind.AnimalBought, 0, 1)),
        new(CookMeal, "cook_meal", 40, new QuestObjective(QuestEventKind.ItemCrafted, ItemCatalog.Salad, 1)),
        new(EatFood, "eat_food", 20, new QuestObjective(QuestEventKind.FoodEaten, 0, 1)),
        new(FillRequest, "fill_request", 50, new QuestObjective(QuestEventKind.RequestFilled, 0, 1)),
        new(Decorate, "decorate", 40, new QuestObjective(QuestEventKind.PlaceablePlaced, 0, 3)),
        new(AdoptPet, "adopt_pet", 40, new QuestObjective(QuestEventKind.PetAdopted, 0, 1)),
        new(SendEmote, "send_emote", 10, new QuestObjective(QuestEventKind.EmoteSent, 0, 1)),
    ];

    // Till, water, plant and harvest, ship, buy, chop, craft; then the fishing rod, the pond, the mine, the monsters, the blacksmith,
    // the coop, the animal, the kitchen, the Town board, decorating, the pet and the emote.
    private static readonly int[] s_chain =
    [
        TillSoil, WaterCrop, FirstHarvest, ShipProduce, BuySeeds, ChopTree, CraftChest,
        CraftRod, CatchFish, MineOre, SlaySlime, UpgradeTool, BuildCoop, BuyAnimal, CookMeal, EatFood, FillRequest, Decorate, AdoptPet, SendEmote,
    ];

    private static readonly Dictionary<int, QuestDefinition> s_byId = Index();

    /// <summary>Gets every quest, in id order.</summary>
    public static IReadOnlyList<QuestDefinition> All => s_all;

    /// <summary>Gets the quest ids in the order the chain shows them: the current step is the first one that is not finished.</summary>
    public static IReadOnlyList<int> Chain => s_chain;

    /// <summary>Looks up a quest.</summary>
    /// <param name="questId">Quest id.</param>
    /// <param name="quest">The definition when found.</param>
    /// <returns><see langword="true"/> when the id names a quest.</returns>
    public static bool TryGet(int questId, [NotNullWhen(true)] out QuestDefinition? quest) => s_byId.TryGetValue(questId, out quest);

    /// <summary>Gets a quest's place in <see cref="Chain"/>.</summary>
    /// <param name="questId">Quest id.</param>
    /// <returns>The zero-based position, or <see cref="int.MaxValue"/> for a quest that is not in the chain (it sorts last).</returns>
    public static int PositionOf(int questId)
    {
        for (int i = 0; i < s_chain.Length; i++)
        {
            if (s_chain[i] == questId)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static Dictionary<int, QuestDefinition> Index()
    {
        Dictionary<int, QuestDefinition> index = new(s_all.Length);
        foreach (QuestDefinition quest in s_all)
        {
            index.Add(quest.Id, quest);
        }

        return index;
    }
}
