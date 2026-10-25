// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Bench.Load;

/// <summary>
/// Headless load test of a running Nosum server (docs/adr/ADR-031 D8): N guests connect over a ramp, walk, farm and chat for a steady phase, and the
/// report gives what the clients saw (players that made it in, round trips, snapshot bandwidth) next to what the server measured over the same
/// phase (snapshot tick duration and bytes, from its /metrics). The M5 budget is 200 clients with a snapshot tick p99 under 20 ms on a 4-core server.
/// Run it against a server started with a rate-limit-free configuration only when the limits themselves are not under test (docs/ops/README.md).
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!LoadOptions.TryParse(args, out LoadOptions? options, out string? error))
        {
            if (error is not null)
            {
                await Console.Error.WriteLineAsync(error);
            }

            await (error is null ? Console.Out : Console.Error).WriteLineAsync(LoadOptions.Usage);
            return error is null ? 0 : 2;
        }

        WorldMaps world;
        try
        {
            world = WorldMaps.Load(options.ResolveMapsDirectory());
        }
        catch (Exception ex) when (ex is System.IO.IOException or FormatException)
        {
            await Console.Error.WriteLineAsync("Could not load the maps: " + ex.Message);
            return 2;
        }

        using CancellationTokenSource stop = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        LoadStats stats = new();
        Random random = new(options.Seed);
        List<string> targets = world.TravelTargets();
        List<Task> bots = [];
        Console.WriteLine($"Connecting {options.Clients} clients to {(options.Tls ? "wss" : "ws")}://{options.Host}:{options.Port}{options.Path} over {options.Ramp.TotalSeconds:0.#} s ...");
        Stopwatch clock = Stopwatch.StartNew();
        try
        {
            for (int i = 0; i < options.Clients && !stop.IsCancellationRequested; i++)
            {
                bool traveller = random.NextDouble() < options.Travellers;
                BotClient bot = new(i, options, world, stats, traveller, targets[random.Next(targets.Count)]);
                bots.Add(Task.Run(() => bot.RunAsync(stop.Token)));
                if (options.Clients > 1)
                {
                    await Task.Delay(options.Ramp / options.Clients, stop.Token);
                }
            }

            // Everyone is in; travellers need time to walk. The steady phase starts when the last one joined and the first scrape is taken.
            await WaitForJoinsAsync(stats, options, stop.Token);
            MetricsScrape? before = options.MetricsUrl is { } url ? await MetricsScrape.TryReadAsync(url, stop.Token) : null;
            Stopwatch steady = Stopwatch.StartNew();
            WindowMark start = stats.Mark();
            await Task.Delay(options.Duration, stop.Token);
            MetricsScrape? after = options.MetricsUrl is { } url2 ? await MetricsScrape.TryReadAsync(url2, stop.Token) : null;
            WindowMark end = stats.Mark();

            string report = Report.Build(options, stats, start, end, steady.Elapsed, before, after, out bool pass);
            Console.WriteLine(report);
            await stop.CancelAsync();
            await Task.WhenAll(bots);
            return pass ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Interrupted.");
            await stop.CancelAsync();
            await Task.WhenAll(bots);
            return 1;
        }
    }

    // Waits until every client has either joined or failed, then for the travellers to get where they are going (or the ramp's length, whichever is first).
    private static async Task WaitForJoinsAsync(LoadStats stats, LoadOptions options, CancellationToken stop)
    {
        Stopwatch waited = Stopwatch.StartNew();
        while (stats.Connected + stats.ConnectFailed < options.Clients && waited.Elapsed < TimeSpan.FromSeconds(60))
        {
            await Task.Delay(250, stop);
        }

        Stopwatch walking = Stopwatch.StartNew();
        while (stats.Arrived < stats.Travellers && walking.Elapsed < TimeSpan.FromSeconds(90))
        {
            await Task.Delay(500, stop);
        }
    }
}
