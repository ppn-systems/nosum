// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Middleware;
using Nalix.Abstractions.Networking.Packets;

namespace Nosum.IntegrationTests.Server.DbBudget;

/// <summary>
/// Test-only packet middleware that enters the measured window's scope around every handler, so <see cref="DbCommandCounter"/> attributes to a
/// measured row only the commands its packet's handler issues (docs/adr/ADR-035). Background work (timers, session-end handlers, loops) runs on
/// flows that never entered the scope, so it is not counted for the row.
/// </summary>
[MiddlewareOrder(-2000)]
[MiddlewareStage(MiddlewareStage.Inbound)]
internal sealed class AttributionMiddleware(DbCommandCounter counter) : IPacketMiddleware<IPacket>
{
    /// <inheritdoc/>
    public ValueTask InvokeAsync(IPacketContext<IPacket> context, Func<CancellationToken, ValueTask> next)
    {
        object? previous = counter.Scope;
        counter.Scope = counter.Window;
        bool suspended = false;
        try
        {
            ValueTask task = next(context.CancellationToken);
            if (task.IsCompletedSuccessfully)
            {
                return default;
            }

            suspended = true;
            return AwaitAsync(task, counter, previous);
        }
        finally
        {
            if (!suspended)
            {
                counter.Scope = previous;
            }
        }
    }

    // The handler awaits: its continuations carry the scope, and the scope is put back when it finishes on this flow.
    private static async ValueTask AwaitAsync(ValueTask task, DbCommandCounter counter, object? previous)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            counter.Scope = previous;
        }
    }
}
