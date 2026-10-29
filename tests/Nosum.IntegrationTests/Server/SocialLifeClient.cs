// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Abstractions.Networking.Packets;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Chat;
using Nosum.Contracts.Chat.C2S;
using Nosum.Contracts.Chat.S2C;
using Nosum.Contracts.Emote;
using Nosum.Contracts.Emote.C2S;
using Nosum.Contracts.Emote.S2C;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Pet;
using Nosum.Contracts.Pet.C2S;
using Nosum.Contracts.Pet.S2C;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.C2S;
using Nosum.Contracts.Coop.S2C;
using Nosum.Contracts.Friends;
using Nosum.Contracts.Friends.C2S;
using Nosum.Contracts.Friends.S2C;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Mail.C2S;
using Nosum.Contracts.Mail.S2C;
using Nosum.Contracts.Market.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Trade;
using Nosum.Contracts.Trade.C2S;
using Nosum.Contracts.Trade.S2C;
using Nosum.Contracts.Visit;
using Nosum.Contracts.Visit.C2S;
using Nosum.Contracts.Visit.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Host;

namespace Nosum.IntegrationTests.Server;

/// <summary>A chat message as a client received it.</summary>
internal sealed record ChatCopy(ChatChannel Channel, string From, string To, string Text);

/// <summary>A mail as a client received it.</summary>
internal sealed record MailCopy(Guid Id, string Code, string[] Args, bool Read, long Coins, (int ItemId, int Quantity)[] Items, bool Collected);

/// <summary>A friends snapshot as a client received it.</summary>
internal sealed record FriendsCopy(bool FriendsMayVisit, bool IsGuest, FriendRow[] Friends, Guid[] Incoming, Guid[] Outgoing);

/// <summary>
/// A real Nalix WebSocket client for the chat, friends and mail tests (docs/adr/ADR-029): signs in with a session token (accounts are created
/// through the account service, because sign-ups are rate limited per address), joins the map the server saved for the player and speaks the
/// packets of the three features, plus the few trade, visit and co-op requests the block list and the mail producers touch. Every client of a
/// run shares one address, so one static gate spaces the paced requests of all of them.
/// </summary>
internal sealed class SocialLifeClient : IAsyncDisposable
{
    private static readonly SemaphoreSlim s_pace = new(1, 1);
    private static readonly long[] s_last = new long[2];
    private const int Request = 0;
    private const int Resume = 1;

    private readonly WebSocketSession _session;
    private readonly IDisposable[] _subscriptions;

    private SocialLifeClient(WebSocketSession session)
    {
        _session = session;
        _subscriptions =
        [
            session.On<ChatMessage>(m => this.Chats.Add(new ChatCopy(m.Channel, m.FromNickname, m.ToNickname, m.Text))),
            session.On<FriendsSnapshot>(s => this.FriendSnapshots.Add(Copy(s))),
            session.On<FriendStatusChanged>(s => this.FriendStatuses.Add((s.PlayerId, s.Online, s.MapId))),
            session.On<MailUnread>(m => this.Unread.Add(m.Count)),
            session.On<WalletUpdate>(w => this.Wallet.Add(w.Coins)),
            session.On<MapChanged>(m => this.MapChanges.Add(m.MapId)),
            session.On<TradeInvite>(t => this.TradeInvites.Add(t.FromPlayerId)),
            session.On<EmoteShown>(e => this.Emotes.Add((e.PlayerId, e.Emote))),
            session.On<LookChanged>(l => this.PetLooks.Add((l.Look.PlayerId, l.Look.PetKind, l.Look.PetName))),
        ];
    }

    public Guid AccountId { get; private set; }

    /// <summary>Gets the id the player has on the map.</summary>
    public uint MapPlayerId { get; private set; }

    public VisitClient.Inbox<ChatCopy> Chats { get; } = new();

    public VisitClient.Inbox<FriendsCopy> FriendSnapshots { get; } = new();

    public VisitClient.Inbox<(Guid PlayerId, bool Online, string MapId)> FriendStatuses { get; } = new();

    public VisitClient.Inbox<int> Unread { get; } = new();

    public VisitClient.Inbox<long> Wallet { get; } = new();

    public VisitClient.Inbox<string> MapChanges { get; } = new();

    public VisitClient.Inbox<uint> TradeInvites { get; } = new();

    public VisitClient.Inbox<(uint PlayerId, byte Emote)> Emotes { get; } = new();

    public VisitClient.Inbox<(uint PlayerId, byte Kind, string Name)> PetLooks { get; } = new();

    public static async Task<SocialLifeClient> ConnectAsync(ushort port, string sessionToken, CancellationToken ct)
    {
        WebSocketSession session = new(
            new TransportOptions { Address = "127.0.0.1", Port = port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });
        SocialLifeClient client = new(session);
        await session.ConnectAsync(ct: ct);
        await session.HandshakeAsync(ct);

        // Resuming a session is limited to two a second per address.
        await WaitAsync(Resume, 700, ct);
        using ResumeSession request = new() { SessionToken = sessionToken };
        AccountResult result = await session.RequestAsync<AccountResult>(request, ct: ct);
        result.Status.Should().Be(AccountStatus.Ok);
        client.AccountId = result.PlayerId;
        return client;
    }

    public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
    {
        await WirePace.JoinAsync(ct);
        using JoinMap request = new() { MapId = Program.FarmMapId };
        JoinMapResult result = await this.RequestAsync<JoinMapResult>(request, ct);
        result.Status.Should().Be(JoinStatus.Ok);
        this.MapPlayerId = result.PlayerId;
        return result;
    }

    // ---- chat ----

    /// <summary>Sends a chat message at once, without waiting for the pace gate (for the rate tests).</summary>
    public async Task<ChatStatus> SendFastAsync(ChatChannel channel, string target, string text, CancellationToken ct)
    {
        using SendChat request = new() { Channel = channel, Target = target, Text = text };
        return (await _session.RequestAsync<ChatResult>(request, ct: ct)).Status;
    }

    public async Task<ChatStatus> SendAsync(ChatChannel channel, string target, string text, CancellationToken ct)
    {
        using SendChat request = new() { Channel = channel, Target = target, Text = text };
        return (await this.RequestAsync<ChatResult>(request, ct)).Status;
    }

    public async Task<EmoteStatus> SendEmoteAsync(byte emote, CancellationToken ct)
    {
        using SendEmote request = new() { Emote = emote };
        return (await this.RequestAsync<EmoteResult>(request, ct)).Status;
    }

    public async Task<PetCopy> GetPetAsync(CancellationToken ct)
    {
        using GetPet request = new();
        return PetCopy.Of(await this.RequestAsync<PetState>(request, ct));
    }

    public async Task<PetCopy> AdoptPetAsync(byte kind, string name, CancellationToken ct)
    {
        using AdoptPet request = new() { Kind = kind, Name = name };
        return PetCopy.Of(await this.RequestAsync<PetState>(request, ct));
    }

    public async Task<PetCopy> PetPetAsync(CancellationToken ct)
    {
        using PetPet request = new();
        return PetCopy.Of(await this.RequestAsync<PetState>(request, ct));
    }

    public async Task<ChatStatus> BlockAsync(string nickname, CancellationToken ct)
    {
        using BlockPlayer request = new() { Nickname = nickname };
        return (await this.RequestAsync<ChatResult>(request, ct)).Status;
    }

    public async Task<ChatStatus> UnblockAsync(string nickname, CancellationToken ct)
    {
        using UnblockPlayer request = new() { Nickname = nickname };
        return (await this.RequestAsync<ChatResult>(request, ct)).Status;
    }

    public async Task<ChatStatus> ReportAsync(string nickname, CancellationToken ct)
    {
        using ReportChat request = new() { Nickname = nickname };
        return (await this.RequestAsync<ChatResult>(request, ct)).Status;
    }

    public async Task<(Guid PlayerId, string Nickname)[]> GetBlockedAsync(CancellationToken ct)
    {
        using GetBlocked request = new();
        BlockedList list = await this.RequestAsync<BlockedList>(request, ct);
        return [.. list.Players.Select(p => (p.PlayerId, p.Nickname))];
    }

    // ---- friends ----

    public async Task<FriendsCopy> GetFriendsAsync(CancellationToken ct)
    {
        using GetFriends request = new();
        return Copy(await this.RequestAsync<FriendsSnapshot>(request, ct));
    }

    public async Task<FriendStatus> RequestFriendAsync(string nickname, CancellationToken ct)
    {
        using RequestFriend request = new() { Nickname = nickname };
        return (await this.RequestAsync<FriendResult>(request, ct)).Status;
    }

    public async Task<FriendStatus> RespondFriendAsync(Guid from, bool accept, CancellationToken ct)
    {
        using RespondFriend request = new() { PlayerId = from, Accept = accept };
        return (await this.RequestAsync<FriendResult>(request, ct)).Status;
    }

    public async Task<FriendStatus> RemoveFriendAsync(Guid other, CancellationToken ct)
    {
        using RemoveFriend request = new() { PlayerId = other };
        return (await this.RequestAsync<FriendResult>(request, ct)).Status;
    }

    public async Task<FriendStatus> SetFriendsVisitAsync(bool allowed, CancellationToken ct)
    {
        using SetFriendsVisit request = new() { Allowed = allowed };
        return (await this.RequestAsync<FriendResult>(request, ct)).Status;
    }

    // ---- mail ----

    public async Task<(int Unread, MailCopy[] Mails)> GetMailAsync(CancellationToken ct)
    {
        using GetMail request = new();
        MailList list = await this.RequestAsync<MailList>(request, ct);
        return (list.Unread, [.. list.Mails.Select(m => new MailCopy(m.Id, m.Code, [.. m.Args.Select(a => a.Value)], m.Read, m.Coins, [.. m.Items.Select(i => (i.ItemId, i.Quantity))], m.Collected))]);
    }

    public async Task<MailStatus> ReadMailAsync(Guid id, CancellationToken ct)
    {
        using ReadMail request = new() { MailId = id };
        return (await this.RequestAsync<MailResult>(request, ct)).Status;
    }

    public async Task<MailStatus> CollectMailAsync(Guid id, CancellationToken ct)
    {
        using CollectMail request = new() { MailId = id };
        return (await this.RequestAsync<MailResult>(request, ct)).Status;
    }

    public async Task<MailStatus> DeleteMailAsync(Guid id, CancellationToken ct)
    {
        using DeleteMail request = new() { MailId = id };
        return (await this.RequestAsync<MailResult>(request, ct)).Status;
    }

    // ---- requests the block list and the producers touch ----

    public async Task<TradeStatus> TradeRequestAsync(uint targetMapPlayerId, CancellationToken ct)
    {
        using TradeRequest request = new() { TargetPlayerId = targetMapPlayerId };
        return (await this.RequestAsync<TradeResult>(request, ct)).Status;
    }

    public async Task<VisitStatus> AddVisitorAsync(string nickname, CancellationToken ct)
    {
        using AddVisitor request = new() { Nickname = nickname };
        return (await this.RequestAsync<VisitResult>(request, ct)).Status;
    }

    public async Task<VisitStatus> VisitAsync(string nickname, CancellationToken ct)
    {
        using VisitPlayer request = new() { Nickname = nickname };
        return (await this.RequestAsync<VisitResult>(request, ct)).Status;
    }

    public async Task<CoopStatus> CreateCoopAsync(string name, CancellationToken ct)
    {
        using CreateCoop request = new() { Name = name };
        return (await this.RequestAsync<CoopResult>(request, ct)).Status;
    }

    public async Task<CoopStatus> InviteToCoopAsync(uint targetMapPlayerId, CancellationToken ct)
    {
        using InvitePlayer request = new() { TargetPlayerId = targetMapPlayerId };
        return (await this.RequestAsync<CoopResult>(request, ct)).Status;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        await _session.DisconnectAsync();
        _session.Dispose();
    }

    private static FriendsCopy Copy(FriendsSnapshot s) => new(
        s.FriendsMayVisit,
        s.IsGuest,
        [.. s.Friends.Select(f => new FriendRow { PlayerId = f.PlayerId, Nickname = f.Nickname, Online = f.Online, MapId = f.MapId })],
        [.. s.Incoming.Select(r => r.PlayerId)],
        [.. s.Outgoing.Select(r => r.PlayerId)]);

    private static async Task WaitAsync(int kind, int gapMs, CancellationToken ct)
    {
        await s_pace.WaitAsync(ct);
        try
        {
            long wait = (s_last[kind] + gapMs) - Environment.TickCount64;
            if (wait > 0)
            {
                await Task.Delay((int)wait, ct);
            }

            s_last[kind] = Environment.TickCount64;
        }
        finally
        {
            _ = s_pace.Release();
        }
    }

    // Every client shares one address, so one rate limit: space the requests of all of them (the strictest handler allows about three a second).
    private async ValueTask<TReply> RequestAsync<TReply>(IPacket request, CancellationToken ct)
        where TReply : class, IPacket, IPacketStaticOpcode
    {
        await WaitAsync(Request, 380, ct);
        return await _session.RequestAsync<TReply>(request, ct: ct);
    }
}

/// <summary>A copy of a pet reply, read at once so no test holds a pooled packet.</summary>
internal sealed record PetCopy(PetStatus Status, byte Kind, string Name, int Mood, bool PettedToday)
{
    public static PetCopy Of(PetState state) => new(state.Status, state.Kind, state.Name, state.Mood, state.PettedToday);
}
