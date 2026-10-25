// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Mail;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Mail.C2S;
using Nosum.Contracts.Mail.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Mail;

/// <summary>The mailbox (docs/adr/ADR-029 D5). The identity comes from the connection; a mail id from a packet is only ever looked up in that player's own mailbox.</summary>
[PacketHandler("Nosum.Mail")]
public static partial class MailHandlers
{
    [Inject] private static IMailService s_mail = null!;

    /// <summary>Replies with the player's mailbox.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MailOpCode.GetMail)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "mail-read")]
    public static async ValueTask HandleGetMailAsync(IPacketContext<GetMail> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        PlayerIdentity player = context.Connection.GetPlayer();

        IReadOnlyList<MailView> mails = await s_mail.ListAsync(player.PlayerId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<GetMail, MailList>(context, sequence, list =>
        {
            list.Mails.Clear();
            int unread = 0;
            foreach (MailView mail in mails)
            {
                list.Mails.Add(mail.ToRow());
                if (!mail.Read)
                {
                    unread++;
                }
            }

            list.Unread = unread;
        }).ConfigureAwait(false);
    }

    /// <summary>Marks a mail read.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MailOpCode.ReadMail)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "mail-write")]
    public static async ValueTask HandleReadMailAsync(IPacketContext<ReadMail> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid mailId = context.Packet.MailId;
        PlayerIdentity player = context.Connection.GetPlayer();

        MailStatus status = await s_mail.ReadAsync(player.PlayerId, mailId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<ReadMail, MailResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Collects the attachment of a mail in one atomic transfer.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MailOpCode.CollectMail)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(4, 3, PolicyId = "mail-collect")]
    public static async ValueTask HandleCollectMailAsync(IPacketContext<CollectMail> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid mailId = context.Packet.MailId;
        PlayerIdentity player = context.Connection.GetPlayer();

        MailStatus status = await s_mail.CollectAsync(player.PlayerId, mailId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<CollectMail, MailResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }

    /// <summary>Deletes a mail that carries nothing left to collect.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(MailOpCode.DeleteMail)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 3, PolicyId = "mail-write")]
    public static async ValueTask HandleDeleteMailAsync(IPacketContext<DeleteMail> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        Guid mailId = context.Packet.MailId;
        PlayerIdentity player = context.Connection.GetPlayer();

        MailStatus status = await s_mail.DeleteAsync(player.PlayerId, mailId, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<DeleteMail, MailResult>(context, sequence, r => r.Status = status).ConfigureAwait(false);
    }
}
