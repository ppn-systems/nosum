// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Email;

namespace Nosum.Infrastructure.Email;

/// <summary>
/// Development <see cref="IEmailSender"/>: sends nothing and logs only the fact that a mail would have gone out, to a masked address.
/// The code is a secret and is never written anywhere.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly Action<string> _log;

    /// <summary>Initializes the sender.</summary>
    /// <param name="log">Receives one line per mail; defaults to standard output.</param>
    public LoggingEmailSender(Action<string>? log = null) => _log = log ?? Console.WriteLine;

    /// <inheritdoc/>
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _log($"Mail not sent (no mail provider configured): kind={message.Kind} to={Mask(message.To)}");
        return Task.CompletedTask;
    }

    /// <summary>Hides all but the first character of the local part of an address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>For example <c>a***@gmail.com</c>.</returns>
    public static string Mask(string address)
    {
        ArgumentNullException.ThrowIfNull(address);
        int at = address.IndexOf('@', StringComparison.Ordinal);
        return at <= 0 ? "***" : string.Concat(address.AsSpan(0, 1), "***", address.AsSpan(at));
    }
}
