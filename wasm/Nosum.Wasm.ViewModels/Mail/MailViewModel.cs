// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Mail;
using Nosum.Gameplay.Mail;
using Nosum.Gameplay.Text;
using Nosum.Wasm.Socket.Mail;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Mail;

/// <summary>One mail ready to draw.</summary>
/// <param name="Id">The mail id.</param>
/// <param name="Title">The localized subject, rendered from the mail code.</param>
/// <param name="Body">The localized text, rendered from the code and its arguments.</param>
/// <param name="Read">Whether the player read it.</param>
/// <param name="Expiry">The localized "N days left" (or "Expired").</param>
/// <param name="Coins">Coins attached; zero for none.</param>
/// <param name="CoinsText">The coins formatted for display; empty for none.</param>
/// <param name="Items">The attached items as slots.</param>
/// <param name="CanCollect">Whether there is an attachment still to collect.</param>
/// <param name="Collected">Whether an attachment was collected.</param>
/// <param name="CanDelete">Whether the mail can be deleted (it carries nothing uncollected).</param>
public sealed record MailDisplay(Guid Id, string Title, string Body, bool Read, string Expiry, long Coins, string CoinsText, IReadOnlyList<SlotDisplay> Items, bool CanCollect, bool Collected, bool CanDelete);

/// <summary>
/// View model of the mail page (docs/adr/ADR-029 D5) and of the unread badge on the gear. The unread count is live from sign-in: the mailbox is read once after
/// sign-in and the server pushes every change. System mail arrives as a code plus arguments and is rendered here from the resources, in the current language.
/// </summary>
public sealed class MailViewModel : PanelViewModel, IDisposable
{
    private const long DayMs = 24L * 60 * 60 * 1000;

    private readonly MailChannel _mail;
    private readonly ItemDescriber _items;
    private readonly NicknameFormatter _names;
    private readonly Func<long> _nowMs;
    private bool _open;

    /// <summary>Initializes the view model.</summary>
    /// <param name="mail">The mail channel.</param>
    /// <param name="items">Names and pictures items.</param>
    /// <param name="names">Formats nicknames for display.</param>
    /// <param name="text">UI text.</param>
    /// <param name="serverNowMs">The server time in Unix milliseconds, for "days left"; the local clock when <see langword="null"/>.</param>
    public MailViewModel(MailChannel mail, ItemDescriber items, NicknameFormatter names, IStringLocalizer<Language> text, Func<long>? serverNowMs = null)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(names);

        _mail = mail;
        _items = items;
        _names = names;
        _nowMs = serverNowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        mail.Changed += this.Raise;
        mail.UnreadChanged += this.OnUnreadChanged;
        mail.Completed += this.OnCompleted;
    }

    /// <summary>Gets the number of unread mails (the badge on the gear).</summary>
    public int Unread => _mail.Unread;

    /// <summary>Gets the badge text: the count, or "9+" for many.</summary>
    public string UnreadText => this.Unread > 9 ? "9+" : this.Unread.ToString(CultureInfo.CurrentCulture);

    /// <summary>Gets the mail whose text is open.</summary>
    public Guid? Selected { get; private set; }

    /// <summary>Gets the mails, newest first.</summary>
    public IReadOnlyList<MailDisplay> Mails
    {
        get
        {
            long now = _nowMs();
            List<MailDisplay> rows = [];
            foreach (MailInfo mail in _mail.Mails)
            {
                rows.Add(this.Render(mail, now));
            }

            return rows;
        }
    }

    /// <summary>Gets the localized text of a mail outcome.</summary>
    /// <param name="status">The server's code.</param>
    /// <returns>The text in the current language.</returns>
    public string StatusText(MailStatus status) => this.Text("Mail_Status_" + status);

    /// <summary>Called when the page opens: reads the mailbox, and keeps it current while the page is open.</summary>
    public void Open()
    {
        _open = true;
        this.ClearStatus();
        _mail.Load();
    }

    /// <summary>Called when the page closes.</summary>
    public void Close() => _open = false;

    /// <summary>Opens a mail (or closes it when it is open) and marks it read.</summary>
    /// <param name="mailId">The mail.</param>
    public void Toggle(Guid mailId)
    {
        if (this.Selected == mailId)
        {
            this.Selected = null;
            this.Raise();
            return;
        }

        this.Selected = mailId;
        foreach (MailInfo mail in _mail.Mails)
        {
            if (mail.Id == mailId && !mail.Read)
            {
                _mail.MarkRead(mailId);
                break;
            }
        }

        this.Raise();
    }

    /// <summary>Collects the attachment of a mail.</summary>
    /// <param name="mailId">The mail.</param>
    public void Collect(Guid mailId) => _mail.Collect(mailId);

    /// <summary>Deletes a mail.</summary>
    /// <param name="mailId">The mail.</param>
    public void Delete(Guid mailId)
    {
        if (this.Selected == mailId)
        {
            this.Selected = null;
        }

        _mail.Delete(mailId);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _mail.Changed -= this.Raise;
        _mail.UnreadChanged -= this.OnUnreadChanged;
        _mail.Completed -= this.OnCompleted;
    }

    // A mail arrived or changed while the page is open: read the box again so the list is not stale.
    private void OnUnreadChanged()
    {
        if (_open)
        {
            _mail.Load();
        }
    }

    private void OnCompleted(MailAction action, MailStatus status)
    {
        if (status != MailStatus.Ok)
        {
            this.SetStatus(this.StatusText(status));
        }
        else
        {
            this.SetStatus(action == MailAction.Collect ? this.Text("Mail_Collected") : string.Empty, good: true);
        }
    }

    private MailDisplay Render(MailInfo mail, long now)
    {
        (string title, string body) = this.Describe(mail);
        bool hasAttachment = mail.Coins > 0 || mail.Items.Count > 0;
        List<SlotDisplay> slots = new(mail.Items.Count);
        for (int i = 0; i < mail.Items.Count; i++)
        {
            slots.Add(_items.Describe(i, mail.Items[i].ItemId, mail.Items[i].Quantity));
        }

        bool canCollect = hasAttachment && !mail.Collected && mail.ExpiresAtMs > now;
        return new MailDisplay(
            mail.Id,
            title,
            body,
            mail.Read,
            this.ExpiryText(mail.ExpiresAtMs, now),
            mail.Coins,
            mail.Coins > 0 ? Number(mail.Coins) : string.Empty,
            slots,
            canCollect,
            hasAttachment && mail.Collected,
            !hasAttachment || mail.Collected || mail.ExpiresAtMs <= now);
    }

    /// <summary>Renders a mail code and its arguments as a localized subject and text. A code this client does not know, or arguments that do not fit it, give a generic notice.</summary>
    /// <param name="mail">The mail.</param>
    /// <returns>The subject and the text.</returns>
    public (string Title, string Body) Describe(MailInfo mail)
    {
        ArgumentNullException.ThrowIfNull(mail);
        IReadOnlyList<string> args = mail.Args;
        switch (mail.Code)
        {
            case MailRules.Codes.MarketSold
                when args.Count >= 3
                     && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int itemId)
                     && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int quantity)
                     && long.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long coins):
            {
                string item = _items.NameOf(itemId);
                return (this.Text("Mail_Title_MarketSold"), this.Text("Mail_Body_MarketSold", item.Length > 0 ? item : this.Text("Mail_UnknownItem"), quantity, Number(coins)));
            }

            case MailRules.Codes.CoopInvite when args.Count >= 2:
            {
                string coop = LocalizedText.Resolve(args[0], CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
                string inviter = _names.Display(args[1]);
                return (this.Text("Mail_Title_CoopInvite"), inviter.Length > 0 ? this.Text("Mail_Body_CoopInviteFrom", coop, inviter) : this.Text("Mail_Body_CoopInvite", coop));
            }

            case MailRules.Codes.GoalReward
                when args.Count >= 1 && long.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long reward):
                return (this.Text("Mail_Title_GoalReward"), this.Text("Mail_Body_GoalReward", Number(reward)));
            default:
                return (this.Text("Mail_Title_Unknown"), this.Text("Mail_Body_Unknown"));
        }
    }

    private string ExpiryText(long expiresAtMs, long now)
    {
        long left = expiresAtMs - now;
        if (left <= 0)
        {
            return this.Text("Mail_Expired");
        }

        long days = (left + DayMs - 1) / DayMs;
        return days == 1 ? this.Text("Mail_Expiry_One") : this.Text("Mail_Expiry", days);
    }
}
