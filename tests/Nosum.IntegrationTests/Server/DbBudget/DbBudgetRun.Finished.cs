// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nosum.Abstractions.Quests;
using Nosum.Abstractions.Shops;
using Nosum.Contracts.Emote;
using Nosum.Contracts.Emote.C2S;
using Nosum.Contracts.Emote.S2C;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Requests;
using Nosum.Contracts.Requests.C2S;
using Nosum.Contracts.Requests.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Quests;
using Nosum.Infrastructure.Quests;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // The three hot paths that a quest step touches, measured for a player whose tutorial chain is finished (docs/adr/ADR-035 Amendment).
    // Every quest is completed, so the quest service counts nothing and writes nothing: these rows keep their original limits.
    private async Task FinishedChainAsync()
    {
        (double x, double y) = (1032, 264);
        await using (BudgetPlayer p = await this.FreshAsync(GameMaps.Farm, x, y, 0, (ItemCatalog.Chest, 1)))
        {
            await this.FinishChainAsync(p);
            _ = await this.PlaceAsync(p, ItemCatalog.Chest, "a chest, chain finished");
            _ = await _rig.ProbeAsync<EmoteResult>(p, "Emote", EmoteOpCode.SendEmote, "SendEmote", "wave, chain finished", () => new SendEmote { Emote = 2 });
        }

        (double tx, double ty) = TileCentre(42, 57);
        await using BudgetPlayer t = await this.FreshAsync(GameMaps.Town, tx, ty, 0);
        await this.FinishChainAsync(t);
        RequestsSnapshot board = await _rig.ProbeAsync<RequestsSnapshot>(t, "Requests", RequestsOpCode.GetRequests, "GetRequests", "today's board, chain finished", () => new GetRequests());
        RequestRow? first = board.Rows.FirstOrDefault();
        if (first is null)
        {
            this.Failures.Add("finished chain: the board has no row today");
            return;
        }

        _ = await BudgetRig.Service<IDevGrantService>().GrantAsync(t.AccountId, first.ItemId, first.Quantity, 0, _rig.Timeout);
        int slot = first.Slot;
        int day = board.Day;
        _ = await _rig.ProbeAsync<RequestResult>(t, "Requests", RequestsOpCode.DeliverRequest, "DeliverRequest", "fill one request, chain finished", () => new DeliverRequest { Day = day, Slot = slot });
    }

    // Completes every step of the chain through the real quest service: each objective is reported as often as its count, so rewards are paid
    // and the player's copy stays equal to the rows. Nothing here is measured.
    private async Task FinishChainAsync(BudgetPlayer player)
    {
        QuestService quests = (QuestService)BudgetRig.Service<IQuestService>();
        foreach (int questId in QuestCatalog.Chain)
        {
            _ = QuestCatalog.TryGet(questId, out QuestDefinition? quest);
            for (int i = 0; i < quest!.ObjectiveCount; i++)
            {
                QuestObjective objective = quest.ObjectiveAt(i);
                for (int n = 0; n < objective.Count; n++)
                {
                    await quests.ReportAsync(player.AccountId, new QuestEvent(objective.Kind, objective.SubjectId, 1));
                }
            }
        }

        IReadOnlyList<QuestView> views = await quests.GetAsync(player.AccountId);
        if (views.Any(v => v.State != QuestState.Completed))
        {
            this.Failures.Add("finished chain: a step is still active after completing every objective");
        }
    }
}
