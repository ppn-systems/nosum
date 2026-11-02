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
    private readonly HashSet<Type> _held = [];
    private readonly List<Action> _heldAnswers = [];

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

    /// <summary>Called with each request while it is still alive (request packets are pooled and reused after the call); for tests that read a request's fields.</summary>
    public Action<IPacket>? RequestSeen { get; set; }

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

    /// <summary>Holds the answers to requests that expect <typeparamref name="TResponse"/> until <see cref="ReleaseOldestHeld"/> is called.</summary>
    public void HoldReplies<TResponse>()
        where TResponse : class, IPacket
        => _ = _held.Add(typeof(TResponse));

    /// <summary>Gives the oldest held answer, which runs whatever waits on it (the next request is recorded inside this call).</summary>
    /// <returns><see langword="false"/> when no answer is held.</returns>
    public bool ReleaseOldestHeld()
    {
        if (_heldAnswers.Count == 0)
        {
            return false;
        }

        Action answer = _heldAnswers[0];
        _heldAnswers.RemoveAt(0);
        answer();
        return true;
    }

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
        this.RequestSeen?.Invoke(request);
        if (_held.Contains(typeof(TResponse)) && _replies.TryGetValue(typeof(TResponse), out Func<IPacket, IPacket>? held))
        {
            TaskCompletionSource<TResponse> answer = new();
            TResponse value = (TResponse)held(request);
            _heldAnswers.Add(() => answer.SetResult(value));
            return new ValueTask<TResponse>(answer.Task);
        }

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
