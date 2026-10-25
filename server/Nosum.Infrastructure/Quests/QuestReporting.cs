// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Quests;
using Nosum.Gameplay.Quests;

namespace Nosum.Infrastructure.Quests;

/// <summary>How a service that already decided an action tells the quests about it (docs/adr/ADR-036 D3).</summary>
internal static class QuestReporting
{
    /// <summary>Reports what a player did after the action was committed. Does nothing without a quest service; a failure is swallowed.</summary>
    /// <param name="quests">The quest service, or <see langword="null"/>.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="questEvent">What happened.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when progress was counted or the report was dropped.</returns>
    [SuppressMessage("Design", "CA1031", Justification = "The action is committed; a failing quest update must not turn it into an error reply, and the next matching action counts again.")]
    public static async Task TryReportAsync(this IQuestService? quests, Guid playerId, QuestEvent questEvent, CancellationToken cancellationToken)
    {
        if (quests is null)
        {
            return;
        }

        try
        {
            await quests.ReportAsync(playerId, questEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // See the suppression.
        }
    }
}
