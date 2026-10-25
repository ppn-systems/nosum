// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Market;

namespace Nosum.Infrastructure.Market;

/// <summary>The hosted job that returns the items of expired listings to their sellers (docs/adr/ADR-013).</summary>
public sealed class MarketExpiryService
{
    /// <summary>How often the sweep runs.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(1);

    private readonly IMarketService _market;
    private readonly TimeProvider _time;

    /// <summary>Initializes the job.</summary>
    /// <param name="market">The market.</param>
    /// <param name="time">The clock that paces the sweep.</param>
    public MarketExpiryService(IMarketService market, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(time);

        _market = market;
        _time = time;
    }

    /// <summary>Runs the sweep until cancelled: once at start, then every <paramref name="interval"/>.</summary>
    /// <param name="interval">Time between sweeps.</param>
    /// <param name="onError">Called with a failure of one sweep; the job goes on.</param>
    /// <param name="cancellationToken">Stops the job.</param>
    /// <returns>A task that completes when cancelled.</returns>
    [SuppressMessage("Design", "CA1031", Justification = "One failed sweep must not stop the job; the next tick retries.")]
    public async Task RunAsync(TimeSpan interval, Action<Exception> onError, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onError);

        using PeriodicTimer timer = new(interval, _time);
        try
        {
            do
            {
                try
                {
                    _ = await _market.ExpireDueAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    onError(ex);
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
