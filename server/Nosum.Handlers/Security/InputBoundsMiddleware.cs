// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Middleware;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;

namespace Nosum.Handlers.Security;

/// <summary>
/// Drops a client packet that breaks the bounds its type declares (<see cref="PacketBounds"/>) before any handler, permission check or
/// rate limit sees it (docs/adr/ADR-032 D3). A legitimate client never sends one, so there is no reply; the connection's error count rises, which lets
/// the transport's own policy deal with a client that keeps doing it.
/// </summary>
[MiddlewareOrder(-60)]
[MiddlewareStage(MiddlewareStage.Inbound)]
public sealed class InputBoundsMiddleware : IPacketMiddleware<IPacket>
{
    private static long s_dropped;

    /// <summary>Gets how many packets this process has dropped for breaking their bounds.</summary>
    public static long Dropped => Interlocked.Read(ref s_dropped);

    /// <inheritdoc/>
    public ValueTask InvokeAsync(IPacketContext<IPacket> context, Func<CancellationToken, ValueTask> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (PacketBounds.IsWithinBounds(context.Packet))
        {
            return next(context.CancellationToken);
        }

        _ = Interlocked.Increment(ref s_dropped);
        if (context.Connection is IConnectionErrorTracked tracked)
        {
            tracked.IncrementErrorCount();
        }

        return ValueTask.CompletedTask;
    }
}
