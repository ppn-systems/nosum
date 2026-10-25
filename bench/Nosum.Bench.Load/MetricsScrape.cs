// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Bench.Load;

/// <summary>One scrape of the server's Prometheus endpoint: every sample line as name-with-labels to value.</summary>
internal sealed class MetricsScrape
{
    private readonly Dictionary<string, double> _samples;

    public MetricsScrape(Dictionary<string, double> samples) => _samples = samples;

    /// <summary>Reads the endpoint; null when it cannot be reached (the report then has no server numbers).</summary>
    /// <param name="url">The /metrics URL.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The scrape, or null.</returns>
    public static async Task<MetricsScrape?> TryReadAsync(string url, CancellationToken cancellationToken)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            return Parse(await client.GetStringAsync(new Uri(url), cancellationToken));
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    public static MetricsScrape Parse(string text)
    {
        Dictionary<string, double> samples = new(StringComparer.Ordinal);
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line[0] == '#')
            {
                continue;
            }

            int space = line.LastIndexOf(' ');
            if (space > 0 && double.TryParse(line[(space + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                samples[line[..space]] = value;
            }
        }

        return new MetricsScrape(samples);
    }

    public double Get(string key) => _samples.GetValueOrDefault(key);

    /// <summary>Sums every sample of a metric whatever its labels (for example a counter by area and status).</summary>
    /// <param name="name">The metric name.</param>
    /// <returns>The sum.</returns>
    public double SumOf(string name)
    {
        double sum = 0;
        foreach ((string key, double value) in _samples)
        {
            if (key == name || key.StartsWith(name + "{", StringComparison.Ordinal))
            {
                sum += value;
            }
        }

        return sum;
    }

    /// <summary>
    /// The histogram of a metric between two scrapes: how many observations, their mean, and quantiles as the upper bound of the bucket that holds them
    /// (so a quantile is an upper estimate, never an underestimate).
    /// </summary>
    /// <param name="name">Histogram name, without the <c>_bucket</c> suffix.</param>
    /// <param name="before">The earlier scrape.</param>
    /// <returns>The window's histogram; null when nothing was observed in it.</returns>
    public HistogramWindow? Window(string name, MetricsScrape before)
    {
        double count = this.Get(name + "_count") - before.Get(name + "_count");
        if (count <= 0)
        {
            return null;
        }

        List<(double Le, double Cumulative)> buckets = [];
        string prefix = name + "_bucket{le=\"";
        foreach ((string key, double value) in _samples)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                string le = key[prefix.Length..^2];
                double bound = le == "+Inf" ? double.PositiveInfinity : double.Parse(le, CultureInfo.InvariantCulture);
                buckets.Add((bound, value - before.Get(key)));
            }
        }

        buckets.Sort(static (a, b) => a.Le.CompareTo(b.Le));
        double Quantile(double q)
        {
            foreach ((double le, double cumulative) in buckets)
            {
                if (cumulative >= q * count)
                {
                    return le;
                }
            }

            return double.PositiveInfinity;
        }

        double sum = this.Get(name + "_sum") - before.Get(name + "_sum");
        return new HistogramWindow((long)count, sum / count, Quantile(0.50), Quantile(0.99));
    }
}

/// <summary>A histogram over the steady phase.</summary>
/// <param name="Count">Observations.</param>
/// <param name="Mean">Mean value.</param>
/// <param name="P50">Upper bound of the median bucket.</param>
/// <param name="P99">Upper bound of the 99th-percentile bucket (infinity when it falls past the last bucket).</param>
internal readonly record struct HistogramWindow(long Count, double Mean, double P50, double P99);
