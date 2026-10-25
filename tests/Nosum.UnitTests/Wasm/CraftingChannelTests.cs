// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Gameplay.Crafting;
using Nosum.Wasm.Socket.Crafting;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class CraftingChannelTests
{
    [Fact]
    public void Load_StoresTheUnlockedRecipes()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new RecipeBook { Unlocked = [RecipeCatalog.Chest, RecipeCatalog.Sprinkler] });
        CraftingChannel channel = new(socket);
        int changed = 0;
        channel.Changed += () => changed++;

        channel.Load();

        channel.IsLoaded.Should().BeTrue();
        channel.IsUnlocked(RecipeCatalog.Chest).Should().BeTrue();
        channel.IsUnlocked(RecipeCatalog.Sprinkler).Should().BeTrue();
        channel.IsUnlocked(RecipeCatalog.Furnace).Should().BeFalse();
        changed.Should().Be(1);
    }

    [Fact]
    public void IsUnlocked_BeforeTheBookWasRead_IsFalse()
    {
        CraftingChannel channel = new(new FakeGameSocket());

        channel.IsLoaded.Should().BeFalse();
        channel.IsUnlocked(RecipeCatalog.Chest).Should().BeFalse();
    }

    [Fact]
    public void Craft_SendsTheRecipeAndCount_AndReportsTheServersAnswer()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new CraftResult { Status = CraftStatus.NotEnough });
        CraftingChannel channel = new(socket);
        List<CraftStatus> answers = [];
        channel.Completed += answers.Add;

        channel.Craft(RecipeCatalog.WoodFence, 5);

        Craft request = (Craft)socket.Requests[0];
        (request.RecipeId, request.Times).Should().Be(((ushort)RecipeCatalog.WoodFence, (byte)5));
        answers.Should().Equal(CraftStatus.NotEnough);
    }
}
