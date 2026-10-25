// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Market;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Market;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Gameplay.Shops;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Stands service tests at the market counter in Town (docs/adr/ADR-034): the market refuses listing, buying and cancelling elsewhere. <see cref="At"/> is a connection whose
/// player stands on the counter tile, <see cref="Away"/> one standing on the Town spawn; the player-id overloads below use <see cref="At"/>, so a test that is not about the
/// counter keeps reading as it did.
/// </summary>
internal static class MarketCounter
{
    /// <summary>Gets a connection whose player stands at the market counter.</summary>
    public static IConnection At { get; } = DispatchProxy.Create<IConnection, ConnectionStub>();

    /// <summary>Gets a connection whose player stands in Town, far from the market counter.</summary>
    public static IConnection Away { get; } = DispatchProxy.Create<IConnection, ConnectionStub>();

    /// <summary>Gets the pixel centre of the market counter tile in Town.</summary>
    public static (double X, double Y) CounterPixels
    {
        get
        {
            _ = GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
            ShopPoint point = town!.Shops.Single(p => p.ShopId == MarketRules.MarketCounterId);
            return ((point.TileX * 16) + 8, (point.TileY * 16) + 8);
        }
    }

    /// <summary>Gets the place a wire client starts at to stand at the counter (<c>SocialClient.ConnectAsync</c>'s <c>startAt</c>).</summary>
    public static (string Map, double X, double Y) Place => (GameMaps.Town, CounterPixels.X, CounterPixels.Y);

    /// <summary>Gets a presence that puts <see cref="At"/> on the counter and <see cref="Away"/> on the Town spawn.</summary>
    public static IMapPresence Presence() => new CounterPresence();

    /// <summary>Gets a catalogue of blank 16 px maps (only the tile size matters to the counter check).</summary>
    public static IMapCatalog Maps() => new BlankMaps();

    /// <summary>Lists a stack as a player standing at the counter.</summary>
    public static Task<MarketStatus> CreateListingAsync(this IMarketService market, Guid playerId, int slot, int itemId, int quantity, long price, CancellationToken cancellationToken = default)
        => market.CreateListingAsync(At, playerId, slot, itemId, quantity, price, cancellationToken);

    /// <summary>Buys a listing as a player standing at the counter.</summary>
    public static Task<MarketStatus> BuyAsync(this IMarketService market, Guid buyerId, Guid listingId, CancellationToken cancellationToken = default)
        => market.BuyAsync(At, buyerId, listingId, cancellationToken);

    /// <summary>Cancels a listing as a player standing at the counter.</summary>
    public static Task<MarketStatus> CancelAsync(this IMarketService market, Guid playerId, Guid listingId, CancellationToken cancellationToken = default)
        => market.CancelAsync(At, playerId, listingId, cancellationToken);

    private class ConnectionStub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
    }

    private sealed class BlankMaps : IMapCatalog
    {
        public bool TryGet(string mapId, [NotNullWhen(true)] out MapDefinition? map)
        {
            map = new MapDefinition(mapId, new WalkGrid(130, 110, 16, new bool[130 * 110]), 0, 0);
            return true;
        }
    }

    private sealed class CounterPresence : IMapPresence
    {
        public event Func<PlayerPresence, ValueTask>? PlayerLeft
        {
            add { }
            remove { }
        }

        public int PlayerCount => 0;

        public ValueTask<PlayerPresence> JoinAsync(IConnection connection, Guid accountId, MapInstance instance, MotionState motion, uint? playerId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask LeaveAsync(IConnection connection, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public bool TryGet(IConnection connection, [NotNullWhen(true)] out PlayerPresence? player)
        {
            _ = GameMaps.TryGet(GameMaps.Town, out MapSpec? town);
            (double x, double y) = ReferenceEquals(connection, Away) ? (town!.SpawnTileX * 16d, town.SpawnTileY * 16d) : CounterPixels;
            player = new PlayerPresence(1, Guid.Empty, new MapInstance(GameMaps.Town, Guid.Empty), connection, new MotionState(x, y, 0));
            return true;
        }

        public void CopyActiveMapIds(List<string> into)
        {
        }

        public void CopyPlayers(string mapId, List<PlayerPresence> into)
        {
        }

        public ValueTask SendAsync(IConnection connection, IPacket packet, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask MulticastAsync(string mapId, IPacket packet, IConnection? except = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
