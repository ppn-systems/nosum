// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Framework.Injection;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;
using Nosum.Abstractions.Maps;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.C2S;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.GroundItem;
using Nosum.Contracts.GroundItem.C2S;
using Nosum.Contracts.GroundItem.S2C;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.C2S;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.C2S;
using Nosum.Contracts.Quest.S2C;
using Nosum.Contracts.Season.C2S;
using Nosum.Contracts.Season.S2C;
using Nosum.Gameplay.Farm;
using Nosum.Host;
using Nosum.Infrastructure.Persistence;

namespace Nosum.IntegrationTests.Server;

public sealed partial class FarmWireTests
{
    // The tests share one database: start each with an untouched farm.
    private async Task ResetFarmAsync()
    {
        await using NosumDbContext context = new(_database.Options);
        _ = await context.FarmPlots.ExecuteDeleteAsync();
    }

    // Tiles whose centre is within reach of the spawn feet, nearest first.
    private static IEnumerable<(int X, int Y)> NearbyTiles(JoinMapResult join)
    {
        int tx = (int)(join.SpawnX / 16);
        int ty = (int)(join.SpawnY / 16);
        return Enumerable.Range(-2, 5)
            .SelectMany(dx => Enumerable.Range(-2, 5).Select(dy => (X: tx + dx, Y: ty + dy)))
            .Where(t => FarmRules.InReach(join.SpawnX, join.SpawnY, t.X, t.Y, 16))
            .OrderBy(t => Math.Abs(((t.X + 0.5) * 16) - join.SpawnX) + Math.Abs(((t.Y + 0.5) * 16) - join.SpawnY));
    }

    private static (int X, int Y) TileInReachOf(JoinMapResult join)
    {
        IMapCatalog maps = InstanceManager.Instance.GetExistingInstance<IMapCatalog>()!;
        _ = maps.TryGet(Program.FarmMapId, out MapDefinition? map);
        return NearbyTiles(join).First(t => map!.Tillable!.IsTillable(t.X, t.Y));
    }

    private static ushort GetFreePort()
    {
        using TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        return (ushort)((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private sealed class FarmClient : IAsyncDisposable
    {
        private static readonly TimeSpan s_useCooldown = TimeSpan.FromMilliseconds(330);

        private readonly WebSocketSession _session;
        private readonly IDisposable[] _subscriptions;
        private readonly Inbox<FarmTileRow> _tiles = new();
        private readonly Inbox<InventorySlotRow> _slots = new();
        private readonly Inbox<QuestRow> _quests = new();
        private readonly Inbox<bool> _prompt = new();
        private readonly Inbox<GroundItemRow> _spawned = new();
        private readonly Inbox<GroundItemTaken> _taken = new();

        private FarmClient(WebSocketSession session)
        {
            _session = session;
            _subscriptions =
            [
                session.On<FarmTilesChanged>(p =>
                {
                    foreach (FarmTileRow t in p.Tiles)
                    {
                        _tiles.Add(new FarmTileRow { X = t.X, Y = t.Y, CropId = t.CropId, PlantedAtMs = t.PlantedAtMs, GrowthDays = t.GrowthDays, WateredDay = t.WateredDay, Removed = t.Removed });
                    }
                }),
                session.On<InventoryUpdate>(p =>
                {
                    foreach (InventorySlotRow s in p.Slots)
                    {
                        _slots.Add(new InventorySlotRow { Slot = s.Slot, ItemId = s.ItemId, Quantity = s.Quantity });
                    }
                }),
                session.On<QuestUpdate>(p => _quests.Add(new QuestRow { QuestId = p.Quest.QuestId, State = p.Quest.State, Progress = [.. p.Quest.Progress] })),
                session.On<RegistrationPromptUnlocked>(_ => _prompt.Add(true)),
                session.On<GroundItemSpawned>(p => _spawned.Add(new GroundItemRow { Id = p.Item.Id, ItemId = p.Item.ItemId, Quantity = p.Item.Quantity, X = p.Item.X, Y = p.Item.Y })),
                session.On<GroundItemTaken>(p => _taken.Add(new GroundItemTaken { Id = p.Id, Reason = p.Reason, ToX = p.ToX, ToY = p.ToY })),
            ];
        }

        public string SessionToken { get; private set; } = string.Empty;

        public static async Task<FarmClient> ConnectAsync(ushort port, CancellationToken ct)
        {
            FarmClient client = await OpenAsync(port, ct);
            await WirePace.WaitAsync(ct);
            using StartGuest request = new() { PreferredLanguage = Nosum.Contracts.Common.GameLanguage.English };
            AccountResult result = await client._session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            client.SessionToken = result.SessionToken;
            return client;
        }

        public static async Task<FarmClient> ResumeAsync(ushort port, string token, CancellationToken ct)
        {
            FarmClient client = await OpenAsync(port, ct);
            await WirePace.WaitAsync(ct);
            using ResumeSession request = new() { SessionToken = token };
            AccountResult result = await client._session.RequestAsync<AccountResult>(request, ct: ct);
            result.Status.Should().Be(AccountStatus.Ok);
            client.SessionToken = token;
            return client;
        }

        public async Task<JoinMapResult> JoinAsync(CancellationToken ct)
        {
            await WirePace.JoinAsync(ct);
            using JoinMap request = new() { MapId = Program.FarmMapId };
            JoinMapResult result = await _session.RequestAsync<JoinMapResult>(request, ct: ct);
            result.Status.Should().Be(JoinStatus.Ok);
            return result;
        }

        public async Task<CalendarInfo> GetCalendarAsync(CancellationToken ct)
        {
            using GetCalendar request = new();
            return await _session.RequestAsync<CalendarInfo>(request, ct: ct);
        }

        public async Task<InventorySnapshot> GetInventoryAsync(CancellationToken ct)
        {
            using GetInventory request = new();
            return await _session.RequestAsync<InventorySnapshot>(request, ct: ct);
        }

        public async Task<FarmSnapshot> GetFarmAsync(CancellationToken ct)
        {
            using GetFarm request = new();
            return await _session.RequestAsync<FarmSnapshot>(request, ct: ct);
        }

        public async Task<bool> GrantAsync(int itemId, int quantity, CancellationToken ct)
        {
            using DevGrant request = new() { ItemId = itemId, Quantity = quantity };
            return (await _session.RequestAsync<DevGrantResult>(request, ct: ct)).Ok;
        }

        public async Task<DevAdvanceClockResult> AdvanceClockAsync(int days, CancellationToken ct, TimeSpan? limit = null)
        {
            using DevAdvanceClock request = new() { Days = days };
            RequestOptions options = RequestOptions.Default.WithEncrypt();
            return await _session.RequestAsync<DevAdvanceClockResult>(request, limit is { } wait ? options.WithTimeout((int)wait.TotalMilliseconds) : options, ct: ct);
        }

        public async Task<GroundItemsSnapshot> GetGroundItemsAsync(CancellationToken ct)
        {
            using GetGroundItems request = new();
            return await _session.RequestAsync<GroundItemsSnapshot>(request, ct: ct);
        }

        public Task<GroundItemRow> WaitForGroundItemAsync(Func<GroundItemRow, bool> predicate, CancellationToken ct) => _spawned.WaitAsync(predicate, ct);

        public async Task<QuestLog> GetQuestsAsync(CancellationToken ct)
        {
            using GetQuests request = new();
            return await _session.RequestAsync<QuestLog>(request, ct: ct);
        }

        // Like the real client, wait out the handler's rate limit (8 per second per address, burst 3): tests share one address.
        public async Task<UseItemResult> UseAsync(int slot, int x, int y, CancellationToken ct)
        {
            await Task.Delay(s_useCooldown, ct);
            using UseItem request = new() { Slot = (byte)slot, TileX = x, TileY = y };
            return await _session.RequestAsync<UseItemResult>(request, ct: ct);
        }

        // Sent with no wait: the server's interval refuses it (docs/adr/ADR-072 D2).
        public async Task<UseItemResult> UseWithoutWaitAsync(int slot, int x, int y, CancellationToken ct)
        {
            using UseItem request = new() { Slot = (byte)slot, TileX = x, TileY = y };
            return await _session.RequestAsync<UseItemResult>(request, ct: ct);
        }

        // Nothing must arrive: a push meant for another player's farm would leak here.
        public async Task ExpectNoTilePushAsync(TimeSpan wait, CancellationToken ct)
        {
            using CancellationTokenSource shortWait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            shortWait.CancelAfter(wait);
            Func<Task> next = async () => _ = await _tiles.WaitAsync(_ => true, shortWait.Token);
            _ = await next.Should().ThrowAsync<OperationCanceledException>();
        }

        public Task<FarmTileRow> WaitForTileAsync(int x, int y, Func<FarmTileRow, bool> predicate, CancellationToken ct)
            => _tiles.WaitAsync(t => t.X == x && t.Y == y && predicate(t), ct);

        public Task<InventorySlotRow> WaitForSlotAsync(Func<InventorySlotRow, bool> predicate, CancellationToken ct) => _slots.WaitAsync(predicate, ct);

        public Task<QuestRow> WaitForQuestAsync(Func<QuestRow, bool> predicate, CancellationToken ct) => _quests.WaitAsync(predicate, ct);

        public Task<bool> WaitForPromptUnlockedAsync(CancellationToken ct) => _prompt.WaitAsync(_ => true, ct);

        public async ValueTask DisposeAsync()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            await _session.DisconnectAsync();
            _session.Dispose();
        }

        private static async Task<FarmClient> OpenAsync(ushort port, CancellationToken ct)
        {
            WebSocketSession session = new(
                new TransportOptions { Address = "127.0.0.1", Port = port },
                new WebSocketTransportOptions { Path = Program.WebSocketPath });
            FarmClient client = new(session);
            await session.ConnectAsync(ct: ct);
            await session.HandshakeAsync(ct);
            return client;
        }
    }

    // Pushes arrive on socket threads; tests read them in order and skip what does not match.
    private sealed class Inbox<T>
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
    }
}
