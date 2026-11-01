// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Nosum.Contracts.Pet;
using Nosum.Contracts.Pet.C2S;
using Nosum.Contracts.Pet.S2C;
using Nosum.Gameplay.Pets;
using Nosum.Wasm.Socket.Pet;
using Nosum.Wasm.ViewModels.Pets;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class PetViewModelTests
{
    private static PetChannel LoadedWith(FakeGameSocket socket, Func<PetState> state)
    {
        socket.ReplyWith<PetState>(_ => state());
        PetChannel pets = new(socket);
        pets.Load();
        return pets;
    }

    [Fact]
    public void Type_NoFarmAdoptionForm_ExposesNoAdoptionMembers()
    {
        string[] members = typeof(PetViewModel).GetMembers(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name).ToArray();

        members.Should().NotContain("CanAdopt");
        members.Should().NotContain("SetOnOwnFarm");
        members.Should().NotContain("ChosenKind");
        members.Should().NotContain("Name");
    }

    [Fact]
    public void BuyHintText_NoPet_NamesBothPricesFromTheRules()
    {
        PetChannel pets = LoadedWith(new FakeGameSocket(), () => new PetState { Status = PetStatus.NoPet });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());

        vm.BuyHintText.Should().Contain("600").And.Contain("900");
        vm.HasPet.Should().BeFalse();
    }

    [Fact]
    public void CanPet_PettedToday_IsFalseAndHasPetIsTrue()
    {
        PetChannel pets = LoadedWith(new FakeGameSocket(), () => new PetState { Status = PetStatus.Ok, Kind = 1, Name = "Miu", Mood = 70, PettedToday = true });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());

        vm.HasPet.Should().BeTrue();
        vm.CanPet.Should().BeFalse();
        vm.MoodPercent.Should().Be(70);
    }

    [Fact]
    public void AskRehome_WithPet_OpensTheQuestionAndSendsNothing()
    {
        FakeGameSocket socket = new();
        PetChannel pets = LoadedWith(socket, () => new PetState { Status = PetStatus.Ok, Kind = 2, Name = "Rex", Mood = 50 });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());

        vm.AskRehome();

        vm.RehomeAsked.Should().BeTrue();
        socket.Requests.OfType<RehomePet>().Should().BeEmpty();
    }

    [Fact]
    public void AskRehome_NoPet_DoesNotOpenTheQuestion()
    {
        PetChannel pets = LoadedWith(new FakeGameSocket(), () => new PetState { Status = PetStatus.NoPet });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());

        vm.AskRehome();

        vm.RehomeAsked.Should().BeFalse();
    }

    [Fact]
    public void CancelRehome_AfterAsking_SendsNothingAndClosesTheQuestion()
    {
        FakeGameSocket socket = new();
        PetChannel pets = LoadedWith(socket, () => new PetState { Status = PetStatus.Ok, Kind = 1, Name = "Miu", Mood = 50 });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());
        vm.AskRehome();

        vm.CancelRehome();

        vm.RehomeAsked.Should().BeFalse();
        socket.Requests.OfType<RehomePet>().Should().BeEmpty();
    }

    [Fact]
    public void ConfirmRehome_AfterAsking_SendsRehomeOnce()
    {
        FakeGameSocket socket = new();
        PetChannel pets = LoadedWith(socket, () => new PetState { Status = PetStatus.Ok, Kind = 1, Name = "Miu", Mood = 50 });
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.Ok, Kind = 0 });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());
        vm.AskRehome();

        vm.ConfirmRehome();

        vm.RehomeAsked.Should().BeFalse();
        socket.Requests.OfType<RehomePet>().Should().ContainSingle();
        vm.HasPet.Should().BeFalse();
        vm.StatusIsGood.Should().BeTrue();
    }

    [Fact]
    public void RehomeMessage_WithPet_NamesThePetAndTheRefund()
    {
        PetChannel pets = LoadedWith(new FakeGameSocket(), () => new PetState { Status = PetStatus.Ok, Kind = 2, Name = "Rex", Mood = 50 });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());

        vm.RehomeMessage.Should().Contain("Rex").And.Contain("450");
        vm.RefundCoins.Should().Be(450, "a dog is 900 coins and half of it comes back");
    }

    [Fact]
    public void Adopt_SendsTheKindAndTheTrimmedName()
    {
        FakeGameSocket socket = new();
        PetChannel pets = LoadedWith(socket, () => new PetState { Status = PetStatus.NoPet });
        string name = string.Empty;
        socket.ReplyWith<PetState>(request =>
        {
            name = ((AdoptPet)request).Name;
            return new PetState { Status = PetStatus.Ok, Kind = 2, Name = "Rex", Mood = 50 };
        });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());

        vm.Adopt((byte)PetKind.Dog, "  Rex ");

        name.Should().Be("Rex");
        vm.HasPet.Should().BeTrue();
    }

    [Fact]
    public void Adopt_ServerRefusesNotAtShop_ShowsTheCounterText()
    {
        FakeGameSocket socket = new();
        PetChannel pets = LoadedWith(socket, () => new PetState { Status = PetStatus.NoPet });
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.NotAtShop });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());

        vm.Adopt((byte)PetKind.Cat, "Miu");

        vm.StatusIsGood.Should().BeFalse();
        vm.Status.Should().Be(WasmTestSupport.Text()["Pet_Status_NotAtShop"].Value);
    }

    [Fact]
    public void Adopt_ServerRefusesNotEnoughCoins_ShowsTheCoinsText()
    {
        FakeGameSocket socket = new();
        PetChannel pets = LoadedWith(socket, () => new PetState { Status = PetStatus.NoPet });
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.NotEnoughCoins });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());

        vm.Adopt((byte)PetKind.Dog, "Rex");

        vm.StatusIsGood.Should().BeFalse();
        vm.Status.Should().Be(WasmTestSupport.Text()["Pet_Status_NotEnoughCoins"].Value);
    }

    [Fact]
    public void Adopt_Completed_RaisesTheStatusForTheCarpenter()
    {
        FakeGameSocket socket = new();
        PetChannel pets = LoadedWith(socket, () => new PetState { Status = PetStatus.NoPet });
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.Ok, Kind = 1, Name = "Miu", Mood = 50 });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());
        PetStatus? raised = null;
        vm.Completed += status => raised = status;

        vm.Adopt((byte)PetKind.Cat, "Miu");

        raised.Should().Be(PetStatus.Ok);
    }

    [Fact]
    public void Changed_OfThePetState_RaisesChanged()
    {
        PetChannel pets = LoadedWith(new FakeGameSocket(), () => new PetState { Status = PetStatus.NoPet });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());
        int raised = 0;
        vm.Changed += () => raised++;

        pets.Load();

        raised.Should().BeGreaterThan(0);
    }
}
