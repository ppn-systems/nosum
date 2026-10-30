// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Contracts.Pet;
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
    public void CanAdopt_NoPetOnOwnFarm_IsTrue()
    {
        PetChannel pets = LoadedWith(new FakeGameSocket(), () => new PetState { Status = PetStatus.NoPet });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());

        vm.SetOnOwnFarm(true);

        vm.CanAdopt.Should().BeTrue();
    }

    [Fact]
    public void CanAdopt_NotOnOwnFarm_IsFalse()
    {
        PetChannel pets = LoadedWith(new FakeGameSocket(), () => new PetState { Status = PetStatus.NoPet });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());

        vm.SetOnOwnFarm(false);

        vm.CanAdopt.Should().BeFalse();
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
    public void Adopt_InvalidName_ShowsTheRefusalAndKeepsTheName()
    {
        FakeGameSocket socket = new();
        PetChannel pets = LoadedWith(socket, () => new PetState { Status = PetStatus.NoPet });
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.InvalidName });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());
        vm.Name = "   ";

        vm.Adopt();

        vm.StatusIsGood.Should().BeFalse();
        vm.Status.Should().Be(WasmTestSupport.Text()["Pet_Status_InvalidName"].Value);
    }

    [Fact]
    public void Adopt_Accepted_ClearsTheNameAndShowsSuccess()
    {
        FakeGameSocket socket = new();
        PetChannel pets = LoadedWith(socket, () => new PetState { Status = PetStatus.NoPet });
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.Ok, Kind = 2, Name = "Rex", Mood = 50 });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());
        vm.ChosenKind = (byte)PetKind.Dog;
        vm.Name = "Rex";

        vm.Adopt();

        vm.Name.Should().BeEmpty();
        vm.StatusIsGood.Should().BeTrue();
        vm.HasPet.Should().BeTrue();
    }

    [Fact]
    public void ChosenKind_Changed_RaisesChanged()
    {
        PetChannel pets = LoadedWith(new FakeGameSocket(), () => new PetState { Status = PetStatus.NoPet });
        using PetViewModel vm = new(pets, WasmTestSupport.Text());
        int raised = 0;
        vm.Changed += () => raised++;

        vm.ChosenKind = (byte)PetKind.Dog;

        raised.Should().BeGreaterThan(0);
        vm.ChosenKind.Should().Be((byte)PetKind.Dog);
    }
}
