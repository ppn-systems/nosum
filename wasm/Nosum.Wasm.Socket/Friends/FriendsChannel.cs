// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Friends.C2S;
using Nosum.Contracts.Friends.S2C;

namespace Nosum.Wasm.Socket.Friends;

/// <summary>A mutual friend.</summary>
/// <param name="PlayerId">The friend's account id.</param>
/// <param name="Nickname">The nickname as the server sent it.</param>
/// <param name="Online">Whether the friend is online.</param>
/// <param name="MapId">The public map the friend is on; empty when offline or in a private place.</param>
public readonly record struct FriendInfo(Guid PlayerId, string Nickname, bool Online, string MapId);

/// <summary>An open friend request.</summary>
/// <param name="PlayerId">The other player's account id.</param>
/// <param name="Nickname">The other player's nickname.</param>
public readonly record struct FriendRequestInfo(Guid PlayerId, string Nickname);

/// <summary>The player's friends as the server last sent them.</summary>
/// <param name="FriendsMayVisit">Whether friends may visit the farm without being on the allow-list.</param>
/// <param name="IsGuest">Whether the player is a guest, who keeps no friends list.</param>
/// <param name="Friends">Mutual friends.</param>
/// <param name="Incoming">Requests waiting for the player's answer.</param>
/// <param name="Outgoing">Requests the player sent and nobody answered.</param>
public sealed record FriendsInfo(bool FriendsMayVisit, bool IsGuest, IReadOnlyList<FriendInfo> Friends, IReadOnlyList<FriendRequestInfo> Incoming, IReadOnlyList<FriendRequestInfo> Outgoing)
{
    /// <summary>Gets the state before the first read.</summary>
    public static readonly FriendsInfo None = new(true, false, [], [], []);
}

/// <summary>The friends side of the connection (docs/adr/ADR-029 D4): reads and receives the list, applies status pushes in place, and sends the requests that change it. The server decides every outcome.</summary>
public sealed class FriendsChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private readonly object _gate = new();

    /// <summary>Initializes the channel and starts listening for pushed lists and status changes.</summary>
    /// <param name="socket">The game connection.</param>
    public FriendsChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _subscriptions =
        [
            socket.Subscribe<FriendsSnapshot>(this.Store),
            socket.Subscribe<FriendStatusChanged>(p => this.ApplyStatus(p.PlayerId, p.Online, p.MapId)),
        ];
    }

    /// <summary>Raised when the list changed. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with the server's answer to a request. May run on any thread.</summary>
    public event Action<FriendStatus>? Completed;

    /// <summary>Gets the friends list.</summary>
    public FriendsInfo Current { get; private set; } = FriendsInfo.None;

    /// <summary>Reads the list.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>Sends a friend request; if the other player already asked, the two become friends.</summary>
    /// <param name="nickname">The other player's nickname as typed.</param>
    public void Request(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);
        _ = this.SendAsync<RequestFriend>(r => r.Nickname = nickname);
    }

    /// <summary>Accepts or declines a request.</summary>
    /// <param name="playerId">The account id of the player who asked.</param>
    /// <param name="accept">Whether to accept.</param>
    public void Respond(Guid playerId, bool accept) => _ = this.SendAsync<RespondFriend>(r =>
    {
        r.PlayerId = playerId;
        r.Accept = accept;
    });

    /// <summary>Removes a friend, or withdraws a request.</summary>
    /// <param name="playerId">The other player's account id.</param>
    public void Remove(Guid playerId) => _ = this.SendAsync<RemoveFriend>(r => r.PlayerId = playerId);

    /// <summary>Turns "friends may visit" on or off.</summary>
    /// <param name="allowed">The new setting.</param>
    public void SetFriendsVisit(bool allowed) => _ = this.SendAsync<SetFriendsVisit>(r => r.Allowed = allowed);

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private static List<FriendRequestInfo> Requests(List<FriendRequestRow> rows)
    {
        List<FriendRequestInfo> list = new(rows.Count);
        foreach (FriendRequestRow row in rows)
        {
            list.Add(new FriendRequestInfo(row.PlayerId, row.Nickname));
        }

        return list;
    }

    // The packet is pooled: copy what is needed before returning.
    private void Store(FriendsSnapshot snapshot)
    {
        List<FriendInfo> friends = new(snapshot.Friends.Count);
        foreach (FriendRow row in snapshot.Friends)
        {
            friends.Add(new FriendInfo(row.PlayerId, row.Nickname, row.Online, row.MapId));
        }

        FriendsInfo info = new(snapshot.FriendsMayVisit, snapshot.IsGuest, friends, Requests(snapshot.Incoming), Requests(snapshot.Outgoing));
        lock (_gate)
        {
            this.Current = info;
        }

        this.Changed?.Invoke();
    }

    // A friend went online or offline or changed map: change that row only, no refetch. An unknown id is ignored (the next snapshot has it).
    private void ApplyStatus(Guid playerId, bool online, string mapId)
    {
        lock (_gate)
        {
            FriendsInfo current = this.Current;
            int index = -1;
            for (int i = 0; i < current.Friends.Count; i++)
            {
                if (current.Friends[i].PlayerId == playerId)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            FriendInfo[] friends = [.. current.Friends];
            friends[index] = friends[index] with { Online = online, MapId = online ? mapId : string.Empty };
            this.Current = current with { Friends = friends };
        }

        this.Changed?.Invoke();
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried when the page opens again; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            using PacketScope<GetFriends> request = PacketFactory<GetFriends>.Acquire();
            FriendsSnapshot snapshot = await _socket.RequestAsync<FriendsSnapshot>(request.Value).ConfigureAwait(false);
            this.Store(snapshot);
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SendAsync<TRequest>(Action<TRequest> fill)
        where TRequest : Nalix.Codec.DataFrames.PacketBase<TRequest>, Nalix.Abstractions.Networking.Packets.IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TRequest> request = PacketFactory<TRequest>.Acquire();
            fill(request.Value);
            FriendResult reply = await _socket.RequestAsync<FriendResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(reply.Status);
            await this.LoadAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
