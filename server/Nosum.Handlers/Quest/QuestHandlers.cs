// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.C2S;
using Nosum.Contracts.Quest.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Quest;

/// <summary>Reading the player's quests. Progress is counted by the server when the player acts, never reported by the client.</summary>
[PacketHandler("Nosum.Quest")]
public static partial class QuestHandlers
{
    [Inject] private static IQuestService s_quests = null!;

    /// <summary>Replies with every quest the player has.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(QuestOpCode.GetQuests)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "quest-read")]
    public static async ValueTask HandleGetQuestsAsync(IPacketContext<GetQuests> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        IReadOnlyList<QuestView> quests = await s_quests.GetAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);

        PacketScope<QuestLog> lease = PacketFactory<QuestLog>.Acquire();
        try
        {
            lease.Value.SequenceId = sequence;
            lease.Value.Quests.Clear();
            foreach (QuestView quest in quests)
            {
                QuestRow row = QuestRow.Create();
                row.QuestId = quest.QuestId;
                row.State = quest.State;
                foreach (int count in quest.Progress)
                {
                    row.Progress.Add(count);
                }

                lease.Value.Quests.Add(row);
            }

            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
