// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Journal;
using Nosum.Contracts.Journal;
using Nosum.Contracts.Journal.C2S;
using Nosum.Contracts.Journal.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Journal;

/// <summary>
/// The collection journal. The identity is the connection's, so a player reads only their own journal. Discoveries are written by the inventory
/// service inside each grant, never by a request from the client.
/// </summary>
[PacketHandler("Nosum.Journal")]
public static partial class JournalHandlers
{
    [Inject] private static IJournalService s_journal = null!;

    /// <summary>Replies with the discovered entries of the player's journal.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(JournalOpCode.GetJournal)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "journal-read")]
    public static async ValueTask HandleGetJournalAsync(IPacketContext<GetJournal> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        IReadOnlyList<JournalLine> lines = await s_journal.GetAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetJournal, JournalReply>(context, sequence, reply => FillReply(reply, lines)).ConfigureAwait(false);
    }

    private static void FillReply(JournalReply reply, IReadOnlyList<JournalLine> lines)
    {
        reply.Entries.Clear();
        foreach (JournalLine line in lines)
        {
            JournalEntry entry = JournalEntry.Create();
            entry.ItemId = line.ItemId;
            entry.Count = line.Count;
            entry.FirstObtainedUnix = line.FirstObtainedAt.ToUnixTimeSeconds();
            reply.Entries.Add(entry);
        }
    }
}
