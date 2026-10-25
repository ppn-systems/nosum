// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;
using Nosum.Wasm.Socket;
using Nosum.Wasm.ViewModels.Localization;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>
/// Remembers the signed-in session for the life of the page and signs the connection in again,
/// with the session token, every time the socket reconnects. Raises <see cref="Authenticated"/>
/// whenever the connection is signed in, so features can rebuild their server-side state.
/// </summary>
public sealed class AccountSessionKeeper : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IAccountClient _client;
    private AccountSession? _session;

    /// <summary>Initializes the keeper.</summary>
    /// <param name="socket">The game connection.</param>
    /// <param name="client">Account requests.</param>
    public AccountSessionKeeper(IGameSocket socket, IAccountClient client)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(client);

        _socket = socket;
        _client = client;
        socket.Ready += this.OnSocketReady;
    }

    /// <summary>Raised when the connection is signed in: after <see cref="SignedIn"/> and after each successful resume. May run on any thread.</summary>
    public event Action? Authenticated;

    /// <summary>Gets the current session, or <see langword="null"/> before sign-in.</summary>
    public AccountSession? Session => _session;

    /// <summary>Records a fresh sign-in: applies the player's language to the UI and raises <see cref="Authenticated"/>.</summary>
    /// <param name="session">The session the form received.</param>
    public void SignedIn(AccountSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        UiCulture.Apply(session.PreferredLanguage);
        this.Authenticated?.Invoke();
    }

    /// <inheritdoc/>
    public void Dispose() => _socket.Ready -= this.OnSocketReady;

    [SuppressMessage("Design", "CA1031", Justification = "A failed resume leaves the player signed out; the next reconnect tries again.")]
    private async Task ResumeAsync(AccountSession session)
    {
        try
        {
            AccountReply reply = await _client.ResumeAsync(session.SessionToken, CancellationToken.None).ConfigureAwait(false);
            if (reply is { Status: AccountStatus.Ok, Session: { } fresh })
            {
                _session = fresh;
                this.Authenticated?.Invoke();
            }
        }
        catch (Exception)
        {
            // Not signed in on this connection; see above.
        }
    }

    private void OnSocketReady()
    {
        if (_session is { } session)
        {
            _ = this.ResumeAsync(session);
        }
    }
}
