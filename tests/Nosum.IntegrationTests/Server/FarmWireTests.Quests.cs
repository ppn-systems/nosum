// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Hosting;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.S2C;
using Nosum.Gameplay.Quests;
using Nosum.Host;
using Xunit;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The farming quests reach the farmer as pushed updates (the client's quest card follows the push, not the next log request).
/// </summary>
public sealed partial class FarmWireTests
{
    [Fact]
    public async Task Watering_APlantedSeed_PushesTheWaterQuestAsCompleted()
    {
        await ResetFarmAsync();
        FakeTimeProvider clock = new(s_start);
        ushort port = GetFreePort();
        HostServices.Register(_database.Options);
        TestAccounts.UseFakeVerifiers(_database.Options);
        await using NetworkApplication app = Program.BuildApplication(port, clock);
        using CancellationTokenSource timeout = new(s_timeout);
        await app.ActivateAsync(timeout.Token);
        try
        {
            await using FarmClient farmer = await FarmClient.ConnectAsync(port, timeout.Token);
            JoinMapResult join = await farmer.JoinAsync(timeout.Token);
            (int x, int y) = TileInReachOf(join);

            // Till, plant a parsnip, then water it: each step is pushed to the farmer as its own quest update.
            (await farmer.UseAsync(0, x, y, timeout.Token)).Status.Should().Be(FarmStatus.Ok);
            (await farmer.UseAsync(2, x, y, timeout.Token)).Action.Should().Be(FarmAction.Planted);
            QuestRow planted = await farmer.WaitForQuestAsync(q => q.QuestId == QuestCatalog.FirstHarvest && q.Progress.Count > 0 && q.Progress[0] == 1, timeout.Token);
            planted.State.Should().Be(QuestState.Active);

            (await farmer.UseAsync(1, x, y, timeout.Token)).Action.Should().Be(FarmAction.Watered);

            // The water quest is completed on the server and the completion is pushed, not only visible in the quest log.
            QuestRow watered = await farmer.WaitForQuestAsync(q => q.QuestId == QuestCatalog.WaterCrop && q.State == QuestState.Completed, timeout.Token);
            watered.Progress.Should().Equal(1);
        }
        finally
        {
            await app.DeactivateAsync(CancellationToken.None);
        }
    }
}
