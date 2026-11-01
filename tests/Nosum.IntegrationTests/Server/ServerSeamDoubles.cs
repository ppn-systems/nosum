// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Presence;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// A saved-place service whose <c>LeaveAsync</c> (the save when a player leaves) lands only after a delay. Every other call forwards at once.
/// Used to prove that a leaving player's record is gone only once the place is saved (docs/perf/server-perf.md).
/// </summary>
public class SlowLeaveStates : DispatchProxy
{
    /// <summary>Gets or sets the real service the calls forward to.</summary>
    public IPlayerStateService Target { get; set; } = null!;

    /// <summary>Gets or sets how long the save waits before it lands.</summary>
    public TimeSpan SaveDelay { get; set; }

    /// <summary>Wraps <paramref name="target"/>; the save of every leaving player waits for <paramref name="saveDelay"/>.</summary>
    public static IPlayerStateService Wrap(IPlayerStateService target, TimeSpan saveDelay)
    {
        SlowLeaveStates proxy = (SlowLeaveStates)(object)DispatchProxy.Create<IPlayerStateService, SlowLeaveStates>()!;
        proxy.Target = target;
        proxy.SaveDelay = saveDelay;
        return (IPlayerStateService)(object)proxy;
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        return targetMethod.Name == nameof(IPlayerStateService.LeaveAsync)
            ? this.SaveAfterDelayAsync(targetMethod, args)
            : Forward(this.Target, targetMethod, args);
    }

    private async Task SaveAfterDelayAsync(MethodInfo targetMethod, object?[]? args)
    {
        await Task.Delay(this.SaveDelay).ConfigureAwait(false);
        await ((Task)Forward(this.Target, targetMethod, args)!).ConfigureAwait(false);
    }

    internal static object? Forward(object target, MethodInfo method, object?[]? args)
    {
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}

/// <summary>
/// A presence whose snapshot send to the players of the listed accounts never completes (a stalled client): the send waits until its cancellation
/// token fires. Everything else forwards to the real presence.
/// </summary>
public class StallingPresence : DispatchProxy
{
    /// <summary>Gets or sets the real presence the calls forward to.</summary>
    public IMapPresence Target { get; set; } = null!;

    /// <summary>Gets or sets the accounts whose sends stall. An account added later stalls from its next send.</summary>
    public ConcurrentDictionary<Guid, byte> Stalled { get; set; } = new();

    /// <summary>Wraps <paramref name="target"/>; the sends to the accounts in <paramref name="stalled"/> hang until cancelled.</summary>
    public static IMapPresence Wrap(IMapPresence target, ConcurrentDictionary<Guid, byte> stalled)
    {
        StallingPresence proxy = (StallingPresence)(object)DispatchProxy.Create<IMapPresence, StallingPresence>()!;
        proxy.Target = target;
        proxy.Stalled = stalled;

        return (IMapPresence)(object)proxy;
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.Name == nameof(IMapPresence.SendAsync)
            && args is { Length: 3 } && args[0] is IConnection connection && args[2] is CancellationToken token
            && this.Target.TryGet(connection, out PlayerPresence? player) && this.Stalled.ContainsKey(player.AccountId))
        {
            return new ValueTask(Task.Delay(Timeout.Infinite, token));
        }

        return SlowLeaveStates.Forward(this.Target, targetMethod, args);
    }
}
