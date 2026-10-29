// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Visit;
using Nosum.Wasm.Socket.Visit;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Visit;

/// <summary>One row of the visitors list.</summary>
/// <param name="PlayerId">The player's account id.</param>
/// <param name="Label">The localized name ("Khách 1234" for a guest).</param>
/// <param name="Listed">Whether the owner put them on the allow-list (can be removed).</param>
/// <param name="Present">Whether they are in the owner's place now (can be sent out).</param>
public sealed record VisitorDisplay(Guid PlayerId, string Label, bool Listed, bool Present);

/// <summary>One line of the farm guestbook: a visitor's watering on one day.</summary>
/// <param name="Visitor">The visitor's localized name.</param>
/// <param name="DayNumber">The game day number.</param>
/// <param name="Waters">How many crops the visitor watered.</param>
/// <param name="Unread">Whether the entry was new when the guestbook was read.</param>
public sealed record GuestbookDisplay(string Visitor, int DayNumber, int Waters, bool Unread);

/// <summary>
/// View model of the visitors panel (docs/adr/ADR-019): edit who may visit the player's own farm, and visit somebody else. The server checks the
/// allow-list on every visit and decides every outcome; this only types nicknames, shows the list and the answers.
/// </summary>
public sealed class VisitorsViewModel : PanelViewModel, IDisposable
{
    private readonly VisitChannel _visits;
    private readonly NicknameFormatter _names;

    /// <summary>Initializes the view model.</summary>
    /// <param name="visits">The visit channel.</param>
    /// <param name="names">Names players by nickname.</param>
    /// <param name="text">UI text.</param>
    public VisitorsViewModel(VisitChannel visits, NicknameFormatter names, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(visits);
        ArgumentNullException.ThrowIfNull(names);

        _visits = visits;
        _names = names;
        visits.Changed += this.Raise;
        visits.Completed += this.OnCompleted;
        visits.Visiting += this.OnVisiting;
    }

    /// <summary>Raised when a visit was accepted: the panel can close, the map scene takes over.</summary>
    public event Action? Entered;

    /// <summary>Gets or sets the nickname typed to let a player visit.</summary>
    public string AddText { get; set; } = string.Empty;

    /// <summary>Gets or sets the nickname typed to visit.</summary>
    public string VisitText { get; set; } = string.Empty;

    /// <summary>Gets whether the player is a guest: a guest cannot let others in (the panel offers only visiting).</summary>
    public bool IsGuest => _visits.Current.IsGuest;

    /// <summary>Gets whether members of the player's co-op may visit.</summary>
    public bool CoopMembersMayVisit => _visits.Current.CoopMembersMayVisit;

    /// <summary>Gets the visitors: the allow-list, then any co-op member who is visiting now.</summary>
    public IReadOnlyList<VisitorDisplay> Visitors
    {
        get
        {
            List<VisitorDisplay> rows = [];
            foreach (VisitorInfo visitor in _visits.Current.Visitors)
            {
                string label = _names.Display(visitor.Nickname);
                rows.Add(new VisitorDisplay(visitor.PlayerId, label.Length > 0 ? label : this.Text("Visit_UnknownPlayer"), visitor.Listed, visitor.Present));
            }

            return rows;
        }
    }

    /// <summary>Gets how many guestbook entries were new when the guestbook was last read.</summary>
    public int GuestbookUnread => _visits.Guestbook.UnreadCount;

    /// <summary>Gets the newest guestbook entries, newest day first.</summary>
    public IReadOnlyList<GuestbookDisplay> Guestbook
    {
        get
        {
            List<GuestbookDisplay> rows = [];
            foreach (GuestbookEntryInfo entry in _visits.Guestbook.Entries)
            {
                string label = _names.Display(entry.Nickname);
                rows.Add(new GuestbookDisplay(label.Length > 0 ? label : this.Text("Visit_UnknownPlayer"), entry.DayNumber, entry.Waters, entry.Unread));
            }

            return rows;
        }
    }

    /// <summary>Called when the panel opens: reads the list and the guestbook.</summary>
    public void Open()
    {
        this.ClearStatus();
        _visits.Load();
        _visits.LoadGuestbook();
    }

    /// <summary>Lets the typed player visit.</summary>
    public void Add()
    {
        _visits.Add(WireNickname(this.AddText));
        this.AddText = string.Empty;
        this.Raise();
    }

    /// <summary>Takes a player off the list.</summary>
    /// <param name="playerId">The player's account id.</param>
    public void Remove(Guid playerId) => _visits.Remove(playerId);

    /// <summary>Sends a player out of the place.</summary>
    /// <param name="playerId">The player's account id.</param>
    public void Kick(Guid playerId) => _visits.Kick(playerId);

    /// <summary>Flips the "co-op members may visit" setting.</summary>
    public void ToggleCoopVisits() => _visits.SetCoopVisits(!this.CoopMembersMayVisit);

    /// <summary>Asks to visit the typed player.</summary>
    public void Visit() => _visits.Visit(WireNickname(this.VisitText));

    /// <summary>
    /// Turns what a player typed into the nickname the server stores. A guest is shown as "Khách 1234" or "Guest 1234" but stored as <c>guest-1234</c>;
    /// no chosen nickname can look like those (the words are reserved), so reading them back is safe.
    /// </summary>
    /// <param name="typed">What was typed.</param>
    /// <returns>The nickname to send.</returns>
    public static string WireNickname(string? typed)
    {
        string text = (typed ?? string.Empty).Trim();
        foreach (string prefix in new[] { "khách ", "khach ", "guest " })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string digits = text[prefix.Length..].Trim();
                if (digits.Length > 0 && digits.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0)
                {
                    return Nosum.Gameplay.Players.NicknameRules.GuestPrefix + digits;
                }
            }
        }

        return text;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _visits.Changed -= this.Raise;
        _visits.Completed -= this.OnCompleted;
        _visits.Visiting -= this.OnVisiting;
    }

    private void OnCompleted(VisitStatus status) => this.SetStatus(status == VisitStatus.Ok ? string.Empty : this.Text("Visit_Status_" + status));

    private void OnVisiting() => this.Entered?.Invoke();
}
