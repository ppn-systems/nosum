// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Contracts.Market;
using Nosum.Contracts.Market.C2S;
using Nosum.Contracts.Market.S2C;
using Nosum.Wasm.Socket;
using Nosum.Wasm.Socket.Market;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// Browse replies are shown in the order the panel asked, not the order they arrive: a late reply to an older browse must not replace the page the
/// panel asked for last (docs/adr/ADR-101 amendment 2026-11-01, the page size the panel measures to).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The pages live as long as the test and the channel that reads them.")]
public sealed class MarketBrowseOrderTests
{
    [Fact]
    public async Task Browse_OlderReplyArrivesLast_TheNewestPageStays()
    {
        DeferredSocket socket = new();
        MarketChannel channel = new(socket);
        channel.Browse(0, [], MarketSort.Newest, 10); // asked first: ten rows
        channel.Browse(0, [], MarketSort.Newest, 7);  // asked after: seven rows, the size the panel measured

        socket.Answer(1, Page(7));
        await WaitForAsync(() => channel.Browsed.Listings.Count == 7);
        socket.Answer(0, Page(10));
        await Task.Delay(50);

        channel.Browsed.Listings.Should().HaveCount(7, "the older reply came last, but the panel asked for seven");
        channel.Browsed.PageSize.Should().Be(7);
    }

    [Fact]
    public async Task Browse_OlderReplyArrivesFirst_IsNotShownEither()
    {
        DeferredSocket socket = new();
        MarketChannel channel = new(socket);
        channel.Browse(0, [], MarketSort.Newest, 10);
        channel.Browse(0, [], MarketSort.Newest, 7);

        socket.Answer(0, Page(10));
        await Task.Delay(50);
        channel.Browsed.Listings.Should().BeEmpty("the page for the older ask is not the one the panel shows");

        socket.Answer(1, Page(7));
        await WaitForAsync(() => channel.Browsed.Listings.Count == 7);
        channel.Browsed.PageSize.Should().Be(7, "the page records the size it was read in, so the pager counts in that size");
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue("the reply should have been read");
    }

    private static ListingsPage Page(int rows)
    {
        ListingsPage page = new() { Total = 20 };
        for (int i = 0; i < rows; i++)
        {
            page.Rows.Add(new ListingRow { ListingId = Guid.NewGuid(), ItemId = 200, Quantity = 1, Price = 30 + i, SellerNickname = "Mai" });
        }

        return page;
    }

    /// <summary>A socket whose requests wait until the test answers them, in the order the test chooses.</summary>
    private sealed class DeferredSocket : IGameSocket
    {
        private readonly List<TaskCompletionSource<IPacket>> _pending = [];

        public event Action<GameSocketState>? StateChanged
        {
            add { }
            remove { }
        }

        public event Action? Ready
        {
            add { }
            remove { }
        }

        public event Action<TimeSpan>? ReconnectScheduled
        {
            add { }
            remove { }
        }

        public GameSocketState State => GameSocketState.Ready;

        /// <summary>Answers the request asked <paramref name="index"/>-th (zero based).</summary>
        public void Answer(int index, IPacket reply) => _pending[index].SetResult(reply);

        public void Start()
        {
        }

        public void RetryNow()
        {
        }

        public Task WaitReadyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async ValueTask<TResponse> RequestAsync<TResponse>(IPacket request, CancellationToken cancellationToken = default)
            where TResponse : class, IPacket, IPacketStaticOpcode
        {
            TaskCompletionSource<IPacket> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(reply);
            return (TResponse)await reply.Task.ConfigureAwait(false);
        }

        public ValueTask SendAsync(IPacket packet, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public IDisposable Subscribe<TPacket>(Action<TPacket> handler)
            where TPacket : class, IPacket, IPacketStaticOpcode => new NoSubscription();

        private sealed class NoSubscription : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
