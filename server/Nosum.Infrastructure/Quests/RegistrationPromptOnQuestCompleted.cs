// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Account.S2C;
using Nosum.Gameplay.Quests;

namespace Nosum.Infrastructure.Quests;

/// <summary>
/// Completing the first quest unlocks the invitation to register for a guest
/// (<see cref="IRegistrationPromptService"/>) and tells the client right away.
/// </summary>
public sealed class RegistrationPromptOnQuestCompleted : IDisposable
{
    private readonly IQuestService _quests;
    private readonly IRegistrationPromptService _prompts;
    private readonly IPlayerNotifier _notifier;

    /// <summary>Starts listening to quest completions.</summary>
    /// <param name="quests">The quest service.</param>
    /// <param name="prompts">The registration prompt hook.</param>
    /// <param name="notifier">Pushes to the player.</param>
    public RegistrationPromptOnQuestCompleted(IQuestService quests, IRegistrationPromptService prompts, IPlayerNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(notifier);

        _quests = quests;
        _prompts = prompts;
        _notifier = notifier;
        _quests.Completed += this.OnCompletedAsync;
    }

    /// <inheritdoc/>
    public void Dispose() => _quests.Completed -= this.OnCompletedAsync;

    private async ValueTask OnCompletedAsync(QuestCompletion completion)
    {
        if (completion.QuestId != QuestCatalog.FirstHarvest)
        {
            return;
        }

        // False for a registered player or when it was unlocked before: nothing new to tell the client.
        if (!await _prompts.UnlockAsync(completion.PlayerId, default).ConfigureAwait(false))
        {
            return;
        }

        using PacketScope<RegistrationPromptUnlocked> lease = PacketFactory<RegistrationPromptUnlocked>.Acquire();
        _ = await _notifier.TrySendAsync(completion.PlayerId, lease.Value).ConfigureAwait(false);
    }
}
