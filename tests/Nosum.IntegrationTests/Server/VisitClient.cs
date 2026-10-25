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
using Nalix.Abstractions.Networking.Packets;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Visit;
using Nosum.Contracts.Visit.C2S;
using Nosum.Contracts.Visit.S2C;
using Nosum.Contracts.World.S2C;
using Nosum.Host;

namespace Nosum.IntegrationTests.Server;

/// <summary>A visitors snapshot as a client received it.</summary>
internal sealed record VisitorsCopy(bool CoopMembersMayVisit, bool IsGuest, VisitorRow[] Visitors);

/// <summary>
/// A real Nalix WebSocket client for the visiting tests: signs in with a session token (the accounts are created through the account service,
/// because sign-ups are rate limited per address), joins, walks, farms, arranges furniture, ships and speaks every visit packet.
/// Every client of a run shares one address, so one static gate spaces the requests of all of them.
/// </summary>
internal sealed class VisitClient : IAsyncDisposable
{
    private static readonly SemaphoreSlim s_pace = new(1, 1);
    private static readonly long[] s_last = new long[2];
    private const int Request = 0;
    private const int Resume = 1;

    private readonly WebSocketSession _session;
    private readonly IDisposable[] _subscriptions;
    private DateTime _lastMove = DateTime.MinValue;

    private VisitClient(WebSocketSession session)
    {
        _session = session;
        _subscriptions =
        [
            session.On<MapChanged>(m => this.MapChanges.Add(new MapChanged { PlayerId = m.PlayerId, MapId = m.MapId, X = m.X, Y = m.Y, Facing = m.Facing })),
            session.On<MapSnapshot>(s => this.Snapshots.Add([.. s.Players.Select(p => p.PlayerId)])),
            session.On<MoveCorrection>(c => this.Corrections.Add(c.Reason)),
            session.On<FarmTilesChanged>(p =>
            {
                foreach (FarmTileRow t in p.Tiles)
                {
                    this.Tiles.Add(new FarmTileRow { X = t.X, Y = t.Y, CropId = t.CropId, PlantedAtMs = t.PlantedAtMs, GrowthMs = t.GrowthMs, SettledAtMs = t.SettledAtMs, WetUntilMs = t.WetUntilMs });
                }
            }),
        ];
    }

    public Guid AccountId { get; private set; }

    public string SessionToken { get; private set; } = string.Empty;

    /// <summary>Gets the id the player has on the map (the server keeps it across map changes).</summary>
    public uint MapPlayerId { get; private set; }

    public Inbox<MapChanged> MapChanges { get; } = new();

    public Inbox<uint[]> Snapshots { get; } = new();

    public Inbox<MoveRefusal> Corrections { get; } = new();

    public Inbox<FarmTileRow> Tiles { get; } = new();

    public static async Task<VisitClient> ConnectAsync(ushort port, string sessionToken, CancellationToken ct)
    {
        WebSocketSession session = new(
            new TransportOptions { Address = "127.0.0.1", Port = port },
            new WebSocketTransportOptions { Path = Program.WebSocketPath });
        VisitClient client = new(session);
        await session.ConnectAsync(ct: ct);
        await session.HandshakeAsync(ct);

        // Resuming a session is limited to two a second per address.
        await WaitAsync(Resume, 700, ct);
        using ResumeSession request = new() { SessionToken = sessionToken };
        AccountResult result = await session.RequestAsync<AccountResult>(request, ct: ct);
        result.Status.Should().Be(AccountStatus.Ok);
        client.AccountId = result.PlayerId;
        client.SessionToken = sessionToken;
        return client;
    }

    public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
    {
        using JoinMap request = new() { MapId = Program.FarmMapId };
        JoinMapResult result = await this.RequestAsync<JoinMapResult>(request, ct);
        result.Status.Should().Be(JoinStatus.Ok);
        this.MapPlayerId = result.PlayerId;
        return result;
    }

    public async Task SendMoveAsync(uint sequence, float x, float y, byte facing, CancellationToken ct)
    {
        // The real client sends about every 100 ms; the host drops intents that arrive right behind one another.
        TimeSpan wait = TimeSpan.FromMilliseconds(130) - (DateTime.UtcNow - _lastMove);
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, ct);
        }

        _lastMove = DateTime.UtcNow;
        using MoveIntent intent = new() { Sequence = sequence, X = x, Y = y, Facing = facing, Moving = true };
        await _session.SendAsync(intent, ct);
    }

    public async Task<bool> GrantAsync(int itemId, int quantity, CancellationToken ct)
    {
        using DevGrant request = new() { ItemId = itemId, Quantity = quantity };
        return (await this.RequestAsync<DevGrantResult>(request, ct)).Ok;
    }

    public async Task<InventorySnapshot> GetInventoryAsync(CancellationToken ct)
    {
        using GetInventory request = new();
        return await this.RequestAsync<InventorySnapshot>(request, ct);
    }

    // ---- farm ----
    public async Task<UseItemResult> UseAsync(int slot, int x, int y, CancellationToken ct)
    {
        using UseItem request = new() { Slot = (byte)slot, TileX = x, TileY = y };
        return await this.RequestAsync<UseItemResult>(request, ct);
    }

    public async Task<FarmSnapshot> GetFarmAsync(CancellationToken ct)
    {
        using GetFarm request = new();
        return await this.RequestAsync<FarmSnapshot>(request, ct);
    }

    // ---- furniture and shipping ----
    public async Task<PlaceResult> PlaceAsync(int slot, int x, int y, int rotation, CancellationToken ct)
    {
        using PlaceObject request = new() { Slot = (byte)slot, TileX = x, TileY = y, Rotation = (byte)rotation };
        return await this.RequestAsync<PlaceResult>(request, ct);
    }

    public async Task<PlaceResult> MoveObjectAsync(int objectId, int x, int y, int rotation, CancellationToken ct)
    {
        using MoveObject request = new() { ObjectId = objectId, TileX = x, TileY = y, Rotation = (byte)rotation };
        return await this.RequestAsync<PlaceResult>(request, ct);
    }

    public async Task<PlaceResult> PickUpAsync(int objectId, CancellationToken ct)
    {
        using PickUpObject request = new() { ObjectId = objectId };
        return await this.RequestAsync<PlaceResult>(request, ct);
    }

    public async Task<PlacedSnapshot> GetPlacedAsync(CancellationToken ct)
    {
        using GetPlaced request = new();
        return await this.RequestAsync<PlacedSnapshot>(request, ct);
    }

    public async Task<ShipResult> ShipAsync(int slot, int itemId, int quantity, CancellationToken ct)
    {
        using ShipItems request = new() { Slot = (byte)slot, ItemId = itemId, Quantity = quantity };
        return await this.RequestAsync<ShipResult>(request, ct);
    }

    // ---- visiting ----
    public async Task<VisitorsCopy> GetVisitorsAsync(CancellationToken ct)
    {
        using GetVisitors request = new();
        VisitorsSnapshot reply = await this.RequestAsync<VisitorsSnapshot>(request, ct);
        return new VisitorsCopy(reply.CoopMembersMayVisit, reply.IsGuest, [.. reply.Visitors.Select(v => new VisitorRow { PlayerId = v.PlayerId, Nickname = v.Nickname, Listed = v.Listed, Present = v.Present })]);
    }

    public async Task<VisitStatus> AddVisitorAsync(string nickname, CancellationToken ct)
    {
        using AddVisitor request = new() { Nickname = nickname };
        return (await this.RequestAsync<VisitResult>(request, ct)).Status;
    }

    public async Task<VisitStatus> RemoveVisitorAsync(Guid playerId, CancellationToken ct)
    {
        using RemoveVisitor request = new() { PlayerId = playerId };
        return (await this.RequestAsync<VisitResult>(request, ct)).Status;
    }

    public async Task<VisitStatus> KickVisitorAsync(Guid playerId, CancellationToken ct)
    {
        using KickVisitor request = new() { PlayerId = playerId };
        return (await this.RequestAsync<VisitResult>(request, ct)).Status;
    }

    public async Task<VisitStatus> SetCoopVisitsAsync(bool allowed, CancellationToken ct)
    {
        using SetCoopVisits request = new() { Allowed = allowed };
        return (await this.RequestAsync<VisitResult>(request, ct)).Status;
    }

    public async Task<VisitStatus> VisitAsync(string nickname, CancellationToken ct)
    {
        using VisitPlayer request = new() { Nickname = nickname };
        return (await this.RequestAsync<VisitResult>(request, ct)).Status;
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

        public Task<T> WaitAsync(CancellationToken ct) => this.WaitAsync(_ => true, ct);

        /// <summary>Asserts that nothing matching arrives within a time.</summary>
        public async Task ExpectNoneAsync(Func<T, bool> predicate, TimeSpan time, CancellationToken ct)
        {
            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(time);
            try
            {
                T unexpected = await this.WaitAsync(predicate, limit.Token);
                throw new InvalidOperationException("Unexpected " + typeof(T).Name + ": " + unexpected);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Nothing matching arrived in time.
            }
        }
    }
}
