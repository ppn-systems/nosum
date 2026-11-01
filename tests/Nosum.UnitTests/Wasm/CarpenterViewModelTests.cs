// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Building;
using Nosum.Contracts.Building.C2S;
using Nosum.Contracts.Building.S2C;
using Nosum.Contracts.Market.S2C;
using Nosum.Gameplay.Animals;
using Nosum.Gameplay.Buildings;
using Nosum.Contracts.Pet;
using Nosum.Contracts.Pet.C2S;
using Nosum.Contracts.Pet.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Pets;
using Nosum.Wasm.Socket.Building;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Pet;
using Nosum.Wasm.ViewModels.Building;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Pets;
using Xunit;
using static Nosum.UnitTests.Wasm.WasmTestSupport;

namespace Nosum.UnitTests.Wasm;

/// <summary>The carpenter's building and animal sections and the messages of tending animals (docs/adr/ADR-028).</summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class CarpenterViewModelTests
{
    private static readonly Guid s_coop = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static (CarpenterViewModel Vm, FakeGameSocket Socket, BuildingChannel Buildings) Create(long coins, params (int Slot, int ItemId, int Quantity)[] stacks)
    {
        FakeGameSocket socket = new();
        InventoryChannel inventory = LoadedInventory(socket, stacks);
        WalletChannel wallet = new(socket);
        socket.Push(new WalletUpdate { Coins = coins });
        BuildingChannel buildings = new(socket);
        PetViewModel pet = new(new PetChannel(socket), Text());
        return (new CarpenterViewModel(buildings, wallet, inventory, pet, Text()), socket, buildings);
    }

    private static (CarpenterViewModel Vm, FakeGameSocket Socket, PetChannel Pets) CreateWithPets(long coins)
    {
        FakeGameSocket socket = new();
        InventoryChannel inventory = LoadedInventory(socket);
        WalletChannel wallet = new(socket);
        socket.Push(new WalletUpdate { Coins = coins });
        PetChannel pets = new(socket);
        PetViewModel pet = new(pets, Text());
        return (new CarpenterViewModel(new BuildingChannel(socket), wallet, inventory, pet, Text()), socket, pets);
    }

    private static void ListMine(FakeGameSocket socket, BuildingChannel buildings, params BuildingRow[] rows)
    {
        socket.ReplyWith(_ =>
        {
            BuildingsSnapshot snapshot = new() { Mine = true };
            snapshot.Buildings.AddRange(rows);
            return snapshot;
        });
        buildings.LoadMine();
    }

    [Fact]
    public void Orders_OnlyWhatThePlayerCanAfford_CanBeOrdered()
    {
        (CarpenterViewModel vm, _, _) = Create(4_000, (0, ItemCatalog.Wood, 99), (1, ItemCatalog.Wood, 99), (2, ItemCatalog.Wood, 99), (3, ItemCatalog.Wood, 3), (4, ItemCatalog.Stone, 99), (5, ItemCatalog.Stone, 5));

        BuildingOrder coop = vm.Orders.Single(o => o.Kind == BuildingKind.Coop);
        BuildingOrder barn = vm.Orders.Single(o => o.Kind == BuildingKind.Barn);

        coop.CanOrder.Should().BeTrue("4000 coins, 300 wood and 104 stone cover the coop");
        barn.CanOrder.Should().BeFalse("6000 coins are needed");
    }

    [Theory]
    [InlineData(3_999, 300, 100)]
    [InlineData(4_000, 299, 100)]
    [InlineData(4_000, 300, 99)]
    public void Orders_ShortOfCoinsWoodOrStone_CannotOrder(long coins, int wood, int stone)
    {
        (CarpenterViewModel vm, _, _) = Create(coins, (0, ItemCatalog.Wood, wood), (1, ItemCatalog.Stone, stone));

        vm.Orders.Single(o => o.Kind == BuildingKind.Coop).CanOrder.Should().BeFalse();
    }

    [Fact]
    public void Orders_ShowTheNamesAndPricesInBothLanguages()
    {
        (CarpenterViewModel vm, _, _) = Create(0);

        using (CultureScope scope = new("en"))
        {
            BuildingOrder coop = vm.Orders.Single(o => o.Kind == BuildingKind.Coop);
            (coop.Name, coop.CostText).Should().Be(("Coop", "4,000 coins · 300 wood · 100 stone"));
        }

        using (CultureScope scope = new("vi"))
        {
            vm.Orders.Single(o => o.Kind == BuildingKind.Barn).Name.Should().Be("Chuồng bò");
        }
    }

    [Fact]
    public void Order_SendsTheKind_AndAnAnswerShowsItsText()
    {
        using CultureScope scope = new("en");
        (CarpenterViewModel vm, FakeGameSocket socket, _) = Create(4_000, (0, ItemCatalog.Wood, 99), (1, ItemCatalog.Stone, 99));
        socket.ReplyWith(_ => new BuildingResult { Status = BuildingStatus.NotEnoughCoins });

        vm.Order(BuildingKind.Coop);

        socket.Requests.OfType<CommissionBuilding>().Single().Kind.Should().Be((byte)BuildingKind.Coop);
        vm.Status.Should().Be("You do not have enough coins.");
        vm.StatusIsGood.Should().BeFalse();

        socket.ReplyWith(_ => new BuildingResult { Status = BuildingStatus.Ok, ItemId = ItemCatalog.CoopBlueprint });
        vm.Order(BuildingKind.Coop);

        vm.Status.Should().Be("The blueprint is in your bag. Set it down on the ground.");
        vm.StatusIsGood.Should().BeTrue();
    }

    [Fact]
    public void Offers_OnlyFinishedBuildingsWithRoom_AreListedWithTheirSpeciesAndPrice()
    {
        using CultureScope scope = new("en");
        (CarpenterViewModel vm, FakeGameSocket socket, BuildingChannel buildings) = Create(1_000);
        Guid barn = Guid.Parse("22222222-2222-2222-2222-222222222222");
        ListMine(
            socket,
            buildings,
            new BuildingRow { Id = s_coop, ObjectId = 1, Kind = (byte)BuildingKind.Coop, MapId = "farm", RemainingMs = 0, Animals = 3, Capacity = 4 },
            new BuildingRow { Id = barn, ObjectId = 2, Kind = (byte)BuildingKind.Barn, MapId = "farm", RemainingMs = 5_000, Animals = 0, Capacity = 4 },
            new BuildingRow { Id = Guid.NewGuid(), ObjectId = 3, Kind = (byte)BuildingKind.Coop, MapId = "farm", RemainingMs = 0, Animals = 4, Capacity = 4 });

        // The barn is still being built and the other coop is full: only this coop's two species are offered.
        AnimalOffer[] offers = vm.Offers.ToArray();
        offers.Should().HaveCount(2, "a coop takes chickens and ducks");
        offers.Should().OnlyContain(o => o.BuildingId == s_coop && o.Title == "Coop (3/4)");

        AnimalOffer offer = offers.Single(o => o.Kind == AnimalKind.Chicken);
        offer.BuyLabel.Should().Be("Buy Chicken · 800 coins");
        offer.CanBuy.Should().BeTrue();
        offers.Should().Contain(o => o.Kind == AnimalKind.Duck && !o.CanBuy, "a duck costs 1 100 and the wallet holds 1 000");
    }

    [Fact]
    public void Offers_WithTooFewCoins_CannotBeBought()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, BuildingChannel buildings) = Create(799);
        ListMine(socket, buildings, new BuildingRow { Id = s_coop, ObjectId = 1, Kind = (byte)BuildingKind.Coop, MapId = "farm", Capacity = 4 });

        vm.Offers.Should().OnlyContain(o => !o.CanBuy, "neither a chicken (800) nor a duck (1 100) is affordable with 799 coins");
    }

    [Fact]
    public void BuyAnimal_WithAValidName_SendsItTrimmed_AndClearsTheName()
    {
        using CultureScope scope = new("en");
        (CarpenterViewModel vm, FakeGameSocket socket, BuildingChannel buildings) = Create(1_000);
        ListMine(socket, buildings, new BuildingRow { Id = s_coop, ObjectId = 1, Kind = (byte)BuildingKind.Coop, MapId = "farm", Capacity = 4 });
        socket.ReplyWith(_ => new BuildingResult { Status = BuildingStatus.Ok, Id = Guid.NewGuid() });
        vm.AnimalName = "  Bella ";

        vm.BuyAnimal(s_coop, AnimalKind.Chicken);

        BuyAnimal request = socket.Requests.OfType<BuyAnimal>().Single();
        (request.BuildingId, request.Kind, request.Name).Should().Be((s_coop, (byte)AnimalKind.Chicken, "Bella"));
        // The notice names no building: a chicken goes to a coop, a cow to a barn (docs/adr/ADR-028 amendment 3).
        vm.Status.Should().Be("Bought, and on its way to its new home.");
        vm.AnimalName.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("Bad!Name")]
    public void BuyAnimal_WithAnInvalidName_SendsNothingAndSaysWhy(string name)
    {
        using CultureScope scope = new("en");
        (CarpenterViewModel vm, FakeGameSocket socket, _) = Create(1_000);
        vm.AnimalName = name;

        vm.BuyAnimal(s_coop, AnimalKind.Chicken);

        socket.Requests.OfType<BuyAnimal>().Should().BeEmpty();
        vm.Status.Should().Be("That name is not allowed: 3 to 16 characters, no rude words.");
    }

    [Fact]
    public void ATendingAnswer_IsNotShownOnTheCarpentersPage()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, _) = Create(0);
        socket.ReplyWith(_ => new AnimalResult { Status = BuildingStatus.AlreadyPetted });
        BuildingChannel buildings = new(socket);
        _ = new CarpenterViewModel(buildings, new WalletChannel(socket), LoadedInventory(socket), new PetViewModel(new PetChannel(socket), Text()), Text());

        buildings.Pet(Guid.NewGuid());

        vm.Status.Should().BeEmpty();
    }

    [Fact]
    public void EveryBuildingStatus_AndEveryBuildingAndAnimal_HasATextInBothLanguages()
    {
        string[] needed =
        [
            .. Enum.GetValues<BuildingStatus>().Where(s => s != BuildingStatus.Ok).Select(s => "Building_Status_" + s),
            .. BuildingCatalog.All.Select(b => "Building_" + b.Key + "_Name"),
            .. AnimalCatalog.All.Select(a => "Animal_" + a.Key + "_Name"),
            "Shop_Title_carpenter_shop",
            "Building_Done_Commission",
            "Building_Done_BuyAnimal",
            "Building_Done_Feed",
            "Building_Done_Pet",
            "Building_Done_Collect",
            "Place_Status_Fixed",
        ];

        Keys("Language.resx").Should().Contain(needed);
        Keys("Language.en.resx").Should().Contain(needed);
    }

    [Fact]
    public void PetOffers_EnoughCoinsAndNoPet_CanBuyCatAndDog()
    {
        (CarpenterViewModel vm, _, _) = CreateWithPets(1_000);

        vm.PetOffers.Should().HaveCount(2);
        vm.PetOffers.Should().OnlyContain(o => o.CanBuy);
    }

    [Fact]
    public void PetOffers_NotEnoughCoinsForADog_OnlyTheCatCanBeBought()
    {
        (CarpenterViewModel vm, _, _) = CreateWithPets(700);

        vm.PetOffers.Single(o => o.Kind == PetKind.Cat).CanBuy.Should().BeTrue();
        vm.PetOffers.Single(o => o.Kind == PetKind.Dog).CanBuy.Should().BeFalse();
    }

    [Fact]
    public void PetOffers_AlreadyHasAPet_NothingCanBeBought()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, PetChannel pets) = CreateWithPets(5_000);
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.Ok, Kind = 1, Name = "Miu", Mood = 50 });
        pets.Load();

        vm.HasPet.Should().BeTrue();
        vm.PetOffers.Should().OnlyContain(o => !o.CanBuy);
    }

    [Fact]
    public void ChoosePet_OpensTheNameStep_AndSendsNothing()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, _) = CreateWithPets(5_000);

        vm.ChoosePet(PetKind.Dog);

        vm.PetStep.Should().Be(PetPurchaseStep.Name);
        vm.ChosenPetKind.Should().Be(PetKind.Dog);
        vm.PetConfirmAsked.Should().BeFalse();
        socket.Requests.OfType<AdoptPet>().Should().BeEmpty();
    }

    [Fact]
    public void ChoosePet_WhenThePlayerAlreadyHasAPet_DoesNothing()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, PetChannel pets) = CreateWithPets(5_000);
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.Ok, Kind = 1, Name = "Miu", Mood = 50 });
        pets.Load();

        vm.ChoosePet(PetKind.Dog);

        vm.PetStep.Should().Be(PetPurchaseStep.None);
        socket.Requests.OfType<AdoptPet>().Should().BeEmpty();
    }

    [Fact]
    public void ContinuePet_BlankName_SendsNothingAndAsksForAName()
    {
        using CultureScope scope = new("en");
        (CarpenterViewModel vm, FakeGameSocket socket, _) = CreateWithPets(5_000);
        vm.ChoosePet(PetKind.Cat);
        vm.PetName = "   ";

        vm.ContinuePet();

        vm.PetStep.Should().Be(PetPurchaseStep.Name);
        vm.PetNameError.Should().Be("Give it a name (up to 12 characters).");
        socket.Requests.OfType<AdoptPet>().Should().BeEmpty();
    }

    [Fact]
    public void ContinuePet_WithAName_OpensTheConfirmQuestion_WithKindNameAndPrice_AndSendsNothing()
    {
        using CultureScope scope = new("en");
        (CarpenterViewModel vm, FakeGameSocket socket, _) = CreateWithPets(5_000);
        vm.ChoosePet(PetKind.Dog);
        vm.PetName = "Rex";

        vm.ContinuePet();

        vm.PetStep.Should().Be(PetPurchaseStep.Confirm);
        vm.PetConfirmAsked.Should().BeTrue();
        vm.PetConfirmMessage.Should().Be("Buy a Dog named Rex for 900 coins?");
        vm.PetNameError.Should().BeEmpty();
        socket.Requests.OfType<AdoptPet>().Should().BeEmpty();
    }

    [Fact]
    public void CancelPet_AtTheNameStep_SendsNothingAndClosesThePurchase()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, _) = CreateWithPets(5_000);
        vm.ChoosePet(PetKind.Cat);

        vm.CancelPet();

        vm.PetStep.Should().Be(PetPurchaseStep.None);
        vm.ChosenPetKind.Should().Be(PetKind.None);
        socket.Requests.OfType<AdoptPet>().Should().BeEmpty();
    }

    [Fact]
    public void CancelPet_AtTheConfirmQuestion_SendsNothingAndClosesThePurchase()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, _) = CreateWithPets(5_000);
        vm.ChoosePet(PetKind.Cat);
        vm.PetName = "Miu";
        vm.ContinuePet();

        vm.CancelPet();

        vm.PetConfirmAsked.Should().BeFalse();
        vm.PetStep.Should().Be(PetPurchaseStep.None);
        socket.Requests.OfType<AdoptPet>().Should().BeEmpty();
    }

    [Fact]
    public void ConfirmPet_WithoutTheConfirmQuestion_SendsNothing()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, _) = CreateWithPets(5_000);
        vm.ChoosePet(PetKind.Dog);
        vm.PetName = "Rex";

        vm.ConfirmPet();

        socket.Requests.OfType<AdoptPet>().Should().BeEmpty();
        vm.PetStep.Should().Be(PetPurchaseStep.Name);
    }

    [Fact]
    public void ConfirmPet_SendsTheKindAndTheCleanedName_Once()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, _) = CreateWithPets(5_000);
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.Ok, Kind = 1, Name = "Abcdefghijkl", Mood = 50 });
        vm.ChoosePet(PetKind.Cat);
        vm.PetName = "  Abcdefghijklmnop ";
        vm.ContinuePet();

        vm.ConfirmPet();
        vm.ConfirmPet();

        AdoptPet request = socket.Requests.OfType<AdoptPet>().Should().ContainSingle().Subject;
        request.Kind.Should().Be((byte)PetKind.Cat);
        request.Name.Should().Be(PetRules.CleanName("  Abcdefghijklmnop "));
        request.Name.Length.Should().Be(PetRules.MaxNameLength);
        vm.PetStep.Should().Be(PetPurchaseStep.None);
    }

    [Fact]
    public void ConfirmPet_Accepted_ClearsThePetNameAndShowsSuccess()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, _) = CreateWithPets(5_000);
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.Ok, Kind = 1, Name = "Miu", Mood = 50 });
        vm.ChoosePet(PetKind.Cat);
        vm.PetName = "Miu";
        vm.ContinuePet();

        vm.ConfirmPet();

        vm.PetName.Should().BeEmpty();
        vm.PetStatusIsGood.Should().BeTrue();
    }

    [Fact]
    public void ConfirmPet_NotEnoughCoins_ShowsTheRefusalAndKeepsTheName()
    {
        (CarpenterViewModel vm, FakeGameSocket socket, _) = CreateWithPets(100);
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.NotEnoughCoins });
        vm.ChoosePet(PetKind.Dog);
        vm.PetName = "Rex";
        vm.ContinuePet();

        vm.ConfirmPet();

        vm.PetStatusIsGood.Should().BeFalse();
        vm.PetStatusText.Should().Be(Text()["Pet_Status_NotEnoughCoins"].Value);
        vm.PetName.Should().Be("Rex");
    }
}


/// <summary>The one-line messages of tending animals.</summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the fake socket owns nothing, pooled packets and channels die with the test.")]
public sealed class AnimalCareViewModelTests
{
    [Theory]
    [InlineData("en", BuildingStatus.NeedsPail, "Hold the milk pail to milk a cow.")]
    [InlineData("en", BuildingStatus.NotYours, "This is not your building. Visitors may only look.")]
    [InlineData("vi", BuildingStatus.AlreadyPetted, "Hôm nay bạn đã vuốt ve rồi.")]
    public void ARefusal_GoesToTheMessageLine(string culture, BuildingStatus status, string expected)
    {
        using CultureScope scope = new(culture);
        (FakeGameSocket socket, BuildingChannel buildings, GameMessagesViewModel messages) = Create();
        socket.ReplyWith(_ => new AnimalResult { Status = status });

        buildings.Collect(Guid.NewGuid(), 0);

        messages.Message.Should().Be(expected);
    }

    [Fact]
    public void ACollectedEgg_NamesTheItem_APettedAnimalItsNameAndFriendship()
    {
        using CultureScope scope = new("en");
        (FakeGameSocket socket, BuildingChannel buildings, GameMessagesViewModel messages) = Create();
        Guid hen = Guid.NewGuid();
        socket.ReplyWith(_ =>
        {
            AnimalsSnapshot room = new() { BuildingId = Guid.NewGuid(), Kind = (byte)BuildingKind.Coop, MayTend = true };
            room.Animals.Add(new AnimalRow { Id = hen, Kind = (byte)AnimalKind.Chicken, Name = "Bella" });
            return room;
        });
        buildings.LoadRoom();
        socket.ReplyWith(_ => new AnimalResult { Status = BuildingStatus.Ok, ItemId = ItemCatalog.LargeEgg, Quantity = 1, Friendship = 80 });

        buildings.Collect(hen, 0);
        messages.Message.Should().Be("You got Large egg.");
        buildings.Pet(hen);

        messages.Message.Should().Be("You pet Bella. Friendship: 80.");
    }

    [Fact]
    public void TheCarpentersAnswers_AreNotMessagesOfTheRoom()
    {
        (FakeGameSocket socket, BuildingChannel buildings, GameMessagesViewModel messages) = Create();
        socket.ReplyWith(_ => new BuildingResult { Status = BuildingStatus.NotEnoughCoins });

        buildings.Commission((byte)BuildingKind.Coop);

        messages.Message.Should().BeNullOrEmpty();
    }

    private sealed class NoIcons : Nosum.Wasm.ViewModels.Inventory.IItemIconSource
    {
        public bool TryGet(int itemId, out Nosum.Wasm.ViewModels.Inventory.ItemIcon icon)
        {
            icon = default!;
            return false;
        }
    }

    private static (FakeGameSocket Socket, BuildingChannel Buildings, GameMessagesViewModel Messages) Create()
    {
        FakeGameSocket socket = new();
        BuildingChannel buildings = new(socket);
        Nosum.Wasm.Socket.Farm.FarmChannel farm = new(socket);
        Nosum.Wasm.Socket.Placeable.PlaceableChannel placeables = new(socket);
        Nosum.Wasm.ViewModels.Inventory.InventoryViewModel inventory = new(new InventoryChannel(socket), new NoIcons(), Localizer<Nosum.Wasm.ViewModels.Resources.Catalogue>(), Text());
        GameMessagesViewModel messages = new(farm, placeables, inventory, Text());
        _ = new AnimalCareViewModel(buildings, messages, Describer(), Text());
        return (socket, buildings, messages);
    }
}
