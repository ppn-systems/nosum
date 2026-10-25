// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Nosum.Bench.Load;

/// <summary>Turns the counters and the two scrapes into the text a person reads and the pass/fail verdict.</summary>
internal static class Report
{
    private const string Tick = "nosum_snapshot_tick_duration_ms";
    private const string TickBytes = "nosum_snapshot_bytes_per_tick";

    /// <summary>Builds the report.</summary>
    /// <param name="options">The run's options.</param>
    /// <param name="stats">The counters.</param>
    /// <param name="start">Counters at the start of the steady phase.</param>
    /// <param name="end">Counters at its end.</param>
    /// <param name="steady">How long the steady phase lasted.</param>
    /// <param name="before">The server's metrics at the start; null when not scraped.</param>
    /// <param name="after">The server's metrics at the end; null when not scraped.</param>
    /// <param name="pass">Whether the budget was met.</param>
    /// <returns>The report.</returns>
    public static string Build(LoadOptions options, LoadStats stats, WindowMark start, WindowMark end, TimeSpan steady, MetricsScrape? before, MetricsScrape? after, out bool pass)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        double seconds = Math.Max(0.001, steady.TotalSeconds);
        int connected = stats.Connected;
        pass = connected >= Math.Ceiling(options.Clients * 0.95);

        StringBuilder text = new();
        _ = text.AppendLine().AppendLine(string.Create(c, $"== Nosum load test: {options.Clients} clients, steady phase {seconds:0} s =="));
        _ = text.AppendLine(string.Create(c, $"clients      {connected}/{options.Clients} joined, {stats.ConnectFailed} refused, {stats.Dropped} dropped by the server"));
        _ = text.AppendLine(string.Create(c, $"journeys     {stats.Travellers} travellers: {stats.Arrived} reached their map ({end.MapChanges} exits taken); the rest farm at home"));

        long snapshots = end.Snapshots - start.Snapshots;
        long bytes = end.SnapshotBytes - start.SnapshotBytes;
        double perClient = connected == 0 ? 0 : snapshots / seconds / connected;
        _ = text.AppendLine(string.Create(c, $"snapshots    {perClient:0.0}/s per client (the server sends 10/s), {bytes / seconds / 1024:0.0} KiB/s in total, {(connected == 0 ? 0 : bytes / seconds / 1024 / connected):0.00} KiB/s per client (before encryption)"));
        _ = text.AppendLine(string.Create(c, $"moves        {end.Corrections - start.Corrections} refused by the server"));
        Percentiles farm = stats.Farm();
        Percentiles chat = stats.Chat();
        _ = text.AppendLine(string.Create(c, $"tool uses    {end.FarmSent - start.FarmSent} sent, {end.FarmOk - start.FarmOk} accepted; round trip p50 {farm.P50:0.0} ms, p95 {farm.P95:0.0} ms, p99 {farm.P99:0.0} ms (n={farm.Count})"));
        _ = text.AppendLine(string.Create(c, $"chat         {end.ChatSent - start.ChatSent} sent, {end.ChatOk - start.ChatOk} accepted; round trip p50 {chat.P50:0.0} ms, p95 {chat.P95:0.0} ms, p99 {chat.P99:0.0} ms (n={chat.Count})"));
        if (stats.Statuses.Count > 0)
        {
            _ = text.AppendLine("answers      " + string.Join(", ", stats.Statuses.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key} x{e.Value}")));
        }

        if (!stats.Errors.IsEmpty())
        {
            _ = text.AppendLine("errors       " + string.Join(", ", stats.Errors.OrderByDescending(e => e.Value).Select(e => $"{e.Key} x{e.Value}")));
        }

        if (before is null || after is null)
        {
            _ = text.AppendLine("server       no metrics (the /metrics scrape was off or failed): the tick budget could not be checked");
            pass = false;
            _ = text.AppendLine().AppendLine("verdict      FAIL (tick time unknown)");
            return text.ToString();
        }

        HistogramWindow? tick = after.Window(Tick, before);
        HistogramWindow? tickBytes = after.Window(TickBytes, before);
        _ = text.AppendLine(string.Create(c, $"server       {after.Get("nosum_players_online"):0} players online at the end; {after.SumOf("nosum_intents_rejected_total") - before.SumOf("nosum_intents_rejected_total"):0} intents rejected"));
        if (tick is { } t)
        {
            _ = text.AppendLine(string.Create(c, $"tick         {t.Count} ticks: mean {t.Mean:0.00} ms, p50 <= {Bound(t.P50)} ms, p99 <= {Bound(t.P99)} ms (budget {options.MaxTickP99Ms:0.#} ms)"));
            pass &= t.P99 <= options.MaxTickP99Ms;
        }
        else
        {
            _ = text.AppendLine("tick         no snapshot tick ran during the steady phase");
            pass = false;
        }

        if (tickBytes is { } b)
        {
            _ = text.AppendLine(string.Create(c, $"bandwidth    {b.Mean / 1024:0.0} KiB per tick on average, {b.Mean * 10 / 1024:0.0} KiB/s out of the snapshot loop (server's count, before encryption)"));
        }

        _ = text.AppendLine().AppendLine(pass ? "verdict      PASS" : "verdict      FAIL");
        return text.ToString();
    }

    private static string Bound(double ms) => double.IsPositiveInfinity(ms) ? "inf" : ms.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool IsEmpty(this System.Collections.Generic.IReadOnlyDictionary<string, int> errors) => errors.Count == 0;
}
