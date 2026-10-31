// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Account;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>
/// The "continue as" card of the sign-in screen (docs/adr/ADR-082). Remembers the session a sign-in issued, offers to resume it without another
/// sign-in, and forgets it on sign-out or when the server refuses it (expired, revoked or banned), which sends the player back to the normal sign-in.
/// A connection failure keeps the remembered session: the server was not asked to refuse it.
/// </summary>
public sealed class RememberedSessionViewModel : IObservableViewModel
{
    private readonly IAccountClient _client;
    private readonly IRememberedSessionStore _store;
    private readonly IStringLocalizer<Language> _text;
    private readonly TimeProvider _time;

    /// <summary>Initializes the view model; nothing is remembered until <see cref="LoadAsync"/>.</summary>
    /// <param name="client">The account requests (resume and logout).</param>
    /// <param name="store">Where the session is kept in this browser.</param>
    /// <param name="text">The localizer for <c>Language.resx</c>.</param>
    /// <param name="time">The clock for expiry; the game's own clock in the browser.</param>
    public RememberedSessionViewModel(IAccountClient client, IRememberedSessionStore store, IStringLocalizer<Language> text, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(time);
        _client = client;
        _store = store;
        _text = text;
        _time = time;
    }

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>Gets the remembered session, or <see langword="null"/> when none is kept (or it has expired).</summary>
    public RememberedSession? Remembered { get; private set; }

    /// <summary>Gets whether a session is remembered and the card should be offered.</summary>
    public bool HasRemembered => this.Remembered is not null;

    /// <summary>Gets whether <see cref="LoadAsync"/> has run, so the screen does not flash the sign-in form before the card.</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Gets the name the continue button uses: the nickname, or "Guest" for a guest.</summary>
    public string DisplayName => this.Remembered is not { } r
        ? string.Empty
        : r.Kind == RememberedAccountKind.Guest ? _text["Account_Guest"].Value : r.Nickname;

    /// <summary>Gets the continue button's text, "Continue as {name}", localized.</summary>
    public string ContinueText => string.Format(CultureInfo.CurrentCulture, _text["Account_Continue_As"].Value, this.DisplayName);

    /// <summary>Gets the text of the button that signs out or switches account, localized.</summary>
    public string ForgetText => _text["Account_Continue_Forget"];

    /// <summary>Gets the title of the card, localized.</summary>
    public string Title => _text["Account_Continue_Title"];

    /// <summary>Reads the stored session. An expired one is forgotten here, so the card is never offered for it.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the card's state is known.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        RememberedSession? stored = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (stored is not null && stored.ExpiresAt <= _time.GetUtcNow())
        {
            await _store.ClearAsync(cancellationToken).ConfigureAwait(false);
            stored = null;
        }

        this.Remembered = stored;
        this.IsLoaded = true;
        this.Changed?.Invoke();
    }

    /// <summary>
    /// Resumes the remembered session without another sign-in. A success keeps the rolled token the server issued (a resume renews it); a refusal
    /// forgets the session. A connection failure throws <see cref="AccountConnectionException"/> and keeps the session.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The server's reply, for the form to show or apply.</returns>
    /// <exception cref="InvalidOperationException">Nothing is remembered.</exception>
    /// <exception cref="AccountConnectionException">The connection is down; the session is kept.</exception>
    public async Task<AccountReply> ContinueAsync(CancellationToken cancellationToken)
    {
        RememberedSession current = this.Remembered ?? throw new InvalidOperationException("No remembered session to continue with.");
        AccountReply reply = await _client.ResumeAsync(current.SessionToken, cancellationToken).ConfigureAwait(false);
        if (reply is { Status: AccountStatus.Ok, Session: { } session })
        {
            string token = string.IsNullOrWhiteSpace(session.SessionToken) ? current.SessionToken : session.SessionToken;
            RememberedSession renewed = new(token, session.ExpiresAt, session.Nickname, current.Kind);
            await _store.SaveAsync(renewed, cancellationToken).ConfigureAwait(false);
            this.Remembered = renewed;
        }
        else
        {
            await this.ClearAsync(cancellationToken).ConfigureAwait(false);
        }

        this.Changed?.Invoke();
        return reply;
    }

    /// <summary>Remembers a session a sign-in or link just issued. A guest is always kept as a guest; nothing is kept without a token.</summary>
    /// <param name="session">The session the server issued.</param>
    /// <param name="kind">The provider the player signed in with; ignored for a guest.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the session is kept.</returns>
    public async Task RememberAsync(AccountSession session, RememberedAccountKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.SessionToken))
        {
            return;
        }

        RememberedSession record = new(
            session.SessionToken,
            session.ExpiresAt,
            session.Nickname,
            session.IsGuest ? RememberedAccountKind.Guest : kind);
        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        this.Remembered = record;
        this.Changed?.Invoke();
    }

    /// <summary>
    /// Signs out the remembered session and forgets it: the server is asked to revoke it when it still answers (a resume, then a logout), and the
    /// browser forgets it either way. An unreachable server does not keep the player signed in here; the token lapses on the server by itself.
    /// </summary>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>A task that completes when the session is forgotten.</returns>
    public async Task ForgetAsync(CancellationToken cancellationToken)
    {
        if (this.Remembered is { } current)
        {
            try
            {
                AccountReply reply = await _client.ResumeAsync(current.SessionToken, cancellationToken).ConfigureAwait(false);
                if (reply.Status == AccountStatus.Ok)
                {
                    await _client.LogoutAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (AccountConnectionException)
            {
                // Offline: nothing to revoke on this connection. The local copy is forgotten below.
            }
        }

        await this.ClearAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Forgets the stored session in this browser only, without asking the server (used after the player has signed out).</summary>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the stored session is gone.</returns>
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _store.ClearAsync(cancellationToken).ConfigureAwait(false);
        this.Remembered = null;
        this.Changed?.Invoke();
    }
}
