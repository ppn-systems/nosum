// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Pet;
using Nosum.Contracts.Pet.C2S;
using Nosum.Contracts.Pet.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Pets;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Pet;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class PetChannelTests
{
    [Fact]
    public void Load_ServerAnswer_StoresThePetAndMarksLoaded()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith<PetState>(_ => new PetState { Status = PetStatus.Ok, Kind = 1, Name = "Miu", Mood = 60, PettedToday = true });
        PetChannel pets = new(socket);

        pets.Load();

        pets.IsLoaded.Should().BeTrue();
        pets.Current.Kind.Should().Be(PetKind.Cat);
        pets.Current.Name.Should().Be("Miu");
        pets.Current.Mood.Should().Be(60);
        pets.Current.PettedToday.Should().BeTrue();
    }

    [Fact]
    public void Adopt_SendsTheKindAndTheName()
    {
        FakeGameSocket socket = new();
        byte kind = 0;
        string name = string.Empty;
        socket.ReplyWith<PetState>(request =>
        {
            kind = ((AdoptPet)request).Kind;
            name = ((AdoptPet)request).Name;
            return new PetState { Status = PetStatus.Ok, Kind = 2, Name = "Rex", Mood = 50 };
        });
        PetChannel pets = new(socket);

        pets.Adopt(2, "Rex");

        kind.Should().Be(2);
        name.Should().Be("Rex");
        pets.Current.HasPet.Should().BeTrue();
    }

    [Fact]
    public void LookChannel_PushedLookWithPet_CarriesThePetKindAndName()
    {
        FakeGameSocket socket = new();
        using LookChannel looks = new(socket, () => 1);
        using (LookChanged push = new() { Look = new LookRow { PlayerId = 5, PetKind = 2, PetName = "Rex" } })
        {
            socket.Push(push);
        }

        looks.TryGet(5, out LookInfo look).Should().BeTrue();
        look.PetKind.Should().Be((byte)PetKind.Dog);
        look.PetName.Should().Be("Rex");
    }
}
