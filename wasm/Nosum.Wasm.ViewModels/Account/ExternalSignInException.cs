// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>Thrown by an <see cref="IExternalSignIn"/> when no ID token was obtained. The form shows the text for its <see cref="Failure"/>.</summary>
public sealed class ExternalSignInException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="failure">Why the sign-in produced no token.</param>
    public ExternalSignInException(ExternalSignInFailure failure)
        : base("External sign-in failed: " + failure)
    {
        this.Failure = failure;
    }

    /// <summary>Initializes the exception with the browser error that caused it.</summary>
    /// <param name="failure">Why the sign-in produced no token.</param>
    /// <param name="innerException">The browser error.</param>
    public ExternalSignInException(ExternalSignInFailure failure, Exception innerException)
        : base("External sign-in failed: " + failure, innerException)
    {
        this.Failure = failure;
    }

    /// <summary>Gets why the sign-in produced no token.</summary>
    public ExternalSignInFailure Failure { get; }
}
