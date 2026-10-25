// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Middleware;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Networking.Packets;
using Nosum.Abstractions.Observability;
using Nosum.Abstractions.Presence;
using Nosum.Handlers.Session;

namespace Nosum.Host.Ops;

/// <summary>
/// Times every handler by opcode (<see cref="NosumMetrics.RecordHandler"/>) and logs the slow ones with the player id, the map instance and the latency
/// (docs/adr/ADR-031 D5, D6). Registered first, so the time includes permission and rate-limit checks. Allocation-free when the handler finishes
/// synchronously, which is the common case.
/// </summary>
[MiddlewareOrder(-1000)]
[MiddlewareStage(MiddlewareStage.Inbound)]
internal sealed class MetricsMiddleware : IPacketMiddleware<IPacket>
{
    /// <summary>A handler slower than this is logged as a warning.</summary>
    public const double SlowMilliseconds = 100;

    private readonly Func<IMapPresence?> _presence;

    /// <summary>Initializes the middleware.</summary>
    /// <param name="presence">Finds the presence service lazily (it is registered after the builder); null when none is.</param>
    public MetricsMiddleware(Func<IMapPresence?> presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        _presence = presence;
    }

    /// <inheritdoc/>
    public ValueTask InvokeAsync(IPacketContext<IPacket> context, Func<CancellationToken, ValueTask> next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        long started = Stopwatch.GetTimestamp();
        ushort opcode = context.Packet.Header.OpCode;
        IConnection connection = context.Connection;

        ValueTask task = next(context.CancellationToken);
        if (task.IsCompletedSuccessfully)
        {
            this.Finish(opcode, started, connection);
            return default;
        }

        return this.AwaitAsync(task, opcode, started, connection);
    }

    private async ValueTask AwaitAsync(ValueTask task, ushort opcode, long started, IConnection connection)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        finally
        {
            this.Finish(opcode, started, connection);
        }
    }

    private void Finish(ushort opcode, long started, IConnection connection)
    {
        double ms = NosumMetrics.RecordHandler(opcode, started);
        if (ms < SlowMilliseconds)
        {
            return;
        }

        Guid? player = connection.TryGetPlayer(out PlayerIdentity identity) ? identity.PlayerId : null;
        string? instance = null;
        if (_presence() is { } presence && presence.TryGet(connection, out PlayerPresence? where))
        {
            instance = where.Instance.Key;
        }

        OpsLog.Warning(
            "handler.slow",
            "A packet handler was slow.",
            new LogField("opcode", "0x" + opcode.ToString("X4", System.Globalization.CultureInfo.InvariantCulture)),
            new LogField("player_id", player),
            new LogField("map_instance", instance),
            new LogField("latency_ms", Math.Round(ms, 1)));
    }
}
