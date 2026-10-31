// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using Nosum.Wasm.ViewModels.Account;

namespace Nosum.Wasm.Account;

/// <summary>
/// Keeps the remembered session in the browser's <c>localStorage</c> (docs/adr/ADR-081): one JSON record under one key, with the session token
/// and nothing else secret. A browser that refuses storage, or a record that cannot be read, reads as "nothing remembered"; a write that fails is
/// ignored, so the sign-in screen still works.
/// </summary>
internal sealed class BrowserRememberedSessionStore : IRememberedSessionStore
{
    /// <summary>The <c>localStorage</c> key of the record.</summary>
    internal const string StorageKey = "nosum.remembered-session";

    private readonly IJSRuntime _js;

    /// <summary>Initializes the store.</summary>
    /// <param name="js">The JS runtime; the sign-in screen is not in the frame loop, so the calls are not per frame.</param>
    public BrowserRememberedSessionStore(IJSRuntime js)
    {
        ArgumentNullException.ThrowIfNull(js);
        _js = js;
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "Storage can be refused or hold junk; the sign-in screen must still start, so any failure reads as nothing remembered.")]
    public async Task<RememberedSession?> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            string? json = await _js.InvokeAsync<string?>("localStorage.getItem", cancellationToken, StorageKey).ConfigureAwait(false);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            StoredRememberedSession? record = JsonSerializer.Deserialize(json, RememberedSessionJsonContext.Default.StoredRememberedSession);
            return record?.ToSession();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "A refused write leaves the player signed in for this visit only; nothing else is at stake.")]
    public async Task SaveAsync(RememberedSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            string json = JsonSerializer.Serialize(StoredRememberedSession.From(session), RememberedSessionJsonContext.Default.StoredRememberedSession);
            await _js.InvokeVoidAsync("localStorage.setItem", cancellationToken, StorageKey, json).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Not remembered: the player signs in again next visit.
        }
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "A refused removal leaves a record the next load will treat as nothing remembered once it expires.")]
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", cancellationToken, StorageKey).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Nothing to clear if storage is refused.
        }
    }
}

/// <summary>The stored record. A plain DTO, so the JSON is generated at build time (trimming-safe) and the enum is a fixed word.</summary>
/// <param name="Version">The record version; only 1 is read.</param>
/// <param name="Token">The session token.</param>
/// <param name="ExpiresAt">When the token expires.</param>
/// <param name="Nickname">The nickname to show.</param>
/// <param name="Kind">"guest", "google" or "microsoft".</param>
internal sealed record StoredRememberedSession(int Version, string Token, DateTimeOffset ExpiresAt, string Nickname, string Kind)
{
    /// <summary>Converts a session to its stored form.</summary>
    /// <param name="session">The session to store.</param>
    /// <returns>The record to write.</returns>
    public static StoredRememberedSession From(RememberedSession session) => new(
        1,
        session.SessionToken,
        session.ExpiresAt,
        session.Nickname,
        session.Kind switch
        {
            RememberedAccountKind.Google => "google",
            RememberedAccountKind.Microsoft => "microsoft",
            _ => "guest",
        });

    /// <summary>Reads the record back, or returns <see langword="null"/> when it is not one this version wrote.</summary>
    /// <returns>The session, or <see langword="null"/>.</returns>
    public RememberedSession? ToSession()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(Token))
        {
            return null;
        }

        RememberedAccountKind? kind = Kind switch
        {
            "google" => RememberedAccountKind.Google,
            "microsoft" => RememberedAccountKind.Microsoft,
            "guest" => RememberedAccountKind.Guest,
            _ => null,
        };
        return kind is { } known ? new RememberedSession(Token, ExpiresAt, Nickname ?? string.Empty, known) : null;
    }
}

/// <summary>Source-generated JSON for <see cref="StoredRememberedSession"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StoredRememberedSession))]
internal sealed partial class RememberedSessionJsonContext : JsonSerializerContext
{
}
