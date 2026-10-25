// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;

namespace Nosum.Bench.Load;

/// <summary>What one load-test run does.</summary>
internal sealed class LoadOptions
{
    public const string Usage = """
        usage: Nosum.Bench.Load [options]

          --host <name>          server host                                     (default 127.0.0.1)
          --port <n>             game WebSocket port                             (default 57300)
          --path <p>             WebSocket path                                  (default /ws/)
          --tls                  connect with wss://
          --clients <n>          simulated players                               (default 200)
          --ramp <seconds>       time over which the clients connect             (default 20)
          --duration <seconds>   steady phase after the last client joined       (default 60)
          --travellers <0..1>    share of clients that walk to a public map      (default 0.6; the rest farm at home)
          --farm-every <sec>     seconds between a farmer's tool uses            (default 3)
          --maps <dir>           folder with the .tmx maps                       (default assets/Maps, searched upwards)
          --metrics <url>        the server's /metrics, scraped before and after (default http://<host>:57301/metrics; "off" to skip)
          --max-tick-p99 <ms>    fail when the snapshot tick p99 is above this   (default 20)
          --seed <n>             random seed                                     (default 1)
          --help                 this text

        Exit code: 0 pass, 1 budget missed (or too many clients failed), 2 bad arguments.
        """;

    public string Host { get; private set; } = "127.0.0.1";

    public ushort Port { get; private set; } = 57300;

    public string Path { get; private set; } = "/ws/";

    public bool Tls { get; private set; }

    public int Clients { get; private set; } = 200;

    public TimeSpan Ramp { get; private set; } = TimeSpan.FromSeconds(20);

    public TimeSpan Duration { get; private set; } = TimeSpan.FromSeconds(60);

    public double Travellers { get; private set; } = 0.6;

    public TimeSpan FarmEvery { get; private set; } = TimeSpan.FromSeconds(3);

    public string? MapsDirectory { get; private set; }

    public string? MetricsUrl { get; private set; }

    public double MaxTickP99Ms { get; private set; } = 20;

    public int Seed { get; private set; } = 1;

    /// <summary>Parses the command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="options">The options when this returns true.</param>
    /// <param name="error">What is wrong when this returns false; null for <c>--help</c>.</param>
    /// <returns>Whether the run should go ahead.</returns>
    public static bool TryParse(IReadOnlyList<string> args, [NotNullWhen(true)] out LoadOptions? options, out string? error)
    {
        LoadOptions result = new();
        options = null;
        error = null;
        for (int i = 0; i < args.Count; i++)
        {
            string name = args[i];
            if (name is "--help" or "-h")
            {
                return false;
            }

            if (name == "--tls")
            {
                result.Tls = true;
                continue;
            }

            if (i + 1 >= args.Count)
            {
                error = $"{name} needs a value";
                return false;
            }

            string value = args[++i];
            if (!result.TrySet(name, value, out error))
            {
                return false;
            }
        }

        if (result.MetricsUrl is null)
        {
            // "localhost", not the literal 127.0.0.1: the ops endpoint of a development host only answers requests addressed to localhost.
            string metricsHost = result.Host is "127.0.0.1" or "::1" ? "localhost" : result.Host;
            result.MetricsUrl = $"http://{metricsHost}:57301/metrics";
        }
        else if (result.MetricsUrl == "off")
        {
            result.MetricsUrl = null;
        }

        options = result;
        return true;
    }

    /// <summary>Finds the maps folder: the option, else <c>assets/Maps</c> in the current folder or any parent.</summary>
    /// <returns>The folder.</returns>
    /// <exception cref="DirectoryNotFoundException">No folder was found.</exception>
    public string ResolveMapsDirectory()
    {
        if (this.MapsDirectory is not null)
        {
            return Directory.Exists(this.MapsDirectory) ? this.MapsDirectory : throw new DirectoryNotFoundException(this.MapsDirectory);
        }

        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            string candidate = System.IO.Path.Combine(directory.FullName, "assets", "Maps");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("assets/Maps was not found in the current folder or above; pass --maps.");
    }

    private bool TrySet(string name, string value, out string? error)
    {
        error = null;
        switch (name)
        {
            case "--host":
                this.Host = value;
                return true;
            case "--port" when ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort port) && port > 0:
                this.Port = port;
                return true;
            case "--path":
                this.Path = value.StartsWith('/') ? value : "/" + value;
                return true;
            case "--clients" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int clients) && clients is >= 1 and <= 5000:
                this.Clients = clients;
                return true;
            case "--ramp" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double ramp) && ramp >= 0:
                this.Ramp = TimeSpan.FromSeconds(ramp);
                return true;
            case "--duration" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double duration) && duration > 0:
                this.Duration = TimeSpan.FromSeconds(duration);
                return true;
            case "--travellers" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double share) && share is >= 0 and <= 1:
                this.Travellers = share;
                return true;
            case "--farm-every" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double every) && every > 0:
                this.FarmEvery = TimeSpan.FromSeconds(every);
                return true;
            case "--maps":
                this.MapsDirectory = value;
                return true;
            case "--metrics":
                this.MetricsUrl = value;
                return true;
            case "--max-tick-p99" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double p99) && p99 > 0:
                this.MaxTickP99Ms = p99;
                return true;
            case "--seed" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed):
                this.Seed = seed;
                return true;
            default:
                error = $"bad option or value: {name} {value}";
                return false;
        }
    }
}
