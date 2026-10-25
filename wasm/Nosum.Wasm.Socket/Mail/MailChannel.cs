// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Mail.C2S;
using Nosum.Contracts.Mail.S2C;

namespace Nosum.Wasm.Socket.Mail;

/// <summary>One item line attached to a mail.</summary>
/// <param name="ItemId">The item.</param>
/// <param name="Quantity">How many.</param>
public readonly record struct MailItemInfo(int ItemId, int Quantity);

/// <summary>One mail as the server sent it. System mail is a code plus arguments, rendered by the view model.</summary>
/// <param name="Id">The mail id.</param>
/// <param name="Code">The message code, for example <c>market.sold</c>.</param>
/// <param name="Args">The arguments of the code, in order.</param>
/// <param name="SentAtMs">When it was sent, Unix milliseconds on the server clock.</param>
/// <param name="ExpiresAtMs">When it expires, Unix milliseconds on the server clock.</param>
/// <param name="Read">Whether the player read it.</param>
/// <param name="Coins">Coins attached; zero for none.</param>
/// <param name="Items">Items attached.</param>
/// <param name="Collected">Whether the attachment was collected.</param>
public sealed record MailInfo(Guid Id, string Code, IReadOnlyList<string> Args, long SentAtMs, long ExpiresAtMs, bool Read, long Coins, IReadOnlyList<MailItemInfo> Items, bool Collected);

/// <summary>The kind of mail request an answer belongs to.</summary>
public enum MailAction
{
    /// <summary>A mail was marked read.</summary>
    Read = 0,

    /// <summary>An attachment was collected.</summary>
    Collect = 1,

    /// <summary>A mail was deleted.</summary>
    Delete = 2,
}

/// <summary>
/// The mailbox side of the connection (docs/adr/ADR-029 D5): reads the mailbox, follows the unread count the server pushes, and sends read, collect and delete.
/// The unread count is live from the first read after sign-in; the server decides every outcome (the collect is one atomic transfer).
/// </summary>
public sealed class MailChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable _unreadPush;
    private int _unread;

    /// <summary>Initializes the channel and starts listening for unread-count pushes.</summary>
    /// <param name="socket">The game connection.</param>
    public MailChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _unreadPush = socket.Subscribe<MailUnread>(p =>
        {
            Volatile.Write(ref _unread, p.Count);
            this.UnreadChanged?.Invoke();
            this.Changed?.Invoke();
        });
    }

    /// <summary>Raised when the mailbox or the unread count changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when the server pushed an unread count (a mail arrived, was read or deleted). May run on any thread.</summary>
    public event Action? UnreadChanged;

    /// <summary>Raised with the server's answer to a request. May run on any thread.</summary>
    public event Action<MailAction, MailStatus>? Completed;

    /// <summary>Gets the number of unread mails.</summary>
    public int Unread => Volatile.Read(ref _unread);

    /// <summary>Gets the mailbox, newest first, as last read.</summary>
    public IReadOnlyList<MailInfo> Mails { get; private set; } = [];

    /// <summary>Reads the mailbox (and with it the unread count). Call whenever the connection is signed in.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Marks a mail read.</summary>
    /// <param name="mailId">The mail.</param>
    public void MarkRead(Guid mailId) => _ = this.SendAsync<ReadMail>(MailAction.Read, mailId, r => r.MailId = mailId);

    /// <summary>Collects the items and coins attached to a mail.</summary>
    /// <param name="mailId">The mail.</param>
    public void Collect(Guid mailId) => _ = this.SendAsync<CollectMail>(MailAction.Collect, mailId, r => r.MailId = mailId);

    /// <summary>Deletes a mail that carries no uncollected attachment.</summary>
    /// <param name="mailId">The mail.</param>
    public void Delete(Guid mailId) => _ = this.SendAsync<DeleteMail>(MailAction.Delete, mailId, r => r.MailId = mailId);

    /// <inheritdoc/>
    public void Dispose() => _unreadPush.Dispose();

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried when the page opens again; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetMail> request = PacketFactory<GetMail>.Acquire();
            MailList list = await _socket.RequestAsync<MailList>(request.Value).ConfigureAwait(false);
            List<MailInfo> mails = new(list.Mails.Count);
            foreach (MailRow row in list.Mails)
            {
                List<string> args = new(row.Args.Count);
                foreach (MailArgRow arg in row.Args)
                {
                    args.Add(arg.Value);
                }

                List<MailItemInfo> items = new(row.Items.Count);
                foreach (MailItemRow item in row.Items)
                {
                    items.Add(new MailItemInfo(item.ItemId, item.Quantity));
                }

                mails.Add(new MailInfo(row.Id, row.Code, args, row.SentAtMs, row.ExpiresAtMs, row.Read, row.Coins, items, row.Collected));
            }

            this.Mails = mails;
            Volatile.Write(ref _unread, list.Unread);
            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SendAsync<TRequest>(MailAction action, Guid mailId, Action<TRequest> fill)
        where TRequest : Nalix.Codec.DataFrames.PacketBase<TRequest>, Nalix.Abstractions.Networking.Packets.IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TRequest> request = PacketFactory<TRequest>.Acquire();
            fill(request.Value);
            MailResult reply = await _socket.RequestAsync<MailResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(action, reply.Status);

            // A read only flips a flag (the server pushes the new unread count); the others change what the box holds,
            // and a refusal means the list is stale: read it again.
            if (action == MailAction.Read && reply.Status == MailStatus.Ok)
            {
                this.MarkReadLocally(mailId);
            }
            else
            {
                await this.LoadAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }

    private void MarkReadLocally(Guid mailId)
    {
        List<MailInfo> mails = [.. this.Mails];
        for (int i = 0; i < mails.Count; i++)
        {
            if (mails[i].Id == mailId)
            {
                mails[i] = mails[i] with { Read = true };
                this.Mails = mails;
                this.Changed?.Invoke();
                return;
            }
        }
    }
}
