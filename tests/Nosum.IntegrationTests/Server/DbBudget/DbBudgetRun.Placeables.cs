// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using Nosum.Abstractions.Accounts;
using System.Collections.Generic;
using Nosum.Abstractions.Calendar;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Shops;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.C2S;
using Nosum.Contracts.Building.S2C;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // The farm player's workshop: crafting, a chest, a furnace and the pieces they put down.
    private async Task PlaceablesAsync()
    {
        (double x, double y) = (1032, 264);
        await using BudgetPlayer p = await this.FreshAsync(GameMaps.Farm, x, y, 0, (ItemCatalog.Wood, 200), (ItemCatalog.Chest, 1), (ItemCatalog.Furnace, 1), (ItemCatalog.CopperOre, 10), (ItemCatalog.Coal, 3), (ItemCatalog.Parsnip, 10), (ItemCatalog.Stone, 20), (ItemCatalog.WoodFence, 3));

        _ = await _rig.ProbeAsync<CraftResult>(p, "Crafting", PlaceableOpCode.Craft, "Craft", "a chest from wood", () => new Craft { RecipeId = 1, Times = 1 });
        _ = await _rig.ProbeAsync<CraftResult>(p, "Crafting", PlaceableOpCode.Craft, "Craft", "five fences", () => new Craft { RecipeId = 2, Times = 5 });

        // Put a chest, a furnace and a fence on tiles around the spawn; a tile that is taken is tried again one further on.
        (int chestId, int chestX, int chestY) = await this.PlaceAsync(p, ItemCatalog.Chest, "a chest");
        (int furnaceId, int furnaceX, int furnaceY) = await this.PlaceAsync(p, ItemCatalog.Furnace, "a furnace");
        (int fenceId, int fenceX, int fenceY) = await this.PlaceAsync(p, ItemCatalog.WoodFence, "a fence");

        if (fenceId != 0)
        {
            BudgetRig.Stand(p.AccountId, (fenceX * 16) + 8, ((fenceY + 2) * 16) + 8);
            _ = await _rig.ProbeAsync<PlaceResult>(p, "Placeable", PlaceableOpCode.MoveObject, "MoveObject", "a fence one tile over", () => new MoveObject { ObjectId = fenceId, TileX = fenceX + 1, TileY = fenceY, Rotation = 0 });
            _ = await _rig.ProbeAsync<PlaceResult>(p, "Placeable", PlaceableOpCode.PickUpObject, "PickUpObject", "pick up a fence", () => new PickUpObject { ObjectId = fenceId });
        }

        _ = await _rig.ProbeAsync<PlacedSnapshot>(p, "Placeable", PlaceableOpCode.GetPlaced, "GetPlaced", "a chest and a furnace standing", () => new GetPlaced());

        if (chestId != 0)
        {
            BudgetRig.Stand(p.AccountId, (chestX * 16) + 8, ((chestY + 2) * 16) + 8);
            _ = await _rig.ProbeAsync<ChestSnapshot>(p, "Chest", PlaceableOpCode.OpenChest, "OpenChest", "empty chest", () => new OpenChest { ObjectId = chestId });
            int slot = await this.SlotOfAsync(p, ItemCatalog.Parsnip);
            _ = await _rig.ProbeAsync<ChestResult>(p, "Chest", PlaceableOpCode.ChestMove, "ChestMove", "five parsnips into the chest", () => new ChestMove { ObjectId = chestId, FromSide = ChestSide.Player, FromSlot = (byte)slot, ToSide = ChestSide.Chest, ToSlot = 0, Quantity = 5 });
            _ = await _rig.ProbeAsync<ChestResult>(p, "Chest", PlaceableOpCode.ChestMove, "ChestMove", "one parsnip back out", () => new ChestMove { ObjectId = chestId, FromSide = ChestSide.Chest, FromSlot = 0, ToSide = ChestSide.Player, ToSlot = (byte)slot, Quantity = 1 });
            await _rig.MeasureAsync("Chest", PlaceableOpCode.CloseChest, "CloseChest", "close", async () =>
            {
                using CloseChest close = new() { ObjectId = chestId };
                await p.SendAsync(close);
                return true;
            });
        }

        if (furnaceId != 0)
        {
            BudgetRig.Stand(p.AccountId, (furnaceX * 16) + 8, ((furnaceY + 2) * 16) + 8);
            _ = await _rig.ProbeAsync<FurnaceResult>(p, "Crafting", PlaceableOpCode.StartSmelting, "StartSmelting", "five copper ore", () => new StartSmelting { ObjectId = furnaceId, OreItemId = ItemCatalog.CopperOre });
            _ = await _rig.ProbeAsync<FurnaceSnapshot>(p, "Crafting", PlaceableOpCode.GetFurnaces, "GetFurnaces", "one furnace smelting", () => new GetFurnaces());
            _ = BudgetRig.Service<IDevClockService>().AdvanceDays(1);
            await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(250), _rig.Timeout);
            _ = await _rig.ProbeAsync<FurnaceResult>(p, "Crafting", PlaceableOpCode.CollectFurnace, "CollectFurnace", "a finished batch", () => new CollectFurnace { ObjectId = furnaceId });
        }
    }

    // Sets a piece down on the first free spot in reach of the farm's spawn: the player stands in front of each candidate, nearest first.
    private async Task<(int ObjectId, int X, int Y)> PlaceAsync(BudgetPlayer p, int itemId, string what)
    {
        IMapCatalog maps = BudgetRig.Service<IMapCatalog>();
        _ = maps.TryGet(GameMaps.Farm, out MapDefinition? map);
        int width = 1;
        int height = 1;
        int door = 0;
        if (BuildingCatalog.TryGetByBlueprint(itemId, out BuildingDefinition? building))
        {
            (width, height, door) = (building.View.FootprintWidth, building.View.FootprintHeight, building.DoorX);
        }

        int startX = (int)(map!.SpawnX / 16);
        int startY = (int)(map.SpawnY / 16);
        List<(int X, int Y)> candidates = [];
        for (int y = startY - 8; y <= startY + 3; y++)
        {
            for (int x = startX - 10; x <= startX + 4; x++)
            {
                bool open = true;
                for (int dy = 0; dy <= height && open; dy++)
                {
                    for (int dx = 0; dx < width && open; dx++)
                    {
                        open = !map.Grid.IsBlocked(x + dx, y + dy) && !map.ReservedTiles.Contains((x + dx, y + dy));
                    }
                }

                if (open)
                {
                    candidates.Add((x, y));
                }
            }
        }

        string last = "no candidate";
        foreach ((int x, int y) in candidates.OrderBy(c => Math.Abs(c.X - startX) + Math.Abs(c.Y - startY)).Take(40))
        {
            BudgetRig.Stand(p.AccountId, ((x + door) * 16) + 8, ((y + height + 1) * 16) + 8);
            int slot = await this.SlotOfAsync(p, itemId);
            PlaceResult result = await _rig.ProbeAsync<PlaceResult>(p, "Placeable", PlaceableOpCode.PlaceObject, "PlaceObject", what, () => new PlaceObject { Slot = (byte)slot, TileX = x, TileY = y, Rotation = 0 }, r => r.Status == PlaceStatus.Ok);
            if (result.Status == PlaceStatus.Ok)
            {
                BudgetRig.Stand(p.AccountId, ((x + door) * 16) + 8, ((y + height + 1) * 16) + 8);
                return (result.ObjectId, x, y);
            }

            last = result.Status.ToString();
        }

        this.Failures.Add("place " + what + ": " + last);
        return (0, 0, 0);
    }

    // Commissioning a coop at the carpenter, putting it on the farm, waiting two game days, stocking it and tending the animals.
    private async Task BuildingsAsync()
    {
        (double cx, double cy) = TileCentre(10, 86);
        await using BudgetPlayer p = await this.FreshAsync(GameMaps.Town, cx, cy, 100_000, (ItemCatalog.Wood, 400), (ItemCatalog.Stone, 400), (ItemCatalog.Parsnip, 40), (ItemCatalog.Hay, 40));
        _ = await _rig.ProbeAsync<BuildingResult>(p, "Building", BuildingOpCode.CommissionBuilding, "CommissionBuilding", "a coop blueprint", () => new CommissionBuilding { Kind = (byte)BuildingKind.Coop });

        // Home, and the blueprint is set down like a large piece in front of the farmhouse.
        await BudgetRig.TravelAsync(p.AccountId, new MapInstance(GameMaps.Farm, p.AccountId));
        await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(250), _rig.Timeout);
        (int objectId, _, _) = await this.PlaceAsync(p, ItemCatalog.CoopBlueprint, "a coop blueprint");
        if (objectId == 0)
        {
            return;
        }

        _ = BudgetRig.Service<IDevClockService>().AdvanceDays(3);
        await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(300), _rig.Timeout);
        BuildingsSnapshot mine = await _rig.ProbeAsync<BuildingsSnapshot>(p, "Building", BuildingOpCode.GetBuildings, "GetBuildings", "one coop standing", () => new GetBuildings { Mine = true });
        Guid buildingId = mine.Buildings.First().Id;

        // The animals are bought at the counter.
        await BudgetRig.TravelAsync(p.AccountId, MapInstance.Shared(GameMaps.Town));
        BudgetRig.Stand(p.AccountId, cx, cy);
        _ = await _rig.ProbeAsync<BuildingResult>(p, "Building", BuildingOpCode.BuyAnimal, "BuyAnimal", "a chicken", () => new BuyAnimal { BuildingId = buildingId, Kind = 1, Name = "Clucky" });

        // Inside the coop (a server-side move, as walking onto the door would do), at the trough.
        await BudgetRig.TravelAsync(p.AccountId, new MapInstance(GameMaps.Coop, buildingId));
        BuildingCatalog.TryGet(BuildingKind.Coop, out BuildingDefinition? coop);
        BudgetRig.Stand(p.AccountId, ((coop!.TroughLeft + 1) * 16) + 8, ((coop.TroughTop + 1) * 16) + 8);
        await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(300), _rig.Timeout);
        _ = await _rig.MeasureAsync("Background", null, "AnimalWander", "tick, one player inside a coop", async () =>
        {
            await BudgetRig.Service<Nosum.Infrastructure.Buildings.AnimalWanderService>().TickAsync(_rig.Clock.GetUtcNow().ToUnixTimeMilliseconds() + 10_000, _rig.Timeout);
            return true;
        });
        AnimalsSnapshot animals = await _rig.ProbeAsync<AnimalsSnapshot>(p, "Building", BuildingOpCode.GetAnimals, "GetAnimals", "inside, one chicken", () => new GetAnimals());
        int feed = await this.SlotOfAsync(p, ItemCatalog.Hay);
        _ = await _rig.ProbeAsync<BuildingResult>(p, "Building", BuildingOpCode.FeedAnimals, "FeedAnimals", "hay into the trough", () => new FeedAnimals { Slot = feed, Quantity = 1 });
        if (animals.Animals.Count > 0)
        {
            Guid animal = animals.Animals[0].Id;
            _ = await _rig.ProbeAsync<AnimalResult>(p, "Building", BuildingOpCode.PetAnimal, "PetAnimal", "pet the chicken", () => new PetAnimal { AnimalId = animal });
            _ = BudgetRig.Service<IDevClockService>().AdvanceDays(1);
            await _rig.Counter.SettleAsync(TimeSpan.FromMilliseconds(300), _rig.Timeout);
            _ = await _rig.ProbeAsync<AnimalResult>(p, "Building", BuildingOpCode.CollectProduce, "CollectProduce", "an egg", () => new CollectProduce { AnimalId = animal, Slot = 0 });
        }
    }
}
