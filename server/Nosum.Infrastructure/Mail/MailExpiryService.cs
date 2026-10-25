// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Mail;

namespace Nosum.Infrastructure.Mail;

/// <summary>Background job: removes mail past its expiry together with its attachment (docs/adr/ADR-029 D5).</summary>
public sealed class MailExpiryService
{
    /// <summary>How often expired mail is removed. Expired mail is already invisible to its recipient, so the interval only bounds storage.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    private readonly IMailService _mail;
    private readonly TimeProvider _time;

    /// <summary>Initializes the job.</summary>
    /// <param name="mail">The mail service.</param>
    /// <param name="time">The clock the timer runs on.</param>
    public MailExpiryService(IMailService mail, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(mail);
        ArgumentNullException.ThrowIfNull(time);

        _mail = mail;
        _time = time;
    }

    /// <summary>Runs until cancelled: one sweep at start, then one per <paramref name="interval"/>.</summary>
    /// <param name="interval">Time between sweeps.</param>
    /// <param name="onError">Called with an exception that stopped one sweep; the job continues.</param>
    /// <param name="cancellationToken">Stops the job.</param>
    /// <returns>A task that completes when the job stopped.</returns>
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
                    _ = await _mail.PurgeExpiredAsync(cancellationToken).ConfigureAwait(false);
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
