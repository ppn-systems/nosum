// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Emotes;
using Nosum.Contracts.Emote;
using Nosum.Contracts.Emote.C2S;
using Nosum.Contracts.Emote.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Emotes;

/// <summary>
/// The emote wheel (docs/adr/ADR-075 D1-D2). The service checks the code and the cooldown and shows the emote to the players near the sender;
/// the transport throttles bursts of the opcode.
/// </summary>
[PacketHandler("Nosum.Emote")]
public static partial class EmoteHandlers
{
    [Inject] private static IEmoteService s_emotes = null!;

    /// <summary>Shows an emote above the sender's farmer.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(EmoteOpCode.SendEmote)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 3, PolicyId = "emote-send")]
    public static async ValueTask HandleSendEmoteAsync(IPacketContext<SendEmote> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        byte emote = context.Packet.Emote;

        EmoteStatus status = await s_emotes.SendAsync(context.Connection, emote, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<SendEmote, EmoteResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }
}
