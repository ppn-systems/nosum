// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Common;

namespace Nosum.Abstractions.Email;

/// <summary>The kind of an outgoing e-mail; the sender owns the wording.</summary>
public enum EmailKind
{
    /// <summary>A one-time code to reset a forgotten password.</summary>
    PasswordReset = 0,
}

/// <summary>An outgoing e-mail: what it is, who gets it, in which language, and the secret code it carries.</summary>
/// <param name="Kind">What the mail is.</param>
/// <param name="To">The recipient address.</param>
/// <param name="Language">The recipient's language; the sender picks the wording from it.</param>
/// <param name="Code">The one-time code. A secret: implementations never log it.</param>
public readonly record struct EmailMessage(EmailKind Kind, string To, GameLanguage Language, string Code);

/// <summary>Sends e-mail (docs/adr/ADR-032 D4). The only seam to an external provider.</summary>
public interface IEmailSender
{
    /// <summary>Sends one message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    /// <returns>A task that completes when the provider accepted the message.</returns>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
