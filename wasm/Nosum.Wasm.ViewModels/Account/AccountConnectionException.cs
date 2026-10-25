// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>
/// Thrown by an <see cref="IAccountClient"/> when the request could not be completed (no connection,
/// timeout). The form shows its localized "could not reach the server" text.
/// </summary>
public sealed class AccountConnectionException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public AccountConnectionException()
    {
    }

    /// <summary>Initializes the exception with a message.</summary>
    /// <param name="message">An English description for logs.</param>
    public AccountConnectionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and the transport failure.</summary>
    /// <param name="message">An English description for logs.</param>
    /// <param name="innerException">The transport failure.</param>
    public AccountConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
