// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Building;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Building;

/// <summary>One building the carpenter can make a blueprint of.</summary>
/// <param name="Kind">The building.</param>
/// <param name="Name">Its name.</param>
/// <param name="CostText">What it costs, as a line.</param>
/// <param name="CanOrder">Whether the player has the coins, wood and stone.</param>
public sealed record BuildingOrder(BuildingKind Kind, string Name, string CostText, bool CanOrder);

/// <summary>One finished building of the player with room for an animal, and the animal the carpenter sells for it.</summary>
/// <param name="BuildingId">The building.</param>
/// <param name="Title">Its name and how full it is.</param>
/// <param name="Kind">The species that lives there.</param>
/// <param name="BuyLabel">The label of the buy button: species and price.</param>
/// <param name="CanBuy">Whether the player has the coins.</param>
public sealed record AnimalOffer(Guid BuildingId, string Title, AnimalKind Kind, string BuyLabel, bool CanBuy);

/// <summary>
/// The carpenter's building and animal sections (docs/adr/ADR-028 D1, D3), shown under the supplies of the carpenter's shop page: the buildings to order a blueprint for, and
/// the animals to buy for the player's finished buildings with the name the player types. The server decides and prices everything; this only offers what the player could afford
/// and says how it went.
/// </summary>
public sealed class CarpenterViewModel : PanelViewModel, IDisposable
{
    private readonly BuildingChannel _buildings;
    private readonly WalletChannel _wallet;
    private readonly InventoryChannel _inventory;
    private string _animalName = string.Empty;

    /// <summary>Initializes the view model and subscribes to its channels.</summary>
    /// <param name="buildings">The building requests and the player's list.</param>
    /// <param name="wallet">The player's coins.</param>
    /// <param name="inventory">The player's wood and stone.</param>
    /// <param name="text">The page texts.</param>
    public CarpenterViewModel(BuildingChannel buildings, WalletChannel wallet, InventoryChannel inventory, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(inventory);

        _buildings = buildings;
        _wallet = wallet;
        _inventory = inventory;
        buildings.Changed += this.Raise;
        buildings.Completed += this.OnCompleted;
        wallet.Changed += this.Raise;
        inventory.Changed += this.Raise;
    }

    /// <summary>Gets or sets the name typed for the next animal.</summary>
    public string AnimalName
    {
        get => _animalName;
        set
        {
            _animalName = value ?? string.Empty;
            this.Raise();
        }
    }

    /// <summary>Gets the buildings the carpenter makes blueprints of.</summary>
    public IReadOnlyList<BuildingOrder> Orders
    {
        get
        {
            List<BuildingOrder> orders = [];
            foreach (BuildingDefinition building in BuildingCatalog.All)
            {
                bool canOrder = _wallet.Coins >= building.Coins && this.Count(ItemCatalog.Wood) >= building.Wood && this.Count(ItemCatalog.Stone) >= building.Stone;
                orders.Add(new BuildingOrder(building.Kind, this.Text("Building_" + building.Key + "_Name"), this.Text("Carpenter_Cost", Number(building.Coins), building.Wood, building.Stone), canOrder));
            }

            return orders;
        }
    }

    /// <summary>Gets the animals the player could put in a building that is finished and has room.</summary>
    public IReadOnlyList<AnimalOffer> Offers
    {
        get
        {
            List<AnimalOffer> offers = [];
            foreach (BuildingEntry entry in _buildings.MyBuildings())
            {
                if (!BuildingCatalog.TryGet((BuildingKind)entry.Kind, out BuildingDefinition? building)
                    || !AnimalCatalog.TryGet(building.Residents, out AnimalDefinition? species)
                    || !_buildings.IsFinished(entry)
                    || entry.Animals >= entry.Capacity)
                {
                    continue;
                }

                string title = this.Text("Carpenter_Home", this.Text("Building_" + building.Key + "_Name"), entry.Animals, entry.Capacity);
                offers.Add(new AnimalOffer(entry.Id, title, species.Kind, this.Text("Carpenter_BuyAnimal", this.Text("Animal_" + species.Key + "_Name"), Number(species.Coins)), _wallet.Coins >= species.Coins));
            }

            return offers;
        }
    }

    /// <summary>Starts a visit to the counter: reads the player's buildings again.</summary>
    public void Open()
    {
        this.ClearStatus();
        _animalName = string.Empty;
        _buildings.LoadMine();
        this.Raise();
    }

    /// <summary>Orders the blueprint of a building.</summary>
    /// <param name="kind">The building.</param>
    public void Order(BuildingKind kind)
    {
        this.ClearStatus();
        _buildings.Commission((byte)kind);
    }

    /// <summary>Buys an animal for a building, with the typed name.</summary>
    /// <param name="buildingId">The building.</param>
    /// <param name="kind">The species.</param>
    public void BuyAnimal(Guid buildingId, AnimalKind kind)
    {
        if (!AnimalRules.IsValidName(_animalName))
        {
            this.SetStatus(this.Text("Building_Status_" + BuildingStatus.BadName));
            return;
        }

        this.ClearStatus();
        _buildings.BuyAnimal(buildingId, (byte)kind, NicknameRulesNormalize(_animalName));
    }

    /// <summary>Stops listening to the channels.</summary>
    public void Dispose()
    {
        _buildings.Changed -= this.Raise;
        _buildings.Completed -= this.OnCompleted;
        _wallet.Changed -= this.Raise;
        _inventory.Changed -= this.Raise;
    }

    private static string NicknameRulesNormalize(string typed) => Nosum.Gameplay.Players.NicknameRules.Normalize(typed);

    private int Count(int itemId)
    {
        int total = 0;
        for (int i = 0; i < _inventory.Capacity; i++)
        {
            SlotInfo slot = _inventory.SlotAt(i);
            if (slot.ItemId == itemId)
            {
                total += slot.Quantity;
            }
        }

        return total;
    }

    private void OnCompleted(BuildingResultInfo result)
    {
        if (result.Action is not (BuildingAction.Commission or BuildingAction.BuyAnimal))
        {
            return;
        }

        if (result.Status == BuildingStatus.Ok)
        {
            this.SetStatus(this.Text(result.Action == BuildingAction.Commission ? "Building_Done_Commission" : "Building_Done_BuyAnimal"), good: true);
            if (result.Action == BuildingAction.BuyAnimal)
            {
                _animalName = string.Empty;
            }

            return;
        }

        this.SetStatus(this.Text("Building_Status_" + result.Status));
    }
}
