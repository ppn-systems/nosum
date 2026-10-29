// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Wasm.Socket;

namespace Nosum.UnitTests.Wasm;

/// <summary>A game socket tests drive by hand: canned replies to requests, pushes delivered on demand, every request recorded.</summary>
internal sealed class FakeGameSocket : IGameSocket
{
    private readonly Dictionary<Type, Func<IPacket, IPacket>> _replies = [];
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];

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

    public List<IPacket> Requests { get; } = [];

    public void Start()
    {
    }

    public void RetryNow()
    {
    }

    public Task WaitReadyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Answers requests that expect <typeparamref name="TResponse"/> with what <paramref name="reply"/> builds from the request.</summary>
    public void ReplyWith<TResponse>(Func<IPacket, TResponse> reply)
        where TResponse : class, IPacket
        => _replies[typeof(TResponse)] = request => reply(request);

    /// <summary>Delivers a push to the subscribers of its type.</summary>
    public void Push<TPacket>(TPacket packet)
        where TPacket : class, IPacket
    {
        if (_handlers.TryGetValue(typeof(TPacket), out List<Delegate>? handlers))
        {
            foreach (Delegate handler in handlers.ToArray())
            {
                ((Action<TPacket>)handler)(packet);
            }
        }
    }

    public ValueTask<TResponse> RequestAsync<TResponse>(IPacket request, CancellationToken cancellationToken = default)
        where TResponse : class, IPacket, IPacketStaticOpcode
    {
        // Pooled request packets are returned right after the call: keep what the tests need to read.
        this.Requests.Add(request);
        return _replies.TryGetValue(typeof(TResponse), out Func<IPacket, IPacket>? reply)
            ? ValueTask.FromResult((TResponse)reply(request))
            : throw new InvalidOperationException("No canned reply for " + typeof(TResponse).Name);
    }

    public ValueTask SendAsync(IPacket packet, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public IDisposable Subscribe<TPacket>(Action<TPacket> handler)
        where TPacket : class, IPacket, IPacketStaticOpcode
    {
        if (!_handlers.TryGetValue(typeof(TPacket), out List<Delegate>? list))
        {
            list = [];
            _handlers[typeof(TPacket)] = list;
        }

        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action _dispose;

        public Subscription(Action dispose) => _dispose = dispose;

        public void Dispose() => _dispose();
    }
}
