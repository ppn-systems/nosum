// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Wasm.ViewModels.Account;

namespace Nosum.UnitTests.Wasm;

/// <summary>An in-memory remembered-session store that records every write, so tests can see what would have reached localStorage.</summary>
internal sealed class MemoryRememberedSessionStore : IRememberedSessionStore
{
    public RememberedSession? Stored { get; set; }

    public List<RememberedSession> Saved { get; } = [];

    public int Clears { get; private set; }

    public Task<RememberedSession?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(this.Stored);

    public Task SaveAsync(RememberedSession session, CancellationToken cancellationToken)
    {
        this.Stored = session;
        this.Saved.Add(session);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        this.Stored = null;
        this.Clears++;
        return Task.CompletedTask;
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => this.Now;
}

/// <summary>
/// An account client that answers the resume, logout and provider sign-in calls from script. The other calls are not used by these tests and throw if reached.
/// </summary>
internal sealed class ScriptedAccountClient : IAccountClient
{
    /// <summary>The answer to <see cref="IAccountClient.ResumeAsync"/>, or null to throw <see cref="AccountConnectionException"/>.</summary>
    public AccountReply? ResumeReply { get; set; }

    /// <summary>The tokens the resume calls were made with, in order.</summary>
    public List<string> ResumedTokens { get; } = [];

    /// <summary>How many logouts reached the server.</summary>
    public int Logouts { get; private set; }

    /// <summary>The answer to the provider sign-in call (the form's Google button); null throws <see cref="AccountConnectionException"/>.</summary>
    public AccountReply? SignInReply { get; set; }

    public Task<AccountReply> ResumeAsync(string sessionToken, CancellationToken cancellationToken)
    {
        this.ResumedTokens.Add(sessionToken);
        return this.ResumeReply is { } reply ? Task.FromResult(reply) : throw new AccountConnectionException("test");
    }

    public Task LogoutAsync(CancellationToken cancellationToken)
    {
        this.Logouts++;
        return Task.CompletedTask;
    }

    public Task<AccountReply> SignInWithExternalAsync(ExternalProvider provider, string idToken, GameLanguage language, CancellationToken cancellationToken)
        => this.SignInReply is { } reply ? Task.FromResult(reply) : throw new AccountConnectionException("test");

    public Task<AccountReply> StartGuestAsync(GameLanguage language, CancellationToken cancellationToken)
        => throw new NotSupportedException("Not used by the remembered-session tests.");

    public Task<AccountReply> LinkExternalIdentityAsync(ExternalProvider provider, string idToken, string nickname, CancellationToken cancellationToken)
        => throw new NotSupportedException("Not used by the remembered-session tests.");

    public Task<NicknameReply> SetNicknameAsync(string nickname, CancellationToken cancellationToken)
        => throw new NotSupportedException("Not used by the remembered-session tests.");

    public Task<AccountStatus> SetLanguageAsync(GameLanguage language, CancellationToken cancellationToken)
        => throw new NotSupportedException("Not used by the remembered-session tests.");
}
