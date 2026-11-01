// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Building;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Contracts.Pet;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Pets;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.ViewModels.Pets;
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

/// <summary>One pet the carpenter sells (docs/adr/ADR-075 amendment): a cat or a dog, its price and whether the player can buy it now.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Name">Its name, localized.</param>
/// <param name="PriceText">What it costs, as a line.</param>
/// <param name="CanBuy">Whether the player has no pet yet and the coins for it.</param>
public sealed record PetOffer(PetKind Kind, string Name, string PriceText, bool CanBuy);

/// <summary>The tabs of the carpenter's counter (docs/design/ui-standard.md section 3): the goods of the shop, the buildings and animals, and the pets.</summary>
public enum CarpenterTab : byte
{
    /// <summary>The goods the shop sells (hay, milk pail).</summary>
    Goods = 0,

    /// <summary>The blueprints and the animals for a finished building.</summary>
    Buildings = 1,

    /// <summary>The cat or the dog to buy.</summary>
    Pets = 2,
}

/// <summary>Where the pet purchase is: nothing open, the name being typed, or the question to confirm. Nothing is sent before the confirm step.</summary>
public enum PetPurchaseStep : byte
{
    /// <summary>No purchase is open.</summary>
    None = 0,

    /// <summary>A cat or a dog is chosen; the name is being typed.</summary>
    Name = 1,

    /// <summary>The kind, the name and the price are shown; Confirm sends the purchase, Cancel sends nothing.</summary>
    Confirm = 2,
}

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
    private readonly PetViewModel _pet;
    private string _animalName = string.Empty;
    private string _petName = string.Empty;
    private PetKind _chosenKind = PetKind.None;
    private PetPurchaseStep _petStep = PetPurchaseStep.None;
    private string _petError = string.Empty;

    /// <summary>Initializes the view model and subscribes to its channels.</summary>
    /// <param name="buildings">The building requests and the player's list.</param>
    /// <param name="wallet">The player's coins.</param>
    /// <param name="inventory">The player's wood and stone.</param>
    /// <param name="pet">The player's pet: the carpenter sells cats and dogs through it.</param>
    /// <param name="text">The page texts.</param>
    public CarpenterViewModel(BuildingChannel buildings, WalletChannel wallet, InventoryChannel inventory, PetViewModel pet, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(buildings);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(pet);

        _buildings = buildings;
        _wallet = wallet;
        _inventory = inventory;
        _pet = pet;
        buildings.Changed += this.Raise;
        buildings.Completed += this.OnCompleted;
        wallet.Changed += this.Raise;
        inventory.Changed += this.Raise;
        pet.Changed += this.Raise;
        pet.Completed += this.OnPetCompleted;
    }

    /// <summary>Gets or sets the name typed for the next pet (a cat or a dog from the pet section).</summary>
    public string PetName
    {
        get => _petName;
        set
        {
            _petName = value ?? string.Empty;
            this.Raise();
        }
    }

    /// <summary>Gets the maximum allowed length for a pet's name.</summary>
    public static int MaxPetNameLength => PetViewModel.MaxNameLength;

    /// <summary>Gets whether the player already has a pet: the carpenter sells a new one only to a player without one.</summary>
    public bool HasPet => _pet.HasPet;

    /// <summary>Gets the cats and dogs the carpenter sells, with their prices.</summary>
    public IReadOnlyList<PetOffer> PetOffers
    {
        get
        {
            PetOffer Offer(PetKind kind)
            {
                long price = PetRules.PriceOf((byte)kind);
                return new PetOffer(kind, _pet.KindNameOf(kind), this.Text("Carpenter_PetPrice", Number(price)), !_pet.HasPet && _wallet.Coins >= price);
            }

            return [Offer(PetKind.Cat), Offer(PetKind.Dog)];
        }
    }

    /// <summary>Gets the status of the last pet purchase, localized, or an empty string.</summary>
    public string PetStatusText => _pet.Status;

    /// <summary>Gets whether the pet status reports a success.</summary>
    public bool PetStatusIsGood => _pet.StatusIsGood;

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

    /// <summary>Gets the maximum allowed length for an animal's name.</summary>
    public static int MaxAnimalNameLength => Nosum.Gameplay.Players.NicknameRules.MaxLength;

    /// <summary>Gets the buildings the carpenter makes blueprints of.</summary>
    public IReadOnlyList<BuildingOrder> Orders
    {
        get
        {
            List<BuildingOrder> orders = [];
            foreach (BuildingDefinition building in BuildingCatalog.All)
            {
                bool canOrder = _wallet.Coins >= building.Coins && _inventory.CountOf(ItemCatalog.Wood) >= building.Wood && _inventory.CountOf(ItemCatalog.Stone) >= building.Stone;
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
                    || !_buildings.IsFinished(entry)
                    || entry.Animals >= entry.Capacity)
                {
                    continue;
                }

                string title = this.Text("Carpenter_Home", this.Text("Building_" + building.Key + "_Name"), entry.Animals, entry.Capacity);
                foreach (AnimalKind resident in building.Residents)
                {
                    if (!AnimalCatalog.TryGet(resident, out AnimalDefinition? species))
                    {
                        continue;
                    }

                    offers.Add(new AnimalOffer(entry.Id, title, species.Kind, this.Text("Carpenter_BuyAnimal", this.Text("Animal_" + species.Key + "_Name"), Number(species.Coins)), _wallet.Coins >= species.Coins));
                }
            }

            return offers;
        }
    }

    /// <summary>Starts a visit to the counter: reads the player's buildings again.</summary>
    public void Open()
    {
        this.ClearStatus();
        _animalName = string.Empty;
        _petName = string.Empty;
        _chosenKind = PetKind.None;
        _petStep = PetPurchaseStep.None;
        _petError = string.Empty;
        _pet.ClearStatus();
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

    /// <summary>Orders the blueprint of a building from its display row.</summary>
    /// <param name="order">The building order row.</param>
    public void Order(BuildingOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        this.Order(order.Kind);
    }

    /// <summary>Orders the blueprint of a building from its display row.</summary>
    /// <param name="order">The building order row.</param>
    /// <returns>A completed task.</returns>
    public Task OrderAsync(BuildingOrder order)
    {
        this.Order(order);
        return Task.CompletedTask;
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

    /// <summary>Buys an animal from its offer row, with the typed name.</summary>
    /// <param name="offer">The animal offer row.</param>
    public void BuyAnimal(AnimalOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        this.BuyAnimal(offer.BuildingId, offer.Kind);
    }

    /// <summary>Buys an animal from its offer row, with the typed name.</summary>
    /// <param name="offer">The animal offer row.</param>
    /// <returns>A completed task.</returns>
    public Task BuyAnimalAsync(AnimalOffer offer)
    {
        this.BuyAnimal(offer);
        return Task.CompletedTask;
    }

    /// <summary>Gets where the pet purchase is: nothing open, the name being typed, or the question to confirm.</summary>
    public PetPurchaseStep PetStep => _petStep;

    /// <summary>Gets the kind chosen for the purchase, or <see cref="PetKind.None"/>.</summary>
    public PetKind ChosenPetKind => _chosenKind;

    /// <summary>Gets the message of the name step when the name is refused, or an empty string.</summary>
    public string PetNameError => _petError;

    /// <summary>Gets the heading of the name step: "Name your cat" or "Name your dog".</summary>
    public string PetNameStepTitle => this.Text("Pet_NameStep", _pet.KindNameOf(_chosenKind));

    /// <summary>Gets the text of the button that chooses a pet from its row.</summary>
    public string PetChooseText => this.Text("Pet_Choose");

    /// <summary>Gets the text of the button that goes on from the name step to the confirm question.</summary>
    public string PetNextText => this.Text("Pet_NameNext");

    /// <summary>Gets whether the confirm question is open.</summary>
    public bool PetConfirmAsked => _petStep == PetPurchaseStep.Confirm;

    /// <summary>Gets the title of the confirm box.</summary>
    public string PetConfirmTitle => this.Text("Pet_BuyTitle");

    /// <summary>Gets the question of the confirm box: the kind, the cleaned name and the price.</summary>
    public string PetConfirmMessage => this.Text("Pet_BuyMessage", _pet.KindNameOf(_chosenKind), PetRules.CleanName(_petName), Number(PetRules.PriceOf((byte)_chosenKind)));

    /// <summary>Gets the text of the confirm button.</summary>
    public string PetConfirmText => this.Text("Pet_BuyConfirm");

    /// <summary>Gets the text of the cancel button of the pet purchase.</summary>
    public string PetCancelText => this.Text("Pet_BuyCancel");

    /// <summary>Starts buying a cat or a dog: opens the name step. Nothing is sent.</summary>
    /// <param name="kind">The kind: cat or dog.</param>
    public void ChoosePet(PetKind kind)
    {
        if (this.HasPet || !PetRules.IsAdoptable((byte)kind))
        {
            return;
        }

        _chosenKind = kind;
        _petStep = PetPurchaseStep.Name;
        _petError = string.Empty;
        _pet.ClearStatus();
        this.Raise();
    }

    /// <summary>Starts buying a cat or a dog from its offer row. Nothing is sent.</summary>
    /// <param name="offer">The pet offer row.</param>
    /// <returns>A completed task.</returns>
    public Task ChoosePetAsync(PetOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        this.ChoosePet(offer.Kind);
        return Task.CompletedTask;
    }

    /// <summary>Goes on from the name step to the confirm question, when the name cleans to something. Nothing is sent.</summary>
    public void ContinuePet()
    {
        if (_petStep != PetPurchaseStep.Name)
        {
            return;
        }

        if (PetRules.CleanName(_petName).Length == 0)
        {
            _petError = this.Text("Pet_Status_InvalidName");
            this.Raise();
            return;
        }

        _petError = string.Empty;
        _petStep = PetPurchaseStep.Confirm;
        this.Raise();
    }

    /// <summary>Closes the purchase without sending anything.</summary>
    public void CancelPet()
    {
        if (_petStep == PetPurchaseStep.None)
        {
            return;
        }

        _petStep = PetPurchaseStep.None;
        _chosenKind = PetKind.None;
        _petError = string.Empty;
        this.Raise();
    }

    /// <summary>Confirms the purchase: the only call that sends the adoption, with the cleaned name. The server charges the price and refuses a second pet.</summary>
    public void ConfirmPet()
    {
        if (_petStep != PetPurchaseStep.Confirm || _chosenKind == PetKind.None)
        {
            return;
        }

        PetKind kind = _chosenKind;
        string name = PetRules.CleanName(_petName);
        _petStep = PetPurchaseStep.None;
        _chosenKind = PetKind.None;
        _petError = string.Empty;
        this.Raise();
        _pet.Adopt((byte)kind, name);
    }

    /// <summary>Stops listening to the channels.</summary>
    public void Dispose()
    {
        _buildings.Changed -= this.Raise;
        _buildings.Completed -= this.OnCompleted;
        _wallet.Changed -= this.Raise;
        _inventory.Changed -= this.Raise;
        _pet.Changed -= this.Raise;
        _pet.Completed -= this.OnPetCompleted;
    }

    private static string NicknameRulesNormalize(string typed) => Nosum.Gameplay.Players.NicknameRules.Normalize(typed);

    private void OnPetCompleted(PetStatus status)
    {
        if (status == PetStatus.Ok)
        {
            _petName = string.Empty;
            this.Raise();
        }
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
