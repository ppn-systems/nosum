// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Nosum.Bench.Load;

/// <summary>What the bots counted, shared by all of them.</summary>
internal sealed class LoadStats
{
    private readonly ConcurrentQueue<double> _farmMs = new();
    private readonly ConcurrentQueue<double> _chatMs = new();
    private readonly ConcurrentDictionary<string, int> _errors = new();
    private readonly ConcurrentDictionary<string, int> _statuses = new();
    private long _snapshots;
    private long _snapshotBytes;

    public int Connected;
    public int ConnectFailed;
    public int Travellers;
    public int Arrived;
    public int FarmSent;
    public int FarmOk;
    public int ChatSent;
    public int ChatOk;
    public int Corrections;
    public int MapChanges;
    public int Dropped;

    public long Snapshots => Interlocked.Read(ref _snapshots);

    public long SnapshotBytes => Interlocked.Read(ref _snapshotBytes);

    public IReadOnlyDictionary<string, int> Errors => _errors;

    public void Snapshot(int bytes)
    {
        _ = Interlocked.Increment(ref _snapshots);
        _ = Interlocked.Add(ref _snapshotBytes, bytes);
    }

    /// <summary>Counts one answer by feature and status name, so the report can show why requests were refused.</summary>
    /// <param name="feature">The feature, such as <c>farm</c>.</param>
    /// <param name="status">The status name.</param>
    public void Count(string feature, string status) => _ = _statuses.AddOrUpdate(feature + ":" + status, 1, static (_, n) => n + 1);

    public IReadOnlyDictionary<string, int> Statuses => _statuses;

    public void Error(string kind) => _ = _errors.AddOrUpdate(kind, 1, static (_, n) => n + 1);

    public void FarmRoundTrip(double ms) => _farmMs.Enqueue(ms);

    public void ChatRoundTrip(double ms) => _chatMs.Enqueue(ms);

    /// <summary>Takes the counters at one moment, so a phase is the difference of two marks.</summary>
    /// <returns>The marks.</returns>
    public WindowMark Mark() => new(this.Snapshots, this.SnapshotBytes, Volatile.Read(ref this.Corrections), Volatile.Read(ref this.MapChanges), Volatile.Read(ref this.FarmSent), Volatile.Read(ref this.FarmOk), Volatile.Read(ref this.ChatSent), Volatile.Read(ref this.ChatOk));

    public Percentiles Farm() => Percentiles.Of(_farmMs);

    public Percentiles Chat() => Percentiles.Of(_chatMs);
}

/// <summary>The counters at one moment.</summary>
/// <param name="Snapshots">Map snapshots received.</param>
/// <param name="SnapshotBytes">Bytes of those snapshots, before encryption and compression.</param>
/// <param name="Corrections">Moves the server refused.</param>
/// <param name="MapChanges">Exits taken.</param>
/// <param name="FarmSent">Tool uses sent.</param>
/// <param name="FarmOk">Tool uses accepted.</param>
/// <param name="ChatSent">Chat lines sent.</param>
/// <param name="ChatOk">Chat lines accepted.</param>
internal readonly record struct WindowMark(long Snapshots, long SnapshotBytes, int Corrections, int MapChanges, int FarmSent, int FarmOk, int ChatSent, int ChatOk);

/// <summary>Request round-trip percentiles in milliseconds.</summary>
/// <param name="Count">Samples.</param>
/// <param name="P50">Median.</param>
/// <param name="P95">95th percentile.</param>
/// <param name="P99">99th percentile.</param>
/// <param name="Max">Slowest.</param>
internal readonly record struct Percentiles(int Count, double P50, double P95, double P99, double Max)
{
    public static Percentiles Of(IEnumerable<double> samples)
    {
        double[] sorted = [.. samples.Order()];
        if (sorted.Length == 0)
        {
            return default;
        }

        double At(double q) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(q * sorted.Length) - 1)];
        return new Percentiles(sorted.Length, At(0.50), At(0.95), At(0.99), sorted[^1]);
    }
}
