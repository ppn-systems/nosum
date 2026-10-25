// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Nosum.Abstractions.Observability;

/// <summary>Severity of a structured log event.</summary>
public enum OpsLevel
{
    /// <summary>Normal operation worth a line.</summary>
    Information = 0,

    /// <summary>Something unexpected that the server handled.</summary>
    Warning = 1,

    /// <summary>Something failed.</summary>
    Error = 2,
}

/// <summary>
/// Structured JSON logs, one object per line (docs/adr/ADR-031 D5). Every event has an event id, a level and a time; fields add the player id,
/// the map instance, latencies and so on. Secrets never get out: fields named like a password, token, key, hash, e-mail or chat text are replaced
/// by <c>[redacted]</c>, and e-mail addresses and long token-like runs inside any string value are masked (<see cref="Scrub"/>; ids pass). The output is a
/// <see cref="TextWriter"/> that defaults to nothing, so tests stay silent; the host points it at standard output.
/// </summary>
public static partial class OpsLog
{
    private const string Redacted = "[redacted]";

    private static readonly string[] s_secretNames =
    [
        "password", "passwd", "token", "secret", "key", "hash", "email", "mail", "text", "chat", "authorization", "cookie", "credential", "connectionstring",
    ];

    private static readonly Lock s_gate = new();
    private static TextWriter s_output = TextWriter.Null;
    private static TimeProvider s_clock = TimeProvider.System;

    /// <summary>Gets or sets the least severe level that is written.</summary>
    public static OpsLevel MinimumLevel { get; set; } = OpsLevel.Information;

    /// <summary>Replaces the output writer and the clock (tests pass a string writer and a fake clock).</summary>
    /// <param name="output">Where each event line goes.</param>
    /// <param name="clock">The clock stamping each event; null keeps the system clock.</param>
    public static void Configure(TextWriter output, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        lock (s_gate)
        {
            s_output = output;
            s_clock = clock ?? TimeProvider.System;
        }
    }

    /// <summary>Writes an information event.</summary>
    /// <param name="eventId">Stable dotted id such as <c>admin.ban</c>.</param>
    /// <param name="message">A short English sentence without secrets.</param>
    /// <param name="fields">The event fields.</param>
    public static void Information(string eventId, string message, params ReadOnlySpan<LogField> fields)
        => Write(OpsLevel.Information, eventId, message, fields);

    /// <summary>Writes a warning event.</summary>
    /// <param name="eventId">Stable dotted id.</param>
    /// <param name="message">A short English sentence without secrets.</param>
    /// <param name="fields">The event fields.</param>
    public static void Warning(string eventId, string message, params ReadOnlySpan<LogField> fields)
        => Write(OpsLevel.Warning, eventId, message, fields);

    /// <summary>Writes an error event.</summary>
    /// <param name="eventId">Stable dotted id.</param>
    /// <param name="message">A short English sentence without secrets.</param>
    /// <param name="fields">The event fields.</param>
    public static void Error(string eventId, string message, params ReadOnlySpan<LogField> fields)
        => Write(OpsLevel.Error, eventId, message, fields);

    /// <summary>Writes one event as one JSON line.</summary>
    /// <param name="level">The severity.</param>
    /// <param name="eventId">Stable dotted id.</param>
    /// <param name="message">A short English sentence without secrets.</param>
    /// <param name="fields">The event fields.</param>
    public static void Write(OpsLevel level, string eventId, string message, ReadOnlySpan<LogField> fields)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        ArrayBufferWriter<byte> buffer = new(256);
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("ts", s_clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            json.WriteString("level", level switch { OpsLevel.Warning => "warn", OpsLevel.Error => "error", _ => "info" });
            json.WriteString("event", eventId);
            json.WriteString("msg", Scrub(message));
            foreach (LogField field in fields)
            {
                json.WritePropertyName(field.Name);
                WriteValue(json, field);
            }

            json.WriteEndObject();
        }

        string line = Encoding.UTF8.GetString(buffer.WrittenSpan);
        lock (s_gate)
        {
            s_output.WriteLine(line);
            s_output.Flush();
        }
    }

    /// <summary>Masks e-mail addresses and long token-like runs in a text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with each address replaced by <c>[email]</c> and each run of 32 or more base64 characters by <c>[redacted]</c>.</returns>
    public static string Scrub(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return text;
        }

        string withoutMail = EmailPattern().Replace(text, "[email]");

        // Ids are not secrets and are what an operator searches for; anything else that long and token-shaped (a session token is 43 base64url characters) is.
        return TokenPattern().Replace(withoutMail, static m => Guid.TryParse(m.Value, out _) ? m.Value : Redacted);
    }

    /// <summary>Redirects <see cref="Console.Error"/> into the log: each line written there becomes one <c>console.error</c> event at error level.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The writer replaces Console.Error for the life of the process.")]
    public static void RouteConsoleError() => Console.SetError(new ErrorLineWriter());

    private static void WriteValue(Utf8JsonWriter json, LogField field)
    {
        if (IsSecretName(field.Name))
        {
            json.WriteStringValue(Redacted);
            return;
        }

        switch (field.Value)
        {
            case null:
                json.WriteNullValue();
                break;
            case string text:
                json.WriteStringValue(Scrub(text));
                break;
            case bool flag:
                json.WriteBooleanValue(flag);
                break;
            case int number:
                json.WriteNumberValue(number);
                break;
            case long number:
                json.WriteNumberValue(number);
                break;
            case uint number:
                json.WriteNumberValue(number);
                break;
            case ushort number:
                json.WriteNumberValue(number);
                break;
            case double number:
                json.WriteNumberValue(number);
                break;
            case Guid id:
                json.WriteStringValue(id);
                break;
            case DateTimeOffset time:
                json.WriteStringValue(time.UtcDateTime);
                break;
            default:
                json.WriteStringValue(Scrub(Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? string.Empty));
                break;
        }
    }

    private static bool IsSecretName(string name)
    {
        foreach (string secret in s_secretNames)
        {
            if (name.Contains(secret, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"[^\s@""'<>,;]+@[^\s@""'<>,;]+\.[^\s@""'<>,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"[A-Za-z0-9_+/=-]{32,}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    private sealed class ErrorLineWriter : TextWriter
    {
        private readonly StringBuilder _line = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                this.Emit();
            }
            else if (value != '\r')
            {
                _ = _line.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (value is null)
            {
                return;
            }

            foreach (char c in value)
            {
                this.Write(c);
            }
        }

        public override void WriteLine(string? value)
        {
            this.Write(value);
            this.Emit();
        }

        private void Emit()
        {
            if (_line.Length == 0)
            {
                return;
            }

            string text = _line.ToString();
            _ = _line.Clear();
            OpsLog.Error("console.error", text);
        }
    }
}
