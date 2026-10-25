// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.GroundItems;

namespace Nosum.Infrastructure.GroundItems;

/// <summary>The hosted job that removes items that lay on shared maps too long (docs/adr/ADR-021 D6).</summary>
public sealed class GroundItemExpiryService
{
    /// <summary>How often the sweep runs.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(30);

    private readonly IGroundItems _items;
    private readonly TimeProvider _time;

    /// <summary>Initializes the job.</summary>
    /// <param name="items">The ground items.</param>
    /// <param name="time">The clock that paces the sweep.</param>
    public GroundItemExpiryService(IGroundItems items, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(time);

        _items = items;
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
                    _ = await _items.ExpireDueAsync(cancellationToken).ConfigureAwait(false);
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
