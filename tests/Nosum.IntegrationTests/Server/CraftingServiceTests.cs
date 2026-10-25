// Copyright (c) 2026 PPN Systems. All rights reserved.

#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nosum.Abstractions.Crafting;
using Nosum.Contracts.Placeable;
using Nosum.Domain.Inventory;
using Nosum.Gameplay.Crafting;
using Nosum.Gameplay.Items;
using Nosum.Infrastructure.Crafting;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Crafting (docs/adr/ADR-024 D1) against a real PostgreSQL database: the inputs leave and the outputs arrive in one audited change, a refused craft
/// changes nothing, a locked recipe is refused before any item moves, and two crafts racing for the same wood make exactly one chest.
/// </summary>
public sealed class CraftingServiceTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _database;

    public CraftingServiceTests(PostgresFixture database) => _database = database;

    [Fact]
    public async Task Craft_Chest_TakesFiftyWoodAndGivesAChest_AuditedInOneChange()
    {
        using PlacementHarness world = new(_database);
        Guid player = await StandingPlayerAsync(world);
        _ = await world.GiveAsync(player, ItemCatalog.Wood, 60);

        CraftOutcome outcome = await world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.Chest, 1);

        outcome.Should().Be(new CraftOutcome(CraftStatus.Ok, 1));
        (await world.CountAsync(player, ItemCatalog.Wood)).Should().Be(10);
        (await world.CountAsync(player, ItemCatalog.Chest)).Should().Be(1);
        await using Nosum.Infrastructure.Persistence.NosumDbContext db = world.Db();
        List<InventoryLogEntry> log = await db.InventoryLog.Where(e => e.PlayerId == player && e.Reason == "craft.chest").ToListAsync();
        log.Select(e => (e.ItemId, e.Delta)).Should().BeEquivalentTo([(ItemCatalog.Wood, -50), (ItemCatalog.Chest, 1)]);
    }

    [Fact]
    public async Task Craft_Batch_ScalesInputsAndOutputs()
    {
        using PlacementHarness world = new(_database);
        Guid player = await StandingPlayerAsync(world);
        _ = await world.GiveAsync(player, ItemCatalog.Wood, 40);

        CraftOutcome outcome = await world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.WoodFence, 15);

        outcome.Should().Be(new CraftOutcome(CraftStatus.Ok, 15));
        (await world.CountAsync(player, ItemCatalog.Wood)).Should().Be(10);
        (await world.CountAsync(player, ItemCatalog.WoodFence)).Should().Be(15);
    }

    [Fact]
    public async Task Craft_NotEnoughOfOneInput_ChangesNothing()
    {
        using PlacementHarness world = new(_database);
        Guid player = await StandingPlayerAsync(world);
        _ = await world.GiveAsync(player, ItemCatalog.Wood, 60);
        _ = await world.GiveAsync(player, ItemCatalog.Fiber, 19);

        CraftOutcome outcome = await world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.Scarecrow, 1);

        outcome.Should().Be(new CraftOutcome(CraftStatus.NotEnough, 0));
        (await world.CountAsync(player, ItemCatalog.Wood)).Should().Be(60, "the wood was not taken although it was enough");
        (await world.CountAsync(player, ItemCatalog.Fiber)).Should().Be(19);
        (await world.CountAsync(player, ItemCatalog.Scarecrow)).Should().Be(0);
    }

    [Fact]
    public async Task Craft_NoRoomForTheOutput_ChangesNothing()
    {
        using PlacementHarness world = new(_database);
        Guid player = await StandingPlayerAsync(world);
        await FillAllButOneSlotAsync(world, player);
        _ = await world.Inventory.AddAsync(player, ItemCatalog.Stone, 99, "test.setup");
        (await world.Inventory.GetAsync(player)).Slots.Count.Should().Be(36, "the bag is full");

        CraftOutcome outcome = await world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.StonePath, 1);

        // One stone leaves the stack of 99, which frees no slot, so a stone path has nowhere to go.
        outcome.Should().Be(new CraftOutcome(CraftStatus.NoSpace, 0));
        (await world.CountAsync(player, ItemCatalog.Stone)).Should().Be(99);
        (await world.CountAsync(player, ItemCatalog.StonePath)).Should().Be(0);
    }

    [Fact]
    public async Task Craft_InputsThatFreeTheLastSlot_MakeRoomForTheOutput()
    {
        using PlacementHarness world = new(_database);
        Guid player = await StandingPlayerAsync(world);
        await FillAllButOneSlotAsync(world, player);

        // The last slot holds exactly the one stone the path needs: crafting empties it, then the path takes it.
        _ = await world.Inventory.AddAsync(player, ItemCatalog.Stone, 1, "test.setup");
        (await world.Inventory.GetAsync(player)).Slots.Count.Should().Be(36);

        CraftOutcome outcome = await world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.StonePath, 1);

        outcome.Should().Be(new CraftOutcome(CraftStatus.Ok, 1));
        (await world.CountAsync(player, ItemCatalog.Stone)).Should().Be(0);
        (await world.CountAsync(player, ItemCatalog.StonePath)).Should().Be(1);
    }

    [Fact]
    public async Task Craft_LockedRecipe_IsRefusedBeforeAnythingMoves_AndUnlocksWithTheLevel()
    {
        int farming = 1;
        using PlacementHarness world = new(_database, skills: new PlacementHarness.FixedLevels(skill => skill == CraftSkill.Farming ? farming : 0));
        Guid player = await StandingPlayerAsync(world);
        _ = await world.GiveAsync(player, ItemCatalog.CopperBar, 1);
        _ = await world.GiveAsync(player, ItemCatalog.IronBar, 1);

        (await world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.Sprinkler, 1)).Should().Be(new CraftOutcome(CraftStatus.Locked, 0));
        (await world.CountAsync(player, ItemCatalog.CopperBar)).Should().Be(1);
        (await world.Crafting.UnlockedAsync(world.Presence.ConnectionOf(player))).Should().NotContain(RecipeCatalog.Sprinkler).And.Contain(RecipeCatalog.Chest).And.Contain(RecipeCatalog.Scarecrow);

        farming = 2;

        (await world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.Sprinkler, 1)).Should().Be(new CraftOutcome(CraftStatus.Ok, 1));
        (await world.CountAsync(player, ItemCatalog.Sprinkler)).Should().Be(1);
        (await world.CountAsync(player, ItemCatalog.CopperBar)).Should().Be(0);
        (await world.Crafting.UnlockedAsync(world.Presence.ConnectionOf(player))).Should().Contain(RecipeCatalog.Sprinkler);
    }

    [Fact]
    public async Task Craft_FoodRecipes_MakeFoodItems()
    {
        using PlacementHarness world = new(_database);
        Guid player = await StandingPlayerAsync(world);
        _ = await world.GiveAsync(player, ItemCatalog.Radish, 2);
        _ = await world.GiveAsync(player, ItemCatalog.Cauliflower, 1);

        (await world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.Salad, 1)).Status.Should().Be(CraftStatus.Ok);

        (await world.CountAsync(player, ItemCatalog.Salad)).Should().Be(1);
        (await world.CountAsync(player, ItemCatalog.Radish)).Should().Be(1);
        ItemCatalog.TryGet(ItemCatalog.Salad, out ItemDefinition? salad).Should().BeTrue();
        salad!.Kind.Should().Be(ItemKind.Food);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-5)]
    public async Task Craft_BadBatch_IsRefused(int times)
    {
        using PlacementHarness world = new(_database);
        Guid player = await StandingPlayerAsync(world);
        _ = await world.GiveAsync(player, ItemCatalog.Wood, 99);

        (await world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.WoodFence, times)).Status.Should().Be(CraftStatus.InvalidQuantity);
        (await world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), 9999, 1)).Status.Should().Be(CraftStatus.UnknownRecipe);
        (await world.CountAsync(player, ItemCatalog.Wood)).Should().Be(99);
    }

    [Fact]
    public async Task Craft_TwoRequestsForTheSameWood_MakeExactlyOneChest()
    {
        using PlacementHarness world = new(_database);
        Guid player = await StandingPlayerAsync(world);
        _ = await world.GiveAsync(player, ItemCatalog.Wood, 60);

        CraftOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => world.Crafting.CraftAsync(world.Presence.ConnectionOf(player), RecipeCatalog.Chest, 1))));

        outcomes.Count(o => o.Status == CraftStatus.Ok).Should().Be(1);
        outcomes.Where(o => o.Status != CraftStatus.Ok).Should().OnlyContain(o => o.Status == CraftStatus.NotEnough);
        (await world.CountAsync(player, ItemCatalog.Chest)).Should().Be(1);
        (await world.CountAsync(player, ItemCatalog.Wood)).Should().Be(10);
    }

    [Fact]
    public async Task Craft_ForAConnectionThatIsNotSignedIn_IsRefused()
    {
        using PlacementHarness world = new(_database);
        Guid player = await StandingPlayerAsync(world);
        _ = await world.GiveAsync(player, ItemCatalog.Wood, 60);
        Nalix.Abstractions.Networking.IConnection connection = world.Presence.ConnectionOf(player);
        await world.Presence.LeaveAsync(player);

        (await world.Crafting.CraftAsync(connection, RecipeCatalog.Chest, 1)).Should().Be(new CraftOutcome(CraftStatus.UnknownRecipe, 0));
    }

    [Fact]
    public void FixedSkillLevels_AnswersTenForEverySkill()
    {
        FixedSkillLevels levels = new();

        foreach (CraftSkill skill in Enum.GetValues<CraftSkill>())
        {
            levels.LevelOfAsync(Guid.NewGuid(), skill).AsTask().Result.Should().Be(10);
        }
    }

    // Fills every slot but the last with a full stack of fiber (the starter kit already holds eight stacks).
    private static async Task FillAllButOneSlotAsync(PlacementHarness world, Guid player)
    {
        while ((await world.Inventory.GetAsync(player)).Slots.Count < 35)
        {
            _ = await world.Inventory.AddAsync(player, ItemCatalog.Fiber, 99, "test.setup");
        }
    }

    private static async Task<Guid> StandingPlayerAsync(PlacementHarness world)
    {
        Guid player = await world.CreatePlayerAsync();
        world.StandOnFarm(player, 30, 30);
        return player;
    }
}
