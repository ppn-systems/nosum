// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Nodes;

namespace Nosum.Gameplay.Items;

/// <summary>
/// The generic item catalogue: every item any feature (farm, market, co-op warehouse, quests) may
/// put in an inventory. Ids are append-only. Ids below 100 are tools (the five basic ones, then the copper tier from 10 and the iron tier from 20, docs/adr/ADR-026), 100-189 crop seeds, 190-199 tree seeds, 200-299 produce, 300-399 furniture (placeable, see Nosum.Gameplay.Placeables), 400-499 clothing, 500-599 resources and bars, 600-699 crafted placeables, 700-799 food (dishes, ADR-024), 800-899 forage (docs/adr/ADR-025), 900-949 animal produce (docs/adr/ADR-028), 950-959 fish (docs/adr/ADR-078); raw produce is edible through its <see cref="ItemDefinition.FoodEnergy"/>.
/// </summary>
public static partial class ItemCatalog
{
    /// <summary>The hoe.</summary>
    public const int Hoe = 1;

    /// <summary>The watering can.</summary>
    public const int WateringCan = 2;

    /// <summary>The axe: trees, stumps and twigs.</summary>
    public const int Axe = 3;

    /// <summary>The pickaxe: stones and ore nodes; clears empty tilled soil.</summary>
    public const int Pickaxe = 4;

    /// <summary>The scythe: weeds and grass; harvests a mature crop.</summary>
    public const int Scythe = 5;

    /// <summary>The milk pail: collects a cow's milk (docs/adr/ADR-028). Sold at the carpenter; not part of the starter kit.</summary>
    public const int MilkPail = 6;
    /// <summary>The copper hoe: tills a line of three tiles when the use is wide.</summary>
    public const int CopperHoe = 10;

    /// <summary>The copper watering can: holds 55, waters a line of three.</summary>
    public const int CopperWateringCan = 11;

    /// <summary>The copper axe: two hits' worth of damage per swing.</summary>
    public const int CopperAxe = 12;

    /// <summary>The copper pickaxe: two hits' worth of damage per swing.</summary>
    public const int CopperPickaxe = 13;

    /// <summary>The iron hoe: tills a line of five tiles when the use is wide.</summary>
    public const int IronHoe = 20;

    /// <summary>The iron watering can: holds 70, waters a line of five.</summary>
    public const int IronWateringCan = 21;

    /// <summary>The iron axe: three hits' worth of damage per swing.</summary>
    public const int IronAxe = 22;

    /// <summary>The iron pickaxe: three hits' worth of damage per swing.</summary>
    public const int IronPickaxe = 23;

    /// <summary>A wooden sword for basic melee combat (docs/adr/ADR-045).</summary>
    public const int WoodenSword = 30;

    /// <summary>A bronze sword forged from copper and wood.</summary>
    public const int BronzeSword = 31;

    /// <summary>An iron sword forged from iron and coal.</summary>
    public const int IronSword = 32;

    /// <summary>A steel broadsword forged from iron and monster bone.</summary>
    public const int SteelSword = 33;

    /// <summary>A gilded blade forged from golem cores and treasure gems.</summary>
    public const int GoldSword = 34;

    /// <summary>An arcane staff that channels mana into magical bolts.</summary>
    public const int ArcaneStaff = 35;

    /// <summary>A fishing rod, cast at water (docs/adr/ADR-078).</summary>
    public const int FishingRod = 40;

    /// <summary>Parsnip seeds.</summary>
    public const int ParsnipSeeds = 100;

    /// <summary>Radish seeds.</summary>
    public const int RadishSeeds = 101;

    /// <summary>Cauliflower seeds.</summary>
    public const int CauliflowerSeeds = 102;

    /// <summary>Potato seeds.</summary>
    public const int PotatoSeeds = 103;

    /// <summary>Green bean seeds.</summary>
    public const int GreenBeanSeeds = 104;

    /// <summary>Kale seeds.</summary>
    public const int KaleSeeds = 105;

    /// <summary>Garlic seeds.</summary>
    public const int GarlicSeeds = 106;

    /// <summary>Strawberry seeds.</summary>
    public const int StrawberrySeeds = 107;

    /// <summary>Tomato seeds.</summary>
    public const int TomatoSeeds = 108;

    /// <summary>Blueberry seeds.</summary>
    public const int BlueberrySeeds = 109;

    /// <summary>Melon seeds.</summary>
    public const int MelonSeeds = 110;

    /// <summary>Hot pepper seeds.</summary>
    public const int HotPepperSeeds = 111;

    /// <summary>Corn seeds.</summary>
    public const int CornSeeds = 112;

    /// <summary>Wheat seeds.</summary>
    public const int WheatSeeds = 113;

    /// <summary>Sunflower seeds.</summary>
    public const int SunflowerSeeds = 114;

    /// <summary>Pumpkin seeds.</summary>
    public const int PumpkinSeeds = 115;

    /// <summary>Cranberries seeds.</summary>
    public const int CranberriesSeeds = 116;

    /// <summary>Eggplant seeds.</summary>
    public const int EggplantSeeds = 117;

    /// <summary>Yam seeds.</summary>
    public const int YamSeeds = 118;

    /// <summary>Bok choy seeds.</summary>
    public const int BokChoySeeds = 119;

    /// <summary>Grape seeds.</summary>
    public const int GrapeSeeds = 120;

    /// <summary>An oak seed, from chopping an oak; planted, it grows into an oak.</summary>
    public const int OakSeed = 190;

    /// <summary>A maple seed.</summary>
    public const int MapleSeed = 191;

    /// <summary>A pine seed.</summary>
    public const int PineSeed = 192;

    /// <summary>A harvested parsnip.</summary>
    public const int Parsnip = 200;

    /// <summary>A harvested radish.</summary>
    public const int Radish = 201;

    /// <summary>A harvested cauliflower.</summary>
    public const int Cauliflower = 202;

    /// <summary>A harvested potato.</summary>
    public const int Potato = 203;

    /// <summary>A harvested green bean.</summary>
    public const int GreenBean = 204;

    /// <summary>A harvested kale.</summary>
    public const int Kale = 205;

    /// <summary>A harvested garlic.</summary>
    public const int Garlic = 206;

    /// <summary>A harvested strawberry.</summary>
    public const int Strawberry = 207;

    /// <summary>A harvested tomato.</summary>
    public const int Tomato = 208;

    /// <summary>A harvested blueberry.</summary>
    public const int Blueberry = 209;

    /// <summary>A harvested melon.</summary>
    public const int Melon = 210;

    /// <summary>A harvested hot pepper.</summary>
    public const int HotPepper = 211;

    /// <summary>A harvested corn.</summary>
    public const int Corn = 212;

    /// <summary>A harvested wheat.</summary>
    public const int Wheat = 213;

    /// <summary>A harvested sunflower.</summary>
    public const int Sunflower = 214;

    /// <summary>A harvested pumpkin.</summary>
    public const int Pumpkin = 215;

    /// <summary>A harvested cranberries.</summary>
    public const int Cranberries = 216;

    /// <summary>A harvested eggplant.</summary>
    public const int Eggplant = 217;

    /// <summary>A harvested yam.</summary>
    public const int Yam = 218;

    /// <summary>A harvested bok choy.</summary>
    public const int BokChoy = 219;

    /// <summary>A harvested grape.</summary>
    public const int Grape = 220;

    /// <summary>A spring onion, foraged in spring.</summary>
    public const int SpringOnion = 800;

    /// <summary>A leek, foraged in spring.</summary>
    public const int Leek = 801;

    /// <summary>A dandelion, foraged in spring.</summary>
    public const int Dandelion = 802;

    /// <summary>A daffodil, foraged in spring.</summary>
    public const int Daffodil = 803;

    /// <summary>A spice berry, foraged in summer.</summary>
    public const int SpiceBerry = 804;

    /// <summary>A sweet pea, foraged in summer.</summary>
    public const int SweetPea = 805;

    /// <summary>A blackberry, foraged in fall.</summary>
    public const int Blackberry = 806;

    /// <summary>A hazelnut, foraged in fall.</summary>
    public const int Hazelnut = 807;

    /// <summary>A wild plum, foraged in fall.</summary>
    public const int WildPlum = 808;

    /// <summary>A mushroom, foraged in fall.</summary>
    public const int Mushroom = 809;

    /// <summary>A winter root, foraged in winter.</summary>
    public const int WinterRoot = 810;

    /// <summary>A crystal fruit, foraged in winter.</summary>
    public const int CrystalFruit = 811;

    /// <summary>A snow yam, foraged in winter.</summary>
    public const int SnowYam = 812;

    /// <summary>A clam, picked up on the beach.</summary>
    public const int Clam = 813;

    /// <summary>A cockle, picked up on the beach.</summary>
    public const int Cockle = 814;

    /// <summary>A piece of coral, picked up on the beach.</summary>
    public const int Coral = 815;

    /// <summary>A sea urchin, picked up on the beach.</summary>
    public const int SeaUrchin = 816;

    /// <summary>A nautilus shell, picked up on the beach.</summary>
    public const int NautilusShell = 817;

    /// <summary>A rainbow shell, picked up on the beach.</summary>
    public const int RainbowShell = 818;

    /// <summary>A fish caught with a fishing rod (docs/adr/ADR-078).</summary>
    public const int Anchovy = 950;

    /// <summary>A fish caught with a fishing rod (docs/adr/ADR-078).</summary>
    public const int SeaBass = 951;

    /// <summary>A fish caught with a fishing rod (docs/adr/ADR-078).</summary>
    public const int Mackerel = 952;

    /// <summary>A fish caught with a fishing rod (docs/adr/ADR-078).</summary>
    public const int Pufferfish = 953;

    /// <summary>A fish caught with a fishing rod (docs/adr/ADR-078).</summary>
    public const int Carp = 954;

    /// <summary>A fish caught with a fishing rod (docs/adr/ADR-078).</summary>
    public const int Trout = 955;

    /// <summary>A fish caught with a fishing rod (docs/adr/ADR-078).</summary>
    public const int Bass = 956;

    /// <summary>A fish caught with a fishing rod (docs/adr/ADR-078).</summary>
    public const int Pike = 957;

    /// <summary>A fish caught with a fishing rod (docs/adr/ADR-078).</summary>
    public const int Eel = 958;

    /// <summary>A fish caught with a fishing rod (docs/adr/ADR-078).</summary>
    public const int GoldenKoi = 959;

    /// <summary>Wood, from trees, stumps and twigs.</summary>
    public const int Wood = 500;

    /// <summary>Stone, from stones.</summary>
    public const int Stone = 501;

    /// <summary>Fiber, from weeds and grass.</summary>
    public const int Fiber = 502;

    /// <summary>Coal, a rare find in stones.</summary>
    public const int Coal = 503;

    /// <summary>Copper ore, from copper ore nodes.</summary>
    public const int CopperOre = 504;

    /// <summary>Iron ore, from iron ore nodes (the mine, docs/adr/ADR-026).</summary>
    public const int IronOre = 505;

    /// <summary>A copper bar, smelted from copper ore and coal in a furnace.</summary>
    public const int CopperBar = 506;

    /// <summary>An iron bar, smelted from iron ore and coal in a furnace.</summary>
    public const int IronBar = 507;

    /// <summary>A storage chest (docs/adr/ADR-024).</summary>
    public const int Chest = 600;

    /// <summary>A wood fence piece.</summary>
    public const int WoodFence = 601;

    /// <summary>A stone path tile.</summary>
    public const int StonePath = 602;

    /// <summary>A scarecrow.</summary>
    public const int Scarecrow = 603;

    /// <summary>A sprinkler: waters the four tiles around it at the start of each game day.</summary>
    public const int Sprinkler = 604;

    /// <summary>A furnace: smelts ore into bars.</summary>
    public const int Furnace = 605;

    /// <summary>Farm guestbook: a placeable that keeps a log of visitors who water the farm (docs/adr/ADR-066).</summary>
    public const int Guestbook = 606;

    /// <summary>The coop blueprint: handed over by the carpenter, set down on the farm or co-op land to start building (docs/adr/ADR-028 D1).</summary>
    public const int CoopBlueprint = 610;

    /// <summary>The barn blueprint (docs/adr/ADR-028 D1).</summary>
    public const int BarnBlueprint = 611;

    /// <summary>A salad.</summary>
    public const int Salad = 700;

    /// <summary>A baked potato.</summary>
    public const int BakedPotato = 701;

    /// <summary>A bowl of pumpkin soup.</summary>
    public const int PumpkinSoup = 702;

    /// <summary>Fish stew, cooked from fish and a potato (docs/adr/ADR-078).</summary>
    public const int FishStew = 703;

    /// <summary>A health potion brewed from slime gel and berries; restores HP (docs/adr/ADR-045).</summary>
    public const int HealthPotion = 710;

    /// <summary>A mana potion brewed from spore caps and mana crystals; restores MP (docs/adr/ADR-045).</summary>
    public const int ManaPotion = 711;

    /// <summary>A golden elixir that restores HP, MP and energy (docs/adr/ADR-045).</summary>
    public const int Elixir = 712;

    /// <summary>An ancient ruby-bound spell grimoire that restores mana and grants Swiftness (docs/adr/ADR-065).</summary>
    public const int ArcaneGrimoire = 713;

    /// <summary>A radiant crimson vitality crystal that restores HP, MP, and grants enhanced Regeneration (docs/adr/ADR-065).</summary>
    public const int HeartCrystal = 714;

    /// <summary>An egg, a chicken's produce (docs/adr/ADR-028 D3; animal produce is ids 900-949).</summary>
    public const int Egg = 900;

    /// <summary>A large egg.</summary>
    public const int LargeEgg = 901;

    /// <summary>A bottle of milk, a cow's produce.</summary>
    public const int Milk = 902;

    /// <summary>A bottle of large milk.</summary>
    public const int LargeMilk = 903;

    /// <summary>A golden egg, the gold-quality produce of a chicken loved by four hearts or more (docs/adr/ADR-028 D3).</summary>
    public const int GoldenEgg = 904;

    /// <summary>A bottle of cream milk, the gold-quality produce of a cow loved by four hearts or more (docs/adr/ADR-028 D3).</summary>
    public const int CreamMilk = 905;

    /// <summary>A duck egg, a duck's produce (docs/adr/ADR-028 amendment 2).</summary>
    public const int DuckEgg = 906;

    /// <summary>A large duck egg.</summary>
    public const int LargeDuckEgg = 907;

    /// <summary>A golden duck egg, the gold-quality produce of a duck loved by four hearts or more.</summary>
    public const int GoldenDuckEgg = 908;

    /// <summary>A bottle of goat milk, a goat's produce.</summary>
    public const int GoatMilk = 909;

    /// <summary>A bottle of large goat milk.</summary>
    public const int LargeGoatMilk = 910;

    /// <summary>A bottle of cream goat milk, the gold-quality produce of a goat loved by four hearts or more.</summary>
    public const int CreamGoatMilk = 911;

    /// <summary>Wool, a sheep's fleece. Spins into the scarecrow's stuffing and sells well.</summary>
    public const int Wool = 912;

    /// <summary>Fine wool, the long soft fleece of a sheep that is well loved.</summary>
    public const int FineWool = 913;

    /// <summary>A golden fleece, the gold-quality fleece of a sheep loved by four hearts or more.</summary>
    public const int GoldenFleece = 914;

    /// <summary>A truffle a pig digs up on a sunny day.</summary>
    public const int Truffle = 915;

    /// <summary>A large truffle, dug up more often by a pig that is well loved.</summary>
    public const int LargeTruffle = 916;

    /// <summary>A golden truffle, the gold-quality truffle of a pig loved by four hearts or more.</summary>
    public const int GoldenTruffle = 917;

    /// <summary>Hay, the feed the carpenter sells for the feeding bench (fiber works too).</summary>
    public const int Hay = 508;

    /// <summary>Translucent slime gel dropped by slimes (docs/adr/ADR-045).</summary>
    public const int SlimeGel = 510;

    /// <summary>A leathery bat wing dropped by cave bats.</summary>
    public const int BatWing = 511;

    /// <summary>A glowing spore cap dropped by spore shrooms.</summary>
    public const int SporeCap = 512;

    /// <summary>A trophy goblin ear dropped by goblin scouts.</summary>
    public const int GoblinEar = 513;

    /// <summary>A sturdy monster bone dropped by skeleton warriors.</summary>
    public const int MonsterBone = 514;

    /// <summary>An enchanted stone heart dropped by stone golems.</summary>
    public const int GolemCore = 515;

    /// <summary>A raw mana crystal dropped by magical creatures.</summary>
    public const int ManaCrystal = 516;

    /// <summary>A cut treasure gem dropped by deep dungeon monsters.</summary>
    public const int TreasureGem = 517;

    /// <summary>A glittering dungeon boss crown dropped by the Golem King (docs/adr/ADR-047).</summary>
    public const int BossCrown = 518;

    /// <summary>An ancient magical scroll dropped by the Cave Broodmother.</summary>
    public const int AncientScroll = 519;

    /// <summary>A golden skeleton key dropped by the Goblin Chieftain.</summary>
    public const int GoldenKey = 520;

    /// <summary>A blazing molten core dropped by the Magma Colossus.</summary>
    public const int ColossusCore = 521;

    /// <summary>An undead necromancer skull trophy relic (docs/adr/ADR-065).</summary>
    public const int NecromancerSkull = 522;

    /// <summary>An engraved ancient runestone tablet (docs/adr/ADR-065).</summary>
    public const int RunestoneTablet = 523;

    /// <summary>An ornate three-flame silver sanctuary candelabra relic (docs/adr/ADR-065).</summary>
    public const int SanctuaryCandelabra = 524;

    /// <summary>Largest stack of furniture.</summary>
    public const int FurnitureStack = 20;

    /// <summary>A red shirt.</summary>
    public const int ShirtRed = 400;

    /// <summary>A green shirt.</summary>
    public const int ShirtGreen = 401;

    /// <summary>A blue shirt.</summary>
    public const int ShirtBlue = 402;

    /// <summary>Sturdy leather armor (docs/adr/ADR-049).</summary>
    public const int LeatherArmor = 405;

    /// <summary>Reinforced iron chestplate offering heavy physical protection.</summary>
    public const int IronChestplate = 406;

    /// <summary>Agile ranger battle tunic reinforced with bat leather (docs/adr/ADR-065).</summary>
    public const int RangerTunic = 407;

    /// <summary>Blue trousers.</summary>
    public const int PantsBlue = 420;

    /// <summary>Khaki trousers.</summary>
    public const int PantsKhaki = 421;

    /// <summary>Iron greaves protecting legs against monster bites.</summary>
    public const int IronGreaves = 422;

    /// <summary>A straw hat.</summary>
    public const int HatStraw = 440;

    /// <summary>A cap.</summary>
    public const int HatCap = 441;

    /// <summary>A forged iron battle helmet.</summary>
    public const int IronHelmet = 442;

    /// <summary>Largest stack of seeds and produce.</summary>
    public const int DefaultStack = 99;

    private static readonly ItemDefinition[] s_all =
    [
        new(Hoe, "hoe", ItemKind.Tool, 1, ToolKind.Hoe),
        new(WateringCan, "watering_can", ItemKind.Tool, 1, ToolKind.WateringCan),
        new(Axe, "axe", ItemKind.Tool, 1, ToolKind.Axe),
        new(Pickaxe, "pickaxe", ItemKind.Tool, 1, ToolKind.Pickaxe),
        new(Scythe, "scythe", ItemKind.Tool, 1, ToolKind.Scythe),
        new(MilkPail, "milk_pail", ItemKind.Tool, 1, ToolKind.MilkPail),
        new(CopperHoe, "copper_hoe", ItemKind.Tool, 1, ToolKind.Hoe, tier: ToolTier.Copper),
        new(CopperWateringCan, "copper_watering_can", ItemKind.Tool, 1, ToolKind.WateringCan, tier: ToolTier.Copper),
        new(CopperAxe, "copper_axe", ItemKind.Tool, 1, ToolKind.Axe, tier: ToolTier.Copper),
        new(CopperPickaxe, "copper_pickaxe", ItemKind.Tool, 1, ToolKind.Pickaxe, tier: ToolTier.Copper),
        new(IronHoe, "iron_hoe", ItemKind.Tool, 1, ToolKind.Hoe, tier: ToolTier.Iron),
        new(IronWateringCan, "iron_watering_can", ItemKind.Tool, 1, ToolKind.WateringCan, tier: ToolTier.Iron),
        new(IronAxe, "iron_axe", ItemKind.Tool, 1, ToolKind.Axe, tier: ToolTier.Iron),
        new(IronPickaxe, "iron_pickaxe", ItemKind.Tool, 1, ToolKind.Pickaxe, tier: ToolTier.Iron),
        new(WoodenSword, "wooden_sword", ItemKind.Tool, 1, ToolKind.Sword),
        new(BronzeSword, "bronze_sword", ItemKind.Tool, 1, ToolKind.Sword, tier: ToolTier.Copper),
        new(IronSword, "iron_sword", ItemKind.Tool, 1, ToolKind.Sword, tier: ToolTier.Iron),
        new(SteelSword, "steel_sword", ItemKind.Tool, 1, ToolKind.Sword, tier: ToolTier.Iron),
        new(GoldSword, "gold_sword", ItemKind.Tool, 1, ToolKind.Sword, tier: ToolTier.Iron),
        new(ArcaneStaff, "arcane_staff", ItemKind.Tool, 1, ToolKind.Staff, tier: ToolTier.Iron),
        new(FishingRod, "fishing_rod", ItemKind.Tool, 1, ToolKind.FishingRod) { ObjectIcon = 132 },
        new(ParsnipSeeds, "parsnip_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Parsnip) { ObjectIcon = 1 },
        new(RadishSeeds, "radish_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Radish) { ObjectIcon = 2 },
        new(CauliflowerSeeds, "cauliflower_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Cauliflower) { ObjectIcon = 3 },
        new(PotatoSeeds, "potato_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Potato) { ObjectIcon = 4 },
        new(GreenBeanSeeds, "green_bean_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.GreenBean) { ObjectIcon = 5 },
        new(KaleSeeds, "kale_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Kale) { ObjectIcon = 6 },
        new(GarlicSeeds, "garlic_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Garlic) { ObjectIcon = 7 },
        new(StrawberrySeeds, "strawberry_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Strawberry) { ObjectIcon = 8 },
        new(TomatoSeeds, "tomato_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Tomato) { ObjectIcon = 9 },
        new(BlueberrySeeds, "blueberry_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Blueberry) { ObjectIcon = 10 },
        new(MelonSeeds, "melon_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Melon) { ObjectIcon = 11 },
        new(HotPepperSeeds, "hot_pepper_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.HotPepper) { ObjectIcon = 12 },
        new(CornSeeds, "corn_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Corn) { ObjectIcon = 13 },
        new(WheatSeeds, "wheat_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Wheat) { ObjectIcon = 14 },
        new(SunflowerSeeds, "sunflower_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Sunflower) { ObjectIcon = 15 },
        new(PumpkinSeeds, "pumpkin_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Pumpkin) { ObjectIcon = 16 },
        new(CranberriesSeeds, "cranberries_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Cranberries) { ObjectIcon = 17 },
        new(EggplantSeeds, "eggplant_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Eggplant) { ObjectIcon = 18 },
        new(YamSeeds, "yam_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Yam) { ObjectIcon = 19 },
        new(BokChoySeeds, "bok_choy_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.BokChoy) { ObjectIcon = 20 },
        new(GrapeSeeds, "grape_seeds", ItemKind.Seed, DefaultStack, cropId: CropCatalog.Grape) { ObjectIcon = 21 },
        new(OakSeed, "oak_seed", ItemKind.TreeSeed, DefaultStack, treeVariant: TreeVariant.Oak),
        new(MapleSeed, "maple_seed", ItemKind.TreeSeed, DefaultStack, treeVariant: TreeVariant.Maple),
        new(PineSeed, "pine_seed", ItemKind.TreeSeed, DefaultStack, treeVariant: TreeVariant.Pine),
        new(Parsnip, "parsnip", ItemKind.Produce, DefaultStack, foodEnergy: 10) { SellPrice = 40, ObjectIcon = 25 },
        new(Radish, "radish", ItemKind.Produce, DefaultStack, foodEnergy: 8) { SellPrice = 60, ObjectIcon = 26 },
        new(Cauliflower, "cauliflower", ItemKind.Produce, DefaultStack, foodEnergy: 20) { SellPrice = 120, ObjectIcon = 27 },
        new(Potato, "potato", ItemKind.Produce, DefaultStack, foodEnergy: 8) { SellPrice = 60, ObjectIcon = 28 },
        new(GreenBean, "green_bean", ItemKind.Produce, DefaultStack, foodEnergy: 4) { SellPrice = 30, ObjectIcon = 29 },
        new(Kale, "kale", ItemKind.Produce, DefaultStack, foodEnergy: 11) { SellPrice = 65, ObjectIcon = 30 },
        new(Garlic, "garlic", ItemKind.Produce, DefaultStack, foodEnergy: 6) { SellPrice = 45, ObjectIcon = 31 },
        new(Strawberry, "strawberry", ItemKind.Produce, DefaultStack, foodEnergy: 15) { SellPrice = 45, ObjectIcon = 32 },
        new(Tomato, "tomato", ItemKind.Produce, DefaultStack, foodEnergy: 6) { SellPrice = 48, ObjectIcon = 33 },
        new(Blueberry, "blueberry", ItemKind.Produce, DefaultStack, foodEnergy: 4) { SellPrice = 20, ObjectIcon = 34 },
        new(Melon, "melon", ItemKind.Produce, DefaultStack, foodEnergy: 27) { SellPrice = 150, ObjectIcon = 35 },
        new(HotPepper, "hot_pepper", ItemKind.Produce, DefaultStack, foodEnergy: 7) { SellPrice = 30, ObjectIcon = 36 },
        new(Corn, "corn", ItemKind.Produce, DefaultStack, foodEnergy: 8) { SellPrice = 60, ObjectIcon = 37 },
        new(Wheat, "wheat", ItemKind.Produce, DefaultStack, foodEnergy: 5) { SellPrice = 40, ObjectIcon = 38 },
        new(Sunflower, "sunflower", ItemKind.Produce, DefaultStack, foodEnergy: 10) { SellPrice = 90, ObjectIcon = 39 },
        new(Pumpkin, "pumpkin", ItemKind.Produce, DefaultStack, foodEnergy: 30) { SellPrice = 180, ObjectIcon = 40 },
        new(Cranberries, "cranberries", ItemKind.Produce, DefaultStack, foodEnergy: 7) { SellPrice = 30, ObjectIcon = 41 },
        new(Eggplant, "eggplant", ItemKind.Produce, DefaultStack, foodEnergy: 7) { SellPrice = 22, ObjectIcon = 42 },
        new(Yam, "yam", ItemKind.Produce, DefaultStack, foodEnergy: 15) { SellPrice = 60, ObjectIcon = 43 },
        new(BokChoy, "bok_choy", ItemKind.Produce, DefaultStack, foodEnergy: 10) { SellPrice = 50, ObjectIcon = 44 },
        new(Grape, "grape", ItemKind.Produce, DefaultStack, foodEnergy: 8) { SellPrice = 37, ObjectIcon = 45 },
        new(Wood, "wood", ItemKind.Resource, DefaultStack),
        new(Stone, "stone", ItemKind.Resource, DefaultStack),
        new(Fiber, "fiber", ItemKind.Resource, DefaultStack),
        new(Coal, "coal", ItemKind.Resource, DefaultStack),
        new(CopperOre, "copper_ore", ItemKind.Resource, DefaultStack),
        new(IronOre, "iron_ore", ItemKind.Resource, DefaultStack),
        new(CopperBar, "copper_bar", ItemKind.Resource, DefaultStack),
        new(IronBar, "iron_bar", ItemKind.Resource, DefaultStack),
        new(Chest, "chest", ItemKind.Placeable, FurnitureStack),
        new(WoodFence, "wood_fence", ItemKind.Placeable, DefaultStack),
        new(StonePath, "stone_path", ItemKind.Placeable, DefaultStack),
        new(Scarecrow, "scarecrow", ItemKind.Placeable, FurnitureStack),
        new(Guestbook, "guestbook", ItemKind.Placeable, FurnitureStack),
        new(Sprinkler, "sprinkler", ItemKind.Placeable, FurnitureStack),
        new(Furnace, "furnace", ItemKind.Placeable, FurnitureStack),
        new(CoopBlueprint, "coop_blueprint", ItemKind.Placeable, 1),
        new(BarnBlueprint, "barn_blueprint", ItemKind.Placeable, 1),
        new(Egg, "egg", ItemKind.Produce, DefaultStack, foodEnergy: 20) { SellPrice = 15, ObjectIcon = 57 },
        new(LargeEgg, "large_egg", ItemKind.Produce, DefaultStack, foodEnergy: 30) { SellPrice = 25, ObjectIcon = 58 },
        new(Milk, "milk", ItemKind.Produce, DefaultStack, foodEnergy: 25) { SellPrice = 27, ObjectIcon = 59 },
        new(LargeMilk, "large_milk", ItemKind.Produce, DefaultStack, foodEnergy: 40) { SellPrice = 45, ObjectIcon = 60 },
        new(GoldenEgg, "golden_egg", ItemKind.Produce, DefaultStack, foodEnergy: 30) { SellPrice = 22, ObjectIcon = 58 },
        new(CreamMilk, "cream_milk", ItemKind.Produce, DefaultStack, foodEnergy: 40) { SellPrice = 40, ObjectIcon = 60 },
        new(DuckEgg, "duck_egg", ItemKind.Produce, DefaultStack, foodEnergy: 25) { SellPrice = 20, ObjectIcon = 120 },
        new(LargeDuckEgg, "large_duck_egg", ItemKind.Produce, DefaultStack, foodEnergy: 35) { SellPrice = 35, ObjectIcon = 121 },
        new(GoldenDuckEgg, "golden_duck_egg", ItemKind.Produce, DefaultStack, foodEnergy: 35) { SellPrice = 30, ObjectIcon = 122 },
        new(GoatMilk, "goat_milk", ItemKind.Produce, DefaultStack, foodEnergy: 30) { SellPrice = 32, ObjectIcon = 123 },
        new(LargeGoatMilk, "large_goat_milk", ItemKind.Produce, DefaultStack, foodEnergy: 45) { SellPrice = 50, ObjectIcon = 124 },
        new(CreamGoatMilk, "cream_goat_milk", ItemKind.Produce, DefaultStack, foodEnergy: 45) { SellPrice = 48, ObjectIcon = 125 },
        new(Wool, "wool", ItemKind.Produce, DefaultStack) { SellPrice = 80, ObjectIcon = 126 },
        new(FineWool, "fine_wool", ItemKind.Produce, DefaultStack) { SellPrice = 110, ObjectIcon = 127 },
        new(GoldenFleece, "golden_fleece", ItemKind.Produce, DefaultStack) { SellPrice = 120, ObjectIcon = 128 },
        new(Truffle, "truffle", ItemKind.Produce, DefaultStack, foodEnergy: 20) { SellPrice = 70, ObjectIcon = 129 },
        new(LargeTruffle, "large_truffle", ItemKind.Produce, DefaultStack, foodEnergy: 30) { SellPrice = 110, ObjectIcon = 130 },
        new(GoldenTruffle, "golden_truffle", ItemKind.Produce, DefaultStack, foodEnergy: 30) { SellPrice = 105, ObjectIcon = 131 },
        new(Hay, "hay", ItemKind.Resource, DefaultStack) { ObjectIcon = 61 },
        new(SlimeGel, "slime_gel", ItemKind.Resource, DefaultStack) { ObjectIcon = 96 },
        new(BatWing, "bat_wing", ItemKind.Resource, DefaultStack) { ObjectIcon = 97 },
        new(SporeCap, "spore_cap", ItemKind.Resource, DefaultStack) { ObjectIcon = 98 },
        new(GoblinEar, "goblin_ear", ItemKind.Resource, DefaultStack) { ObjectIcon = 99 },
        new(MonsterBone, "monster_bone", ItemKind.Resource, DefaultStack) { ObjectIcon = 100 },
        new(GolemCore, "golem_core", ItemKind.Resource, DefaultStack) { ObjectIcon = 101 },
        new(ManaCrystal, "mana_crystal", ItemKind.Resource, DefaultStack) { ObjectIcon = 102 },
        new(TreasureGem, "treasure_gem", ItemKind.Resource, DefaultStack) { ObjectIcon = 103 },
        new(BossCrown, "boss_crown", ItemKind.Clothing, 1, wear: WearSlot.Hat) { SellPrice = 2500, ObjectIcon = 113 },
        new(AncientScroll, "ancient_scroll", ItemKind.Resource, DefaultStack) { SellPrice = 800, ObjectIcon = 106 },
        new(GoldenKey, "golden_key", ItemKind.Resource, DefaultStack) { SellPrice = 1200, ObjectIcon = 114 },
        new(ColossusCore, "colossus_core", ItemKind.Resource, DefaultStack) { SellPrice = 1800, ObjectIcon = 115 },
        new(NecromancerSkull, "necromancer_skull", ItemKind.Resource, DefaultStack) { SellPrice = 450, ObjectIcon = 105 },
        new(RunestoneTablet, "runestone_tablet", ItemKind.Resource, DefaultStack) { SellPrice = 500, ObjectIcon = 110 },
        new(SanctuaryCandelabra, "sanctuary_candelabra", ItemKind.Resource, DefaultStack) { SellPrice = 950, ObjectIcon = 107 },
        new(LeatherArmor, "leather_armor", ItemKind.Clothing, 1, wear: WearSlot.Shirt) { SellPrice = 200, ObjectIcon = 116 },
        new(IronChestplate, "iron_chestplate", ItemKind.Clothing, 1, wear: WearSlot.Shirt) { SellPrice = 600, ObjectIcon = 117 },
        new(RangerTunic, "ranger_tunic", ItemKind.Clothing, 1, wear: WearSlot.Shirt) { SellPrice = 350, ObjectIcon = 111 },
        new(IronGreaves, "iron_greaves", ItemKind.Clothing, 1, wear: WearSlot.Pants) { SellPrice = 400, ObjectIcon = 118 },
        new(IronHelmet, "iron_helmet", ItemKind.Clothing, 1, wear: WearSlot.Hat) { SellPrice = 350, ObjectIcon = 119 },
        new(Salad, "salad", ItemKind.Food, DefaultStack, foodEnergy: 90),
        new(BakedPotato, "baked_potato", ItemKind.Food, DefaultStack, foodEnergy: 60),
        new(PumpkinSoup, "pumpkin_soup", ItemKind.Food, DefaultStack, foodEnergy: 120),
        new(FishStew, "fish_stew", ItemKind.Food, DefaultStack, foodEnergy: 110) { SellPrice = 120, ObjectIcon = 133 },
        new(HealthPotion, "health_potion", ItemKind.Food, DefaultStack, foodEnergy: 15) { ObjectIcon = 93 },
        new(ManaPotion, "mana_potion", ItemKind.Food, DefaultStack, foodEnergy: 15) { ObjectIcon = 94 },
        new(Elixir, "elixir", ItemKind.Food, DefaultStack, foodEnergy: 60) { ObjectIcon = 95 },
        new(ArcaneGrimoire, "arcane_grimoire", ItemKind.Food, DefaultStack, foodEnergy: 40) { SellPrice = 650, ObjectIcon = 104 },
        new(HeartCrystal, "heart_crystal", ItemKind.Food, DefaultStack, foodEnergy: 50) { SellPrice = 750, ObjectIcon = 112 },
        new(OakChair, "oak_chair", ItemKind.Furniture, FurnitureStack),
        new(BlueArmchair, "blue_armchair", ItemKind.Furniture, FurnitureStack),
        new(BlueCouch, "blue_couch", ItemKind.Furniture, FurnitureStack),
        new(OakTable, "oak_table", ItemKind.Furniture, FurnitureStack),
        new(Bookcase, "bookcase", ItemKind.Furniture, FurnitureStack),
        new(FloorLamp, "floor_lamp", ItemKind.Furniture, FurnitureStack),
        new(HousePlant, "house_plant", ItemKind.Furniture, FurnitureStack),
        new(WoodenStool, "wooden_stool", ItemKind.Furniture, FurnitureStack),
        new(CushionedChair, "cushioned_chair", ItemKind.Furniture, FurnitureStack),
        new(WoodenBench, "wooden_bench", ItemKind.Furniture, FurnitureStack),
        new(GreenArmchair, "green_armchair", ItemKind.Furniture, FurnitureStack),
        new(RoundTable, "round_table", ItemKind.Furniture, FurnitureStack),
        new(SideTable, "side_table", ItemKind.Furniture, FurnitureStack),
        new(LongTable, "long_table", ItemKind.Furniture, FurnitureStack),
        new(SingleBed, "single_bed", ItemKind.Furniture, FurnitureStack),
        new(DoubleBed, "double_bed", ItemKind.Furniture, FurnitureStack),
        new(Wardrobe, "wardrobe", ItemKind.Furniture, FurnitureStack),
        new(ChestOfDrawers, "chest_of_drawers", ItemKind.Furniture, FurnitureStack),
        new(KitchenCabinet, "kitchen_cabinet", ItemKind.Furniture, FurnitureStack),
        new(WallShelf, "wall_shelf", ItemKind.Furniture, FurnitureStack),
        new(Candelabra, "candelabra", ItemKind.Furniture, FurnitureStack),
        new(TableLamp, "table_lamp", ItemKind.Furniture, FurnitureStack),
        new(WallSconce, "wall_sconce", ItemKind.Furniture, FurnitureStack),
        new(PaperLantern, "paper_lantern", ItemKind.Furniture, FurnitureStack),
        new(PottedFern, "potted_fern", ItemKind.Furniture, FurnitureStack),
        new(CactusPot, "cactus_pot", ItemKind.Furniture, FurnitureStack),
        new(FlowerBox, "flower_box", ItemKind.Furniture, FurnitureStack),
        new(Bonsai, "bonsai", ItemKind.Furniture, FurnitureStack),
        new(LandscapePainting, "landscape_painting", ItemKind.Furniture, FurnitureStack),
        new(PortraitPainting, "portrait_painting", ItemKind.Furniture, FurnitureStack),
        new(Window, "window", ItemKind.Furniture, FurnitureStack),
        new(WallClock, "wall_clock", ItemKind.Furniture, FurnitureStack),
        new(Mirror, "mirror", ItemKind.Furniture, FurnitureStack),
        new(RedRug, "red_rug", ItemKind.Furniture, FurnitureStack),
        new(BlueRug, "blue_rug", ItemKind.Furniture, FurnitureStack),
        new(RoundRug, "round_rug", ItemKind.Furniture, FurnitureStack),
        new(Doormat, "doormat", ItemKind.Furniture, FurnitureStack),
        new(PumpkinDecor, "pumpkin_decor", ItemKind.Furniture, FurnitureStack),
        new(WinterWreath, "winter_wreath", ItemKind.Furniture, FurnitureStack),
        new(ShirtRed, "shirt_red", ItemKind.Clothing, 1, wear: WearSlot.Shirt),
        new(ShirtGreen, "shirt_green", ItemKind.Clothing, 1, wear: WearSlot.Shirt),
        new(ShirtBlue, "shirt_blue", ItemKind.Clothing, 1, wear: WearSlot.Shirt),
        new(PantsBlue, "pants_blue", ItemKind.Clothing, 1, wear: WearSlot.Pants),
        new(PantsKhaki, "pants_khaki", ItemKind.Clothing, 1, wear: WearSlot.Pants),
        new(HatStraw, "hat_straw", ItemKind.Clothing, 1, wear: WearSlot.Hat),
        new(HatCap, "hat_cap", ItemKind.Clothing, 1, wear: WearSlot.Hat),
        new(SpringOnion, "spring_onion", ItemKind.Produce, DefaultStack, foodEnergy: 4) { SellPrice = 8, ObjectIcon = 65 },
        new(Leek, "leek", ItemKind.Produce, DefaultStack, foodEnergy: 5) { SellPrice = 20, ObjectIcon = 66 },
        new(Dandelion, "dandelion", ItemKind.Produce, DefaultStack, foodEnergy: 4) { SellPrice = 8, ObjectIcon = 67 },
        new(Daffodil, "daffodil", ItemKind.Produce, DefaultStack, foodEnergy: 4) { SellPrice = 15, ObjectIcon = 68 },
        new(SpiceBerry, "spice_berry", ItemKind.Produce, DefaultStack, foodEnergy: 5) { SellPrice = 20, ObjectIcon = 69 },
        new(SweetPea, "sweet_pea", ItemKind.Produce, DefaultStack, foodEnergy: 6) { SellPrice = 25, ObjectIcon = 70 },
        new(Blackberry, "blackberry", ItemKind.Produce, DefaultStack, foodEnergy: 4) { SellPrice = 10, ObjectIcon = 71 },
        new(Hazelnut, "hazelnut", ItemKind.Produce, DefaultStack, foodEnergy: 5) { SellPrice = 20, ObjectIcon = 72 },
        new(WildPlum, "wild_plum", ItemKind.Produce, DefaultStack, foodEnergy: 7) { SellPrice = 30, ObjectIcon = 73 },
        new(Mushroom, "mushroom", ItemKind.Produce, DefaultStack, foodEnergy: 6) { SellPrice = 25, ObjectIcon = 74 },
        new(WinterRoot, "winter_root", ItemKind.Produce, DefaultStack, foodEnergy: 6) { SellPrice = 25, ObjectIcon = 75 },
        new(CrystalFruit, "crystal_fruit", ItemKind.Produce, DefaultStack, foodEnergy: 20) { SellPrice = 80, ObjectIcon = 76 },
        new(SnowYam, "snow_yam", ItemKind.Produce, DefaultStack, foodEnergy: 7) { SellPrice = 30, ObjectIcon = 77 },
        new(Clam, "clam", ItemKind.Produce, DefaultStack) { SellPrice = 50, ObjectIcon = 78 },
        new(Cockle, "cockle", ItemKind.Produce, DefaultStack) { SellPrice = 50, ObjectIcon = 79 },
        new(Coral, "coral", ItemKind.Produce, DefaultStack) { SellPrice = 80, ObjectIcon = 80 },
        new(SeaUrchin, "sea_urchin", ItemKind.Produce, DefaultStack) { SellPrice = 160, ObjectIcon = 81 },
        new(NautilusShell, "nautilus_shell", ItemKind.Produce, DefaultStack) { SellPrice = 120, ObjectIcon = 82 },
        new(RainbowShell, "rainbow_shell", ItemKind.Produce, DefaultStack) { SellPrice = 300, ObjectIcon = 83 },
        new(Anchovy, "anchovy", ItemKind.Produce, DefaultStack) { SellPrice = 18, ObjectIcon = 134 },
        new(SeaBass, "sea_bass", ItemKind.Produce, DefaultStack) { SellPrice = 36, ObjectIcon = 135 },
        new(Mackerel, "mackerel", ItemKind.Produce, DefaultStack) { SellPrice = 48, ObjectIcon = 136 },
        new(Pufferfish, "pufferfish", ItemKind.Produce, DefaultStack) { SellPrice = 120, ObjectIcon = 137 },
        new(Carp, "carp", ItemKind.Produce, DefaultStack) { SellPrice = 24, ObjectIcon = 138 },
        new(Trout, "trout", ItemKind.Produce, DefaultStack) { SellPrice = 42, ObjectIcon = 139 },
        new(Bass, "bass", ItemKind.Produce, DefaultStack) { SellPrice = 54, ObjectIcon = 140 },
        new(Pike, "pike", ItemKind.Produce, DefaultStack) { SellPrice = 90, ObjectIcon = 141 },
        new(Eel, "eel", ItemKind.Produce, DefaultStack) { SellPrice = 96, ObjectIcon = 142 },
        new(GoldenKoi, "golden_koi", ItemKind.Produce, DefaultStack) { SellPrice = 300, ObjectIcon = 143 },
    ];

    private static readonly Dictionary<int, ItemDefinition> s_byId = Index();

    /// <summary>Gets every item, in id order.</summary>
    public static IReadOnlyList<ItemDefinition> All => s_all;

    /// <summary>Looks up an item.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="item">The definition when found.</param>
    /// <returns><see langword="true"/> when the id names an item.</returns>
    public static bool TryGet(int itemId, [NotNullWhen(true)] out ItemDefinition? item) => s_byId.TryGetValue(itemId, out item);

    /// <summary>Tells whether two item ids belong to the same non-none <see cref="ToolKind"/> family (for example <see cref="Hoe"/> and <see cref="CopperHoe"/>).</summary>
    /// <param name="firstItemId">First item id.</param>
    /// <param name="secondItemId">Second item id.</param>
    /// <returns><see langword="true"/> when both items are tools of the same <see cref="ToolKind"/>.</returns>
    public static bool IsSameToolFamily(int firstItemId, int secondItemId)
        => TryGet(firstItemId, out ItemDefinition? first)
        && TryGet(secondItemId, out ItemDefinition? second)
        && first.Tool != ToolKind.None
        && first.Tool == second.Tool;

    private static Dictionary<int, ItemDefinition> Index()
    {
        Dictionary<int, ItemDefinition> index = new(s_all.Length);
        foreach (ItemDefinition item in s_all)
        {
            index.Add(item.Id, item);
        }

        return index;
    }
}
