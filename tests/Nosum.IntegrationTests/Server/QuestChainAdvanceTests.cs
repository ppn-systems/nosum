// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Quest;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Farm;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Quests;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Quests;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The tutorial chain advances on PostgreSQL: a finished step hands the player to the next one in the chain, a partly done counted step keeps the
/// player on it, and the chain ends after its last step (docs/adr/ADR-036 D1-D3).
/// </summary>
public sealed class QuestChainAdvanceTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(s_start);
    private readonly EfUnitOfWorkFactory _units;
    private readonly QuestService _quests;

    public QuestChainAdvanceTests(PostgresFixture database)
    {
        _units = new EfUnitOfWorkFactory(database.Options);
        _quests = new QuestService(_units, _clock);
    }

    // The event that finishes each step, repeated for the counted steps (the same counts docs/adr/ADR-036 names in QuestCatalog).
    private static readonly IReadOnlyDictionary<int, QuestEvent[]> s_steps = new Dictionary<int, QuestEvent[]>
    {
        [QuestCatalog.TillSoil] = [new(QuestEventKind.SoilTilled)],
        [QuestCatalog.WaterCrop] = [new(QuestEventKind.CropWatered)],
        [QuestCatalog.FirstHarvest] = [new(QuestEventKind.CropPlanted, CropCatalog.Parsnip), new(QuestEventKind.CropHarvested, CropCatalog.Parsnip)],
        [QuestCatalog.ShipProduce] = [new(QuestEventKind.ItemShipped, ItemCatalog.Parsnip, 1)],
        [QuestCatalog.BuySeeds] = [new(QuestEventKind.SeedBought, ItemCatalog.ParsnipSeeds, 1)],
        [QuestCatalog.ChopTree] = [new(QuestEventKind.NodeBroken, (int)NodeKind.Tree)],
        [QuestCatalog.CraftChest] = [new(QuestEventKind.ItemCrafted, ItemCatalog.Chest, 1)],
        [QuestCatalog.CraftRod] = [new(QuestEventKind.ItemCrafted, ItemCatalog.FishingRod, 1)],
        [QuestCatalog.CatchFish] = [new(QuestEventKind.FishCaught, ItemCatalog.Carp)],
        [QuestCatalog.MineOre] = [new(QuestEventKind.NodeBroken, (int)NodeKind.CopperOre), new(QuestEventKind.NodeBroken, (int)NodeKind.CopperOre), new(QuestEventKind.NodeBroken, (int)NodeKind.CopperOre)],
        [QuestCatalog.SlaySlime] = [new(QuestEventKind.MonsterDefeated, 1), new(QuestEventKind.MonsterDefeated, 1), new(QuestEventKind.MonsterDefeated, 1)],
        [QuestCatalog.UpgradeTool] = [new(QuestEventKind.ToolUpgraded, ItemCatalog.CopperAxe)],
        [QuestCatalog.BuildCoop] = [new(QuestEventKind.BuildingPlaced, ItemCatalog.CoopBlueprint)],
        [QuestCatalog.BuyAnimal] = [new(QuestEventKind.AnimalBought, 1)],
        [QuestCatalog.CookMeal] = [new(QuestEventKind.ItemCrafted, ItemCatalog.Salad, 1)],
        [QuestCatalog.EatFood] = [new(QuestEventKind.FoodEaten, ItemCatalog.Salad)],
        [QuestCatalog.FillRequest] = [new(QuestEventKind.RequestFilled)],
        [QuestCatalog.Decorate] = [new(QuestEventKind.PlaceablePlaced, ItemCatalog.Chest), new(QuestEventKind.PlaceablePlaced, ItemCatalog.Chest), new(QuestEventKind.PlaceablePlaced, ItemCatalog.Chest)],
        [QuestCatalog.AdoptPet] = [new(QuestEventKind.PetAdopted, 1)],
        [QuestCatalog.SendEmote] = [new(QuestEventKind.EmoteSent)],
    };

    [Fact]
    public async Task CompletingTheFishingRodStep_HandsTheChainToTheFishingStep()
    {
        Guid player = await CreatePlayerAsync(guest: true);
        await CompleteThroughAsync(player, QuestCatalog.CraftRod);
        CurrentStep(await _quests.GetAsync(player)).Should().Be(QuestCatalog.CraftRod);

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.ItemCrafted, ItemCatalog.FishingRod, 1));

        CurrentStep(await _quests.GetAsync(player)).Should().Be(QuestCatalog.CatchFish);
    }

    [Fact]
    public async Task APartlyMinedStep_KeepsThePlayerOnIt_UntilTheWholeCountIsDone()
    {
        Guid player = await CreatePlayerAsync(guest: true);
        await CompleteThroughAsync(player, QuestCatalog.MineOre);

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.NodeBroken, (int)NodeKind.CopperOre));
        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.NodeBroken, (int)NodeKind.CopperOre));
        CurrentStep(await _quests.GetAsync(player)).Should().Be(QuestCatalog.MineOre);

        await _quests.ReportAsync(player, new QuestEvent(QuestEventKind.NodeBroken, (int)NodeKind.CopperOre));
        CurrentStep(await _quests.GetAsync(player)).Should().Be(QuestCatalog.SlaySlime);
    }

    [Fact]
    public async Task WorkingThroughEveryStepInOrder_AdvancesOneStepAtATime_AndEndsAfterTheLast()
    {
        Guid player = await CreatePlayerAsync(guest: true);

        foreach (int questId in QuestCatalog.Chain)
        {
            CurrentStep(await _quests.GetAsync(player)).Should().Be(questId, "the chain shows step {0} next", questId);
            await CompleteAsync(player, questId);
        }

        IReadOnlyList<QuestView> views = await _quests.GetAsync(player);
        CurrentStep(views).Should().BeNull("every step of the chain is done");
        views.Should().OnlyContain(v => v.State == QuestState.Completed);
    }

    // Finishes every step of the chain before the given one, in chain order, so the given step is the one shown.
    private async Task CompleteThroughAsync(Guid player, int questId)
    {
        foreach (int earlier in QuestCatalog.Chain.TakeWhile(id => id != questId))
        {
            await CompleteAsync(player, earlier);
        }
    }

    // Reports the events that finish a step, and checks the step is done afterwards.
    private async Task CompleteAsync(Guid player, int questId)
    {
        s_steps[questId].Should().NotBeEmpty();
        foreach (QuestEvent questEvent in s_steps[questId])
        {
            await _quests.ReportAsync(player, questEvent);
        }

        IReadOnlyList<QuestView> views = await _quests.GetAsync(player);
        views.Single(v => v.QuestId == questId).State.Should().Be(QuestState.Completed, "step {0} is finished", questId);
    }

    // The step the chain shows: the first active quest in chain order, or null when none is left.
    private static int? CurrentStep(IReadOnlyList<QuestView> views)
        => views.Where(v => v.State == QuestState.Active)
            .OrderBy(v => QuestCatalog.PositionOf(v.QuestId))
            .Select(v => (int?)v.QuestId)
            .FirstOrDefault();

    private async Task<Guid> CreatePlayerAsync(bool guest)
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            Player player = TestNicknames.Name(new() { IsGuest = guest, CreatedAt = s_start, LastActiveAt = s_start });
            unit.Repository<Player>().Add(player);
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }
}
