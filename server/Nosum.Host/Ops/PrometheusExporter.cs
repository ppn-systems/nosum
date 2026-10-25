// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using System.Threading;
using Nosum.Abstractions.Observability;

namespace Nosum.Host.Ops;

/// <summary>
/// Collects the <see cref="NosumMetrics"/> meter with a <see cref="MeterListener"/> and renders it in the Prometheus text exposition format
/// (docs/adr/ADR-031 D6), so no metrics package is needed. Counters add up, histograms keep fixed buckets (millisecond buckets are dense around the
/// 20 ms tick budget), observable gauges are read at scrape time.
/// </summary>
internal sealed class PrometheusExporter : IDisposable
{
    private static readonly double[] s_millisecondBuckets = [0.1, 0.25, 0.5, 1, 2.5, 5, 10, 15, 20, 30, 50, 100, 250, 500, 1000, 5000];
    private static readonly double[] s_byteBuckets = [128, 512, 2_048, 8_192, 32_768, 131_072, 524_288, 2_097_152];

    private readonly Lock _gate = new();
    private readonly MeterListener _listener = new();
    private readonly Dictionary<string, Family> _families = [];

    /// <summary>Starts collecting.</summary>
    public PrometheusExporter()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == NosumMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(this.OnMeasurement);
        _listener.SetMeasurementEventCallback<int>((i, m, t, s) => this.OnMeasurement(i, (long)m, t, s));
        _listener.SetMeasurementEventCallback<double>(this.OnMeasurement);
        _listener.Start();
    }

    /// <summary>Renders every series in the Prometheus text format.</summary>
    /// <returns>The scrape body.</returns>
    public string Render()
    {
        // Gauges are re-read on every scrape, so an instance that emptied disappears instead of lingering at its last value.
        lock (_gate)
        {
            foreach (Family family in _families.Values)
            {
                if (family.Type == "gauge")
                {
                    family.Series.Clear();
                }
            }
        }

        _listener.RecordObservableInstruments();

        StringBuilder text = new();
        lock (_gate)
        {
            foreach ((string name, Family family) in _families)
            {
                _ = text.Append("# HELP ").Append(name).Append(' ').Append(family.Help.Replace("\n", " ", StringComparison.Ordinal)).Append('\n');
                _ = text.Append("# TYPE ").Append(name).Append(' ').Append(family.Type).Append('\n');
                foreach ((string labels, Series series) in family.Series)
                {
                    if (family.Type == "histogram")
                    {
                        RenderHistogram(text, name, labels, family.Buckets!, series);
                    }
                    else
                    {
                        _ = text.Append(name).Append(labels.Length == 0 ? string.Empty : "{" + labels + "}").Append(' ').Append(series.Sum.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                    }
                }
            }
        }

        return text.ToString();
    }

    /// <inheritdoc/>
    public void Dispose() => _listener.Dispose();

    private static void RenderHistogram(StringBuilder text, string name, string labels, double[] bounds, Series series)
    {
        string prefix = labels.Length == 0 ? string.Empty : labels + ",";
        long cumulative = 0;
        for (int i = 0; i < bounds.Length; i++)
        {
            cumulative += series.Counts![i];
            _ = text.Append(name).Append("_bucket{").Append(prefix).Append("le=\"").Append(bounds[i].ToString("R", CultureInfo.InvariantCulture)).Append("\"} ").Append(cumulative.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        _ = text.Append(name).Append("_bucket{").Append(prefix).Append("le=\"+Inf\"} ").Append(series.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        string braces = labels.Length == 0 ? string.Empty : "{" + labels + "}";
        _ = text.Append(name).Append("_sum").Append(braces).Append(' ').Append(series.Sum.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        _ = text.Append(name).Append("_count").Append(braces).Append(' ').Append(series.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    private static string Labels(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        if (tags.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder labels = new();
        foreach (KeyValuePair<string, object?> tag in tags)
        {
            if (labels.Length > 0)
            {
                _ = labels.Append(',');
            }

            string value = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            _ = labels.Append(tag.Key).Append("=\"")
                .Append(value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal))
                .Append('"');
        }

        return labels.ToString();
    }

    private void OnMeasurement<T>(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        where T : struct
    {
        double value = Convert.ToDouble(measurement, CultureInfo.InvariantCulture);
        string labels = Labels(tags);
        lock (_gate)
        {
            if (!_families.TryGetValue(instrument.Name, out Family? family))
            {
                family = Family.For(instrument);
                _families[instrument.Name] = family;
            }

            if (!family.Series.TryGetValue(labels, out Series? series))
            {
                series = new Series(family.Buckets?.Length);
                family.Series[labels] = series;
            }

            switch (family.Type)
            {
                case "counter":
                    series.Sum += value;
                    break;
                case "gauge":
                    series.Sum = value;
                    break;
                default:
                    series.Sum += value;
                    series.Count++;
                    for (int i = 0; i < family.Buckets!.Length; i++)
                    {
                        if (value <= family.Buckets[i])
                        {
                            series.Counts![i]++;
                            break;
                        }
                    }

                    break;
            }
        }
    }

    private sealed class Family
    {
        public Family(string type, string help, double[]? buckets)
        {
            this.Type = type;
            this.Help = help;
            this.Buckets = buckets;
        }

        public string Type { get; }

        public string Help { get; }

        public double[]? Buckets { get; }

        public Dictionary<string, Series> Series { get; } = [];

        public static Family For(Instrument instrument)
        {
            string help = instrument.Description ?? instrument.Name;
            if (instrument.IsObservable)
            {
                return new Family("gauge", help, null);
            }

            if (instrument.GetType().Name.StartsWith("Histogram", StringComparison.Ordinal))
            {
                return new Family("histogram", help, instrument.Unit == "By" ? s_byteBuckets : s_millisecondBuckets);
            }

            return new Family("counter", help, null);
        }
    }

    private sealed class Series
    {
        public Series(int? buckets) => this.Counts = buckets is int n ? new long[n] : null;

        public double Sum { get; set; }

        public long Count { get; set; }

        public long[]? Counts { get; }
    }
}
