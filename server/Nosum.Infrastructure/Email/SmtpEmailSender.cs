// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Abstractions.Email;
using Nosum.Contracts.Common;

namespace Nosum.Infrastructure.Email;

/// <summary>Where and how to reach the SMTP server. Read from the environment by <see cref="FromEnvironment"/>; the password is never logged.</summary>
/// <param name="Host">SMTP host.</param>
/// <param name="Port">SMTP port (587 for STARTTLS).</param>
/// <param name="UserName">Login, or empty for none.</param>
/// <param name="Password">Password, or empty for none.</param>
/// <param name="From">Sender address.</param>
/// <param name="UseSsl">Whether to upgrade the connection with TLS.</param>
public sealed record SmtpEmailOptions(string Host, int Port, string UserName, string Password, string From, bool UseSsl)
{
    /// <summary>Reads <c>NOSUM_SMTP_HOST</c>, <c>NOSUM_SMTP_PORT</c>, <c>NOSUM_SMTP_USER</c>, <c>NOSUM_SMTP_PASSWORD</c>, <c>NOSUM_SMTP_FROM</c> and <c>NOSUM_SMTP_SSL</c>.</summary>
    /// <returns>The options, or null when <c>NOSUM_SMTP_HOST</c> is not set.</returns>
    public static SmtpEmailOptions? FromEnvironment()
    {
        string? host = Environment.GetEnvironmentVariable("NOSUM_SMTP_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        _ = int.TryParse(Environment.GetEnvironmentVariable("NOSUM_SMTP_PORT"), NumberStyles.None, CultureInfo.InvariantCulture, out int port);
        string from = Environment.GetEnvironmentVariable("NOSUM_SMTP_FROM") ?? "no-reply@" + host;
        bool ssl = !string.Equals(Environment.GetEnvironmentVariable("NOSUM_SMTP_SSL"), "false", StringComparison.OrdinalIgnoreCase);
        return new SmtpEmailOptions(
            host,
            port == 0 ? 587 : port,
            Environment.GetEnvironmentVariable("NOSUM_SMTP_USER") ?? string.Empty,
            Environment.GetEnvironmentVariable("NOSUM_SMTP_PASSWORD") ?? string.Empty,
            from,
            ssl);
    }
}

/// <summary>Production <see cref="IEmailSender"/> over SMTP (the framework's client; no extra package). The wording comes from <c>Resources/EmailText</c> (Vietnamese neutral, English).</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private static readonly ResourceManager Texts = new("Nosum.Infrastructure.Resources.EmailText", typeof(SmtpEmailSender).Assembly);

    private readonly SmtpEmailOptions _options;

    /// <summary>Initializes the sender.</summary>
    /// <param name="options">The SMTP server settings.</param>
    public SmtpEmailSender(SmtpEmailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Builds the subject and body of a message in the recipient's language.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The subject and the plain-text body.</returns>
    public static (string Subject, string Body) Render(EmailMessage message)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(GameLanguages.ToTag(GameLanguages.Resolve(message.Language)));
        string subject = Texts.GetString(message.Kind + ".Subject", culture) ?? string.Empty;
        string body = string.Format(culture, Texts.GetString(message.Kind + ".Body", culture) ?? "{0}", message.Code);
        return (subject, body);
    }

    /// <inheritdoc/>
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        (string subject, string body) = Render(message);
        using MailMessage mail = new(_options.From, message.To, subject, body);
        using SmtpClient client = new(_options.Host, _options.Port) { EnableSsl = _options.UseSsl };
        if (_options.UserName.Length > 0)
        {
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
        }

        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
    }
}
