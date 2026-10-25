// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Friends;
using Nosum.Gameplay.Maps;
using Nosum.Wasm.Socket.Chat;
using Nosum.Wasm.Socket.Friends;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Visit;

namespace Nosum.Wasm.ViewModels.Friends;

/// <summary>One friend row.</summary>
/// <param name="PlayerId">The friend's account id.</param>
/// <param name="Label">The localized name ("Khách 1234" for a guest).</param>
/// <param name="Online">Whether the friend is online.</param>
/// <param name="Where">The localized status: the map the friend is on, "Online" when in a private place, or "Offline".</param>
public sealed record FriendDisplay(Guid PlayerId, string Label, bool Online, string Where);

/// <summary>One open friend request, or one blocked player.</summary>
/// <param name="PlayerId">The other player's account id.</param>
/// <param name="Label">The localized name.</param>
/// <param name="Nickname">The nickname as the server stores it.</param>
public sealed record PersonDisplay(Guid PlayerId, string Label, string Nickname);

/// <summary>
/// View model of the friends page (docs/adr/ADR-029 D4, D3): the friends and open requests, asking by nickname, answering, removing, the "friends may visit"
/// switch, and the block list. Status pushes change a row in place. The server decides every outcome; this types nicknames, shows the lists and the answers.
/// </summary>
public sealed class FriendsViewModel : PanelViewModel, IDisposable
{
    private readonly FriendsChannel _friends;
    private readonly ChatMessenger _chat;
    private readonly NicknameFormatter _names;

    /// <summary>Initializes the view model.</summary>
    /// <param name="friends">The friends channel.</param>
    /// <param name="chat">The chat channel, which holds the block list.</param>
    /// <param name="names">Formats nicknames for display.</param>
    /// <param name="text">UI text.</param>
    public FriendsViewModel(FriendsChannel friends, ChatMessenger chat, NicknameFormatter names, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(friends);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(names);

        _friends = friends;
        _chat = chat;
        _names = names;
        friends.Changed += this.Raise;
        friends.Completed += this.OnCompleted;
        chat.BlockedChanged += this.Raise;
        chat.Completed += this.OnChatCompleted;
    }

    /// <summary>Gets or sets the nickname typed to ask somebody to be friends.</summary>
    public string AddText { get; set; } = string.Empty;

    /// <summary>Gets or sets the nickname typed to block somebody.</summary>
    public string BlockText { get; set; } = string.Empty;

    /// <summary>Gets whether the player is a guest: a guest keeps no friends list and sees a hint to register instead of the form.</summary>
    public bool IsGuest => _friends.Current.IsGuest;

    /// <summary>Gets whether friends may visit the player's farm without being on the allow-list.</summary>
    public bool FriendsMayVisit => _friends.Current.FriendsMayVisit;

    /// <summary>Gets the friends: online ones first, then by name.</summary>
    public IReadOnlyList<FriendDisplay> Friends
    {
        get
        {
            List<FriendInfo> sorted = [.. _friends.Current.Friends];
            sorted.Sort((a, b) =>
            {
                int byOnline = b.Online.CompareTo(a.Online);
                return byOnline != 0 ? byOnline : string.Compare(a.Nickname, b.Nickname, StringComparison.CurrentCultureIgnoreCase);
            });

            List<FriendDisplay> rows = new(sorted.Count);
            foreach (FriendInfo friend in sorted)
            {
                rows.Add(new FriendDisplay(friend.PlayerId, this.Label(friend.Nickname), friend.Online, this.Where(friend)));
            }

            return rows;
        }
    }

    /// <summary>Gets the requests waiting for the player's answer.</summary>
    public IReadOnlyList<PersonDisplay> Incoming => this.People(_friends.Current.Incoming);

    /// <summary>Gets the requests the player sent that nobody answered.</summary>
    public IReadOnlyList<PersonDisplay> Outgoing => this.People(_friends.Current.Outgoing);

    /// <summary>Gets the block list.</summary>
    public IReadOnlyList<PersonDisplay> Blocked
    {
        get
        {
            List<PersonDisplay> rows = [];
            foreach (BlockedPlayerInfo player in _chat.Blocked)
            {
                rows.Add(new PersonDisplay(player.PlayerId, this.Label(player.Nickname), player.Nickname));
            }

            return rows;
        }
    }

    /// <summary>Gets the localized text of a friends outcome.</summary>
    /// <param name="status">The server's code.</param>
    /// <returns>The text in the current language.</returns>
    public string StatusText(FriendStatus status) => this.Text("Friend_Status_" + status);

    /// <summary>Called when the page opens: reads the friends and the block list.</summary>
    public void Open()
    {
        this.ClearStatus();
        _friends.Load();
        _chat.LoadBlocked();
    }

    /// <summary>Asks the typed player to be friends.</summary>
    public void Request()
    {
        string nickname = VisitorsViewModel.WireNickname(this.AddText);
        if (nickname.Length == 0)
        {
            return;
        }

        this.AddText = string.Empty;
        _friends.Request(nickname);
        this.Raise();
    }

    /// <summary>Accepts a request.</summary>
    /// <param name="playerId">The account id of the player who asked.</param>
    public void Accept(Guid playerId) => _friends.Respond(playerId, true);

    /// <summary>Declines a request.</summary>
    /// <param name="playerId">The account id of the player who asked.</param>
    public void Decline(Guid playerId) => _friends.Respond(playerId, false);

    /// <summary>Removes a friend, or withdraws a request the player sent.</summary>
    /// <param name="playerId">The other player's account id.</param>
    public void Remove(Guid playerId) => _friends.Remove(playerId);

    /// <summary>Flips the "friends may visit" setting.</summary>
    public void ToggleVisit() => _friends.SetFriendsVisit(!this.FriendsMayVisit);

    /// <summary>Blocks the typed player.</summary>
    public void Block()
    {
        string nickname = VisitorsViewModel.WireNickname(this.BlockText);
        if (nickname.Length == 0)
        {
            return;
        }

        this.BlockText = string.Empty;
        _chat.Block(nickname);
        this.Raise();
    }

    /// <summary>Unblocks a player.</summary>
    /// <param name="nickname">The nickname as the server stores it.</param>
    public void Unblock(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        _chat.Unblock(nickname);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _friends.Changed -= this.Raise;
        _friends.Completed -= this.OnCompleted;
        _chat.BlockedChanged -= this.Raise;
        _chat.Completed -= this.OnChatCompleted;
    }

    private string Label(string nickname)
    {
        string label = _names.Display(nickname);
        return label.Length > 0 ? label : this.Text("Friends_UnknownPlayer");
    }

    private List<PersonDisplay> People(IReadOnlyList<FriendRequestInfo> requests)
    {
        List<PersonDisplay> rows = new(requests.Count);
        foreach (FriendRequestInfo request in requests)
        {
            rows.Add(new PersonDisplay(request.PlayerId, this.Label(request.Nickname), request.Nickname));
        }

        return rows;
    }

    // A friend on a public map the client knows is named by its map; in a private place only "online" is shown (the server sends no map then).
    private string Where(FriendInfo friend)
    {
        if (!friend.Online)
        {
            return this.Text("Friends_Offline");
        }

        if (friend.MapId.Length == 0)
        {
            return this.Text("Friends_Online");
        }

        return GameMaps.TryGet(friend.MapId, out _) ? this.Text("Friends_OnMap", this.Text("Friends_Map_" + friend.MapId)) : this.Text("Friends_Online");
    }

    private void OnCompleted(FriendStatus status) => this.SetStatus(status == FriendStatus.Ok ? string.Empty : this.StatusText(status));

    // Only the moderation requests belong to this page; messages are the chat line's business.
    private void OnChatCompleted(ChatAction action, ChatStatus status, string nickname)
    {
        if (action == ChatAction.Send)
        {
            return;
        }

        if (status != ChatStatus.Ok)
        {
            this.SetStatus(this.Text("Chat_Status_" + status));
            return;
        }

        string key = action switch
        {
            ChatAction.Unblock => "Chat_Sys_Unblocked",
            ChatAction.Report => "Chat_Sys_Reported",
            _ => "Chat_Sys_Blocked",
        };
        this.SetStatus(this.Text(key, _names.Display(nickname)), good: true);
    }
}
