// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace Nosum.Abstractions.Observability;

/// <summary>
/// The server's metrics (docs/adr/ADR-031 D6): one <see cref="Meter"/> named <c>Nosum</c> that the host exposes in Prometheus text format.
/// Recording is cheap and allocation-free once the tag values are cached, so it is safe on the snapshot tick and in the handler pipeline.
/// Nothing here carries a player id, a nickname or text: the labels are opcodes, areas and status names.
/// </summary>
public static class NosumMetrics
{
    /// <summary>The meter name; exporters subscribe to it.</summary>
    public const string MeterName = "Nosum";

    private static readonly Meter s_meter = new(MeterName);
    private static readonly ConcurrentDictionary<ushort, KeyValuePair<string, object?>> s_opcodeTags = new();
    private static readonly ConcurrentDictionary<(string Area, string Status), (KeyValuePair<string, object?> Area, KeyValuePair<string, object?> Status)> s_rejectTags = new();

    private static readonly Histogram<double> s_snapshotTickMs = s_meter.CreateHistogram<double>("nosum_snapshot_tick_duration_ms", "ms", "Time to build and send one round of map snapshots.");
    private static readonly Histogram<long> s_snapshotBytes = s_meter.CreateHistogram<long>("nosum_snapshot_bytes_per_tick", "By", "Snapshot bytes sent in one tick, before encryption.");
    private static readonly Histogram<double> s_handlerMs = s_meter.CreateHistogram<double>("nosum_handler_duration_ms", "ms", "Time from dispatch to the end of a packet handler, per opcode.");
    private static readonly Histogram<double> s_dbMs = s_meter.CreateHistogram<double>("nosum_db_transaction_duration_ms", "ms", "Time of one database save (one transaction).");
    private static readonly Counter<long> s_rejected = s_meter.CreateCounter<long>("nosum_intents_rejected_total", description: "Intents the server refused, by area and status name.");

    private static Func<int>? s_playersOnline;
    private static Func<IEnumerable<Measurement<int>>>? s_playersPerInstance;

    static NosumMetrics()
    {
        _ = s_meter.CreateObservableGauge("nosum_players_online", static () => s_playersOnline?.Invoke() ?? 0, description: "Players on any map.");
        _ = s_meter.CreateObservableGauge("nosum_instance_players", static () => s_playersPerInstance?.Invoke() ?? [], description: "Players per map instance (the key is a map id or an owner-scoped private copy, never a nickname).");

        // Runtime numbers for the load test (bench/Nosum.Bench.Load) and for operators. Read only when scraped, so they cost nothing on the hot path.
        _ = s_meter.CreateObservableCounter("nosum_process_cpu_seconds_total", ReadProcessCpuSeconds, "s", "Processor time of this process (user and kernel).");
        _ = s_meter.CreateObservableCounter("nosum_gc_allocated_bytes_total", static () => GC.GetTotalAllocatedBytes(), "By", "Bytes allocated on the managed heap since the process started.");
        _ = s_meter.CreateObservableCounter("nosum_gc_collections_total", ReadGcCollections, description: "Garbage collections by generation.");
        _ = s_meter.CreateObservableCounter("nosum_gc_pause_seconds_total", static () => GC.GetTotalPauseDuration().TotalSeconds, "s", "Time the runtime spent paused for garbage collection.");
        _ = s_meter.CreateObservableGauge("nosum_process_working_set_bytes", static () => Environment.WorkingSet, "By", "Resident memory of this process.");
    }

    private static double ReadProcessCpuSeconds()
    {
        using Process process = Process.GetCurrentProcess();
        return process.TotalProcessorTime.TotalSeconds;
    }

    private static IEnumerable<Measurement<long>> ReadGcCollections()
    {
        Measurement<long>[] byGeneration = new Measurement<long>[3];
        for (int generation = 0; generation < byGeneration.Length; generation++)
        {
            byGeneration[generation] = new Measurement<long>(GC.CollectionCount(generation), new KeyValuePair<string, object?>("generation", generation.ToString(CultureInfo.InvariantCulture)));
        }

        return byGeneration;
    }

    /// <summary>Tells the gauges where to read the live numbers; the host calls this once.</summary>
    /// <param name="playersOnline">Returns the number of players on all maps.</param>
    /// <param name="playersPerInstance">Returns one measurement per active instance, tagged <c>instance</c>.</param>
    public static void ObservePlayers(Func<int> playersOnline, Func<IEnumerable<Measurement<int>>> playersPerInstance)
    {
        ArgumentNullException.ThrowIfNull(playersOnline);
        ArgumentNullException.ThrowIfNull(playersPerInstance);
        s_playersOnline = playersOnline;
        s_playersPerInstance = playersPerInstance;
    }

    /// <summary>Records how long one snapshot round took.</summary>
    /// <param name="startTimestamp">The <see cref="Stopwatch.GetTimestamp"/> value taken when the round started.</param>
    public static void RecordSnapshotTick(long startTimestamp) => s_snapshotTickMs.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

    /// <summary>Records the bytes one snapshot round sent.</summary>
    /// <param name="bytes">Total payload bytes of the round.</param>
    public static void RecordSnapshotBytes(long bytes) => s_snapshotBytes.Record(bytes);

    /// <summary>Records how long a handler took.</summary>
    /// <param name="opcode">The packet opcode.</param>
    /// <param name="startTimestamp">The <see cref="Stopwatch.GetTimestamp"/> value taken before dispatch.</param>
    /// <returns>The elapsed milliseconds, so the caller can log slow handlers.</returns>
    public static double RecordHandler(ushort opcode, long startTimestamp)
    {
        double ms = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        s_handlerMs.Record(ms, s_opcodeTags.GetOrAdd(opcode, static code => new KeyValuePair<string, object?>("opcode", "0x" + code.ToString("X4", CultureInfo.InvariantCulture))));
        return ms;
    }

    /// <summary>Records how long one database save took.</summary>
    /// <param name="startTimestamp">The <see cref="Stopwatch.GetTimestamp"/> value taken before the save.</param>
    public static void RecordDbTransaction(long startTimestamp) => s_dbMs.Record(Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

    /// <summary>Counts one refused intent.</summary>
    /// <param name="area">The feature area, such as <c>move</c>, <c>account</c> or <c>chat</c>.</param>
    /// <param name="status">The status name the server answered with.</param>
    public static void IntentRejected(string area, string status)
    {
        (KeyValuePair<string, object?> areaTag, KeyValuePair<string, object?> statusTag) = s_rejectTags.GetOrAdd(
            (area, status),
            static key => (new KeyValuePair<string, object?>("area", key.Area), new KeyValuePair<string, object?>("status", key.Status)));
        s_rejected.Add(1, areaTag, statusTag);
    }
}
