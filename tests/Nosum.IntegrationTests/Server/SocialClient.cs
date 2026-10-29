// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Framework.Injection;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Accounts;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Contracts.Coop.C2S;
using Nosum.Contracts.Coop.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Look;
using Nosum.Contracts.Look.C2S;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Trade;
using Nosum.Contracts.Trade.C2S;
using Nosum.Contracts.Trade.S2C;
using Nosum.Core;
using Nosum.Host;

namespace Nosum.IntegrationTests.Server;

/// <summary>An open trade as a client received it.</summary>
internal sealed record TradeCopy(uint OtherPlayerId, (int ItemId, int Quantity)[] MyItems, long MyCoins, (int ItemId, int Quantity)[] OtherItems, long OtherCoins, bool MyConfirmed, bool OtherConfirmed);

/// <summary>A co-op snapshot as a client received it.</summary>
internal sealed record CoopCopy(bool HasCoop, string Name, CoopRole Role, CoopMemberRow[] Members, (int ItemId, int Quantity, int Available)[] Stock, Guid[] InviteIds);

/// <summary>
/// A real Nalix WebSocket client for the social tests: signs in as a guest, joins the farm and speaks every social packet. All clients of
/// a test run share one address, so the rate limits are shared: <see cref="Pace"/> spaces every request (and guest start) of every client.
/// </summary>
internal sealed partial class SocialClient : IAsyncDisposable
{
    private static readonly SemaphoreSlim s_pace = new(1, 1);
    private static long s_lastRequest;
    private static long s_lastGuest;

    private readonly WebSocketSession _session;
    private readonly IDisposable[] _subscriptions;

    private SocialClient(WebSocketSession session)
    {
        _session = session;
        _subscriptions =
        [
            session.On<WalletUpdate>(p => this.Wallet.Add(p.Coins)),
            session.On<InventoryUpdate>(p =>
            {
                foreach (InventorySlotRow s in p.Slots)
                {
                    this.Slots.Add((s.Slot, s.ItemId, s.Quantity));
                }
            }),
            session.On<LookChanged>(p => this.Looks.Add(Copy(p.Look))),
            session.On<TradeInvite>(p => this.Invites.Add(p.FromPlayerId)),
            session.On<TradeState>(p => this.TradeStates.Add(new TradeCopy(
                p.OtherPlayerId,
                [.. p.MyItems.Select(i => (i.ItemId, i.Quantity))],
                p.MyCoins,
                [.. p.OtherItems.Select(i => (i.ItemId, i.Quantity))],
                p.OtherCoins,
                p.MyConfirmed,
                p.OtherConfirmed))),
            session.On<TradeEnded>(p => this.TradeEnds.Add(p.Reason)),
            session.On<CoopSnapshot>(p => this.Coops.Add(new CoopCopy(
                p.HasCoop,
                p.Name,
                p.Role,
                [.. p.Members.Select(m => new CoopMemberRow { MemberId = m.MemberId, Role = m.Role, IsMe = m.IsMe, Online = m.Online, Nickname = m.Nickname })],
                [.. p.Stock.Select(s => (s.ItemId, s.Quantity, s.Available))],
                [.. p.Invites.Select(i => i.InviteId)]))),
        ];
    }

    /// <summary>The shared map the clients meet on.</summary>
    public const string SharedMapId = "busstop";

    public Guid PlayerId { get; private set; }

    public string SessionToken { get; private set; } = string.Empty;

    /// <summary>Gets the nickname the account result carried (a neutral guest name for a guest).</summary>
    public string Nickname { get; private set; } = string.Empty;

    /// <summary>Gets the id the player has on the map.</summary>
    public uint MapPlayerId { get; private set; }

    public Inbox<long> Wallet { get; } = new();

    public Inbox<(int Slot, int ItemId, int Quantity)> Slots { get; } = new();

    public Inbox<LookRow> Looks { get; } = new();

    public Inbox<uint> Invites { get; } = new();

    public Inbox<TradeCopy> TradeStates { get; } = new();

    public Inbox<TradeEndReason> TradeEnds { get; } = new();

    public Inbox<CoopCopy> Coops { get; } = new();

    /// <summary>
    /// Signs in as a guest. The player is saved on a map first, so the join puts them there: by default on the bus stop, a shared map where clients meet
    /// (farms are private, docs/adr/ADR-015); <paramref name="startAt"/> picks another map and place (the saved place is used when it is legal, the map's spawn otherwise).
    /// </summary>
    public static async Task<SocialClient> ConnectAsync(ushort port, CancellationToken ct, (string Map, double X, double Y)? startAt = null)
    {
        await PaceGuestAsync(ct);
        WebSocketSession session = new(
            new TransportOptions { Address = "127.0.0.1", Port = port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });
        SocialClient client = new(session);
        await session.ConnectAsync(ct: ct);
        await session.HandshakeAsync(ct);
        using StartGuest request = new() { PreferredLanguage = Nosum.Contracts.Common.GameLanguage.English };
        AccountResult result = await session.RequestAsync<AccountResult>(request, ct: ct);
        result.Status.Should().Be(AccountStatus.Ok);
        client.PlayerId = result.PlayerId;
        client.Nickname = result.Nickname;
        client.SessionToken = result.SessionToken;
        (string map, double x, double y) = startAt ?? (SharedMapId, -1, -1);
        IPlayerStateService states = InstanceManager.Instance.GetExistingInstance<IPlayerStateService>()!;
        await states.SaveAsync(result.PlayerId, new PlayerStateRow(map, x, y, Facing.Down), ct);

        return client;
    }

    /// <summary>Signs in again with the session token of an earlier client.</summary>
    public static async Task<SocialClient> ResumeAsync(ushort port, string token, CancellationToken ct)
    {
        await PaceGuestAsync(ct);
        WebSocketSession session = new(
            new TransportOptions { Address = "127.0.0.1", Port = port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });
        SocialClient client = new(session);
        await session.ConnectAsync(ct: ct);
        await session.HandshakeAsync(ct);
        using ResumeSession request = new() { SessionToken = token };
        AccountResult result = await session.RequestAsync<AccountResult>(request, ct: ct);
        result.Status.Should().Be(AccountStatus.Ok);
        client.PlayerId = result.PlayerId;
        client.Nickname = result.Nickname;
        client.SessionToken = token;
        return client;
    }

    public async Task JoinAsync(CancellationToken ct)
    {
        await WirePace.JoinAsync(ct);
        using JoinMap request = new() { MapId = Program.FarmMapId };
        JoinMapResult result = await this.RequestAsync<JoinMapResult>(request, ct);
        result.Status.Should().Be(JoinStatus.Ok);
        this.MapPlayerId = result.PlayerId;
    }

    public async Task<InventorySnapshot> GetInventoryAsync(CancellationToken ct)
    {
        using GetInventory request = new();
        return await this.RequestAsync<InventorySnapshot>(request, ct);
    }

    public async Task<int> CountAsync(int itemId, CancellationToken ct)
        => (await this.GetInventoryAsync(ct)).Slots.Where(s => s.ItemId == itemId).Sum(s => s.Quantity);

    public async Task<int> SlotOfAsync(int itemId, CancellationToken ct)
        => (await this.GetInventoryAsync(ct)).Slots.First(s => s.ItemId == itemId).Slot;

    public async Task<long> CoinsAsync(CancellationToken ct)
    {
        using GetWallet request = new();
        return (await this.RequestAsync<WalletSnapshot>(request, ct)).Coins;
    }

    // ---- market ----
    public async Task<MarketStatus> ListAsync(int slot, int itemId, int quantity, long price, CancellationToken ct)
    {
        using CreateListing request = new() { Slot = (byte)slot, ItemId = itemId, Quantity = quantity, Price = price };
        return (await this.RequestAsync<MarketResult>(request, ct)).Status;
    }

    public async Task<MarketStatus> BuyAsync(Guid listing, CancellationToken ct)
    {
        using BuyListing request = new() { ListingId = listing };
        return (await this.RequestAsync<MarketResult>(request, ct)).Status;
    }

    public async Task<MarketStatus> CancelListingAsync(Guid listing, CancellationToken ct)
    {
        using CancelListing request = new() { ListingId = listing };
        return (await this.RequestAsync<MarketResult>(request, ct)).Status;
    }

    public async Task<(int Total, ListingRow[] Rows)> BrowseAsync(int[] itemIds, CancellationToken ct)
    {
        using BrowseListings request = new() { Page = 0 };
        request.ItemIds.AddRange(itemIds);
        ListingsPage page = await this.RequestAsync<ListingsPage>(request, ct);
        return (page.Total, [.. page.Rows.Select(r => new ListingRow { ListingId = r.ListingId, ItemId = r.ItemId, Quantity = r.Quantity, Price = r.Price, ExpiresAtMs = r.ExpiresAtMs, IsMine = r.IsMine, SellerNickname = r.SellerNickname })]);
    }

    public async Task<(MarketStatus Status, long Earned)> ShipAsync(int slot, int itemId, int quantity, CancellationToken ct)
    {
        using ShipItems request = new() { Slot = (byte)slot, ItemId = itemId, Quantity = quantity };
        ShipResult result = await this.RequestAsync<ShipResult>(request, ct);
        return (result.Status, result.Earned);
    }

    // ---- shop, nickname, tester grant ----
    public async Task<(ShopStatus Status, long Spent)> BuyFromShopAsync(int shopId, int itemId, int quantity, CancellationToken ct)
    {
        using BuyFromShop request = new() { ShopId = shopId, ItemId = itemId, Quantity = quantity };
        ShopResult result = await this.RequestAsync<ShopResult>(request, ct);
        return (result.Status, result.Spent);
    }

    /// <summary>Links a new Google identity to the guest in place (the nickname is chosen here). On success this client carries the new token and nickname.</summary>
    public async Task<AccountResult> UpgradeAsync(string nickname, CancellationToken ct)
    {
        using LinkExternalIdentity request = new()
        {
            Provider = ExternalProvider.Google,
            IdToken = FakeExternalTokenVerifier.Token(ExternalProvider.Google, Guid.NewGuid().ToString("N")),
            Nickname = nickname,
        };
        await WirePace.CredentialAsync(ct);
        AccountResult result = await this.RequestAsync<AccountResult>(request, ct);
        if (result.Status == AccountStatus.Ok)
        {
            this.SessionToken = result.SessionToken;
            this.Nickname = result.Nickname;
        }

        return result;
    }

    public async Task<NicknameResult> SetNicknameAsync(string nickname, CancellationToken ct)
    {
        using SetNickname request = new() { Nickname = nickname };
        return await this.RequestAsync<NicknameResult>(request, ct);
    }

    public async Task<bool> GrantAsync(int itemId, int quantity, long coins, CancellationToken ct)
    {
        using DevGrant request = new() { ItemId = itemId, Quantity = quantity, Coins = coins };
        return (await this.RequestAsync<DevGrantResult>(request, ct)).Ok;
    }

    /// <summary>Sends a tester grant and reports whether the server answered at all (a Production server does not know the opcode).</summary>
    public async Task<bool> GrantIsAnsweredAsync(CancellationToken ct)
    {
        using DevGrant request = new() { ItemId = Nosum.Gameplay.Items.ItemCatalog.OakChair, Quantity = 1 };
        try
        {
            _ = await _session.RequestAsync<DevGrantResult>(request, RequestOptions.Default.WithEncrypt().WithTimeout(1500), ct: ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return false;
        }
    }

    // ---- looks ----
    public async Task<LookRow[]> GetLooksAsync(CancellationToken ct)
    {
        using GetLooks request = new();
        LooksSnapshot snapshot = await this.RequestAsync<LooksSnapshot>(request, ct);
        return [.. snapshot.Looks.Select(Copy)];
    }

    public async Task<LookStatus> SetAppearanceAsync(int skin, int style, int dye, CancellationToken ct)
    {
        using SetAppearance request = new() { SkinId = (byte)skin, HairStyleId = (byte)style, HairDyeId = (byte)dye };
        return (await this.RequestAsync<LookResult>(request, ct)).Status;
    }

    public async Task<LookStatus> EquipAsync(int slot, int itemId, CancellationToken ct)
    {
        using EquipItem request = new() { Slot = (byte)slot, ItemId = itemId };
        return (await this.RequestAsync<LookResult>(request, ct)).Status;
    }

    public async Task<LookStatus> UnequipAsync(int wearSlot, CancellationToken ct)
    {
        using UnequipItem request = new() { WearSlot = (byte)wearSlot };
        return (await this.RequestAsync<LookResult>(request, ct)).Status;
    }

    // ---- trade ----
    public async Task<TradeStatus> TradeRequestAsync(uint target, CancellationToken ct)
    {
        using TradeRequest request = new() { TargetPlayerId = target };
        return (await this.RequestAsync<TradeResult>(request, ct)).Status;
    }

    public async Task<TradeStatus> TradeRespondAsync(bool accept, CancellationToken ct)
    {
        using TradeRespond request = new() { Accept = accept };
        return (await this.RequestAsync<TradeResult>(request, ct)).Status;
    }

    public async Task<TradeStatus> TradeOfferAsync((int ItemId, int Quantity)[] items, long coins, CancellationToken ct)
    {
        using TradeSetOffer request = new() { Coins = coins };
        foreach ((int itemId, int quantity) in items)
        {
            request.Items.Add(new TradeItemRow { ItemId = itemId, Quantity = quantity });
        }

        return (await this.RequestAsync<TradeResult>(request, ct)).Status;
    }

    public async Task<TradeStatus> TradeConfirmAsync(CancellationToken ct)
    {
        using TradeConfirm request = new();
        return (await this.RequestAsync<TradeResult>(request, ct)).Status;
    }

    public async Task<TradeStatus> TradeCancelAsync(CancellationToken ct)
    {
        using TradeCancel request = new();
        return (await this.RequestAsync<TradeResult>(request, ct)).Status;
    }

    // ---- co-op ----
    public async Task<CoopStatus> CreateCoopAsync(string name, CancellationToken ct)
    {
        using CreateCoop request = new() { Name = name };
        return (await this.RequestAsync<CoopResult>(request, ct)).Status;
    }

    public async Task<CoopStatus> CoopInviteAsync(uint target, CancellationToken ct)
    {
        using InvitePlayer request = new() { TargetPlayerId = target };
        return (await this.RequestAsync<CoopResult>(request, ct)).Status;
    }

    public async Task<CoopStatus> CoopRespondAsync(Guid invite, bool accept, CancellationToken ct)
    {
        using RespondInvite request = new() { InviteId = invite, Accept = accept };
        return (await this.RequestAsync<CoopResult>(request, ct)).Status;
    }

    public async Task<CoopStatus> CoopKickAsync(Guid member, CancellationToken ct)
    {
        using KickMember request = new() { MemberId = member };
        return (await this.RequestAsync<CoopResult>(request, ct)).Status;
    }

    public async Task<CoopStatus> DepositAsync(int itemId, int quantity, CancellationToken ct)
    {
        using DepositItem request = new() { ItemId = itemId, Quantity = quantity };
        return (await this.RequestAsync<CoopResult>(request, ct)).Status;
    }

    public async Task<CoopStatus> WithdrawAsync(int itemId, int quantity, CancellationToken ct)
    {
        using WithdrawItem request = new() { ItemId = itemId, Quantity = quantity };
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

    private static LookRow Copy(LookRow p) => new() { PlayerId = p.PlayerId, SkinId = p.SkinId, HairStyleId = p.HairStyleId, HairDyeId = p.HairDyeId, ShirtItemId = p.ShirtItemId, PantsItemId = p.PantsItemId, HatItemId = p.HatItemId, Nickname = p.Nickname };

    private static async Task PaceGuestAsync(CancellationToken ct)
    {
        await s_pace.WaitAsync(ct);
        try
        {
            long wait = (s_lastGuest + 1300) - Environment.TickCount64;
            if (wait > 0)
            {
                await Task.Delay((int)wait, ct);
            }

            s_lastGuest = Environment.TickCount64;
        }
        finally
        {
            _ = s_pace.Release();
        }
    }

    // Every client shares one address, so one rate limit: space the requests of all of them.
    private async ValueTask<TReply> RequestAsync<TReply>(Nalix.Abstractions.Networking.Packets.IPacket request, CancellationToken ct)
        where TReply : class, Nalix.Abstractions.Networking.Packets.IPacket, Nalix.Abstractions.Networking.Packets.IPacketStaticOpcode
    {
        await s_pace.WaitAsync(ct);
        try
        {
            long wait = (s_lastRequest + 220) - Environment.TickCount64;
            if (wait > 0)
            {
                await Task.Delay((int)wait, ct);
            }

            s_lastRequest = Environment.TickCount64;
        }
        finally
        {
            _ = s_pace.Release();
        }

        return await _session.RequestAsync<TReply>(request, ct: ct);
    }

    /// <summary>Pushes arrive on socket threads; tests read them in order and skip what does not match.</summary>
    public sealed class Inbox<T>
    {
        private readonly Channel<T> _items = Channel.CreateUnbounded<T>();

        public void Add(T item) => _ = _items.Writer.TryWrite(item);

        public async Task<T> WaitAsync(Func<T, bool> predicate, CancellationToken ct)
        {
            while (true)
            {
                T item = await _items.Reader.ReadAsync(ct);
                if (predicate(item))
                {
                    return item;
                }
            }
        }

        public bool TryTake(out T item) => _items.Reader.TryRead(out item!);
    }
}
