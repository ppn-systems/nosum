// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Emote.S2C;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// A presence whose <c>EmoteShown</c> sends to the listed accounts never complete until cancelled: a client that stops taking the emotes of the
/// place it stands in, while its connection and its snapshots stay healthy. Everything else forwards to the real presence. <see cref="Stalls"/>
/// records the sends that were held, so a test can prove the stall was reached.
/// </summary>
public class EmoteStallingPresence : DispatchProxy
{
    private ConcurrentBag<Guid>? _stalls;

    /// <summary>Gets or sets the real presence the calls forward to.</summary>
    public IMapPresence Target { get; set; } = null!;

    /// <summary>Gets or sets the accounts whose emote sends hang.</summary>
    public ConcurrentDictionary<Guid, byte> Stalled { get; set; } = new();

    /// <summary>Wraps <paramref name="target"/>; emote sends to the accounts in <paramref name="stalled"/> hang until cancelled.</summary>
    public static IMapPresence Wrap(IMapPresence target, ConcurrentDictionary<Guid, byte> stalled, ConcurrentBag<Guid> stalls)
    {
        EmoteStallingPresence proxy = (EmoteStallingPresence)(object)DispatchProxy.Create<IMapPresence, EmoteStallingPresence>()!;
        proxy.Target = target;
        proxy.Stalled = stalled;
        proxy._stalls = stalls;
        return (IMapPresence)(object)proxy;
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.Name == nameof(IMapPresence.SendAsync)
            && args is { Length: 3 } && args[0] is IConnection connection && args[1] is EmoteShown && args[2] is CancellationToken token
            && this.Target.TryGet(connection, out PlayerPresence? player) && this.Stalled.ContainsKey(player.AccountId))
        {
            _stalls?.Add(player.AccountId);
            return new ValueTask(Task.Delay(Timeout.Infinite, token));
        }

        return SlowLeaveStates.Forward(this.Target, targetMethod, args);
    }
}
