// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Wasm.ViewModels.Account;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The "continue as" card: what the browser remembers, resuming it, forgetting it, and the form's use of it (docs/adr/ADR-081).</summary>
[Collection(SharedUiCulture.Name)]
public sealed class RememberedSessionViewModelTests
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset s_later = s_now.AddDays(30);
    private static readonly ExternalSignInOptions s_configured = new("google-client-id", "microsoft-client-id", "common");

    [Fact]
    public async Task LoadAsync_SessionStoredAndNotExpired_IsRemembered()
    {
        MemoryRememberedSessionStore store = new() { Stored = new RememberedSession("token-1", s_later, "Lan", RememberedAccountKind.Google) };
        RememberedSessionViewModel vm = Create(new ScriptedAccountClient(), store, s_now);

        await vm.LoadAsync(CancellationToken.None);

        vm.IsLoaded.Should().BeTrue();
        vm.HasRemembered.Should().BeTrue();
        vm.Remembered.Should().Be(store.Stored);
        store.Clears.Should().Be(0);
    }

    [Fact]
    public async Task LoadAsync_SessionExpired_IsForgottenAndNotOffered()
    {
        MemoryRememberedSessionStore store = new() { Stored = new RememberedSession("token-old", s_now.AddMinutes(-1), "Lan", RememberedAccountKind.Google) };
        RememberedSessionViewModel vm = Create(new ScriptedAccountClient(), store, s_now);

        await vm.LoadAsync(CancellationToken.None);

        vm.HasRemembered.Should().BeFalse();
        store.Stored.Should().BeNull();
        store.Clears.Should().Be(1);
    }

    [Fact]
    public async Task LoadAsync_NothingStored_IsLoadedWithoutARememberedSession()
    {
        RememberedSessionViewModel vm = Create(new ScriptedAccountClient(), new MemoryRememberedSessionStore(), s_now);

        await vm.LoadAsync(CancellationToken.None);

        vm.IsLoaded.Should().BeTrue();
        vm.HasRemembered.Should().BeFalse();
    }

    [Theory]
    [InlineData("en", "Continue as Lan")]
    [InlineData("vi", "Tiếp tục với Lan")]
    public async Task ContinueText_ShowsTheNicknameInTheUiLanguage(string culture, string expected)
    {
        using CultureScope scope = new(culture);
        RememberedSessionViewModel vm = await LoadedAsync(new RememberedSession("token-1", s_later, "Lan", RememberedAccountKind.Microsoft));

        vm.ContinueText.Should().Be(expected);
    }

    [Fact]
    public async Task DisplayName_Guest_ShowsGuestNotTheServerNickname()
    {
        using CultureScope scope = new("en");
        RememberedSessionViewModel vm = await LoadedAsync(new RememberedSession("token-g", s_later, "guest-4821", RememberedAccountKind.Guest));

        vm.DisplayName.Should().Be("Guest");
        vm.ContinueText.Should().Be("Continue as Guest");
    }

    [Fact]
    public async Task ContinueAsync_ResumeAccepted_KeepsTheRolledToken()
    {
        MemoryRememberedSessionStore store = new() { Stored = new RememberedSession("token-old", s_later, "Lan", RememberedAccountKind.Google) };
        ScriptedAccountClient client = new() { ResumeReply = new AccountReply(AccountStatus.Ok, Session("token-rolled", s_later.AddDays(30), "Lan")) };
        RememberedSessionViewModel vm = Create(client, store, s_now);
        await vm.LoadAsync(CancellationToken.None);

        AccountReply reply = await vm.ContinueAsync(CancellationToken.None);

        reply.Status.Should().Be(AccountStatus.Ok);
        client.ResumedTokens.Should().Equal("token-old");
        store.Stored.Should().Be(new RememberedSession("token-rolled", s_later.AddDays(30), "Lan", RememberedAccountKind.Google));
        vm.Remembered.Should().Be(store.Stored);
    }

    [Fact]
    public async Task ContinueAsync_ResumeRefused_ForgetsTheSessionSoTheSignInReturns()
    {
        MemoryRememberedSessionStore store = new() { Stored = new RememberedSession("token-revoked", s_later, "Lan", RememberedAccountKind.Google) };
        ScriptedAccountClient client = new() { ResumeReply = new AccountReply(AccountStatus.InvalidToken, null) };
        RememberedSessionViewModel vm = Create(client, store, s_now);
        await vm.LoadAsync(CancellationToken.None);

        AccountReply reply = await vm.ContinueAsync(CancellationToken.None);

        reply.Status.Should().Be(AccountStatus.InvalidToken);
        vm.HasRemembered.Should().BeFalse();
        store.Stored.Should().BeNull();
    }

    [Fact]
    public async Task ContinueAsync_ConnectionDown_KeepsTheSessionForNextTime()
    {
        MemoryRememberedSessionStore store = new() { Stored = new RememberedSession("token-1", s_later, "Lan", RememberedAccountKind.Google) };
        RememberedSessionViewModel vm = Create(new ScriptedAccountClient { ResumeReply = null }, store, s_now);
        await vm.LoadAsync(CancellationToken.None);

        Func<Task> act = () => vm.ContinueAsync(CancellationToken.None);

        await act.Should().ThrowAsync<AccountConnectionException>();
        vm.HasRemembered.Should().BeTrue();
        store.Stored.Should().Be(new RememberedSession("token-1", s_later, "Lan", RememberedAccountKind.Google));
        store.Clears.Should().Be(0);
    }

    [Fact]
    public async Task ContinueAsync_NothingRemembered_Throws()
    {
        RememberedSessionViewModel vm = Create(new ScriptedAccountClient(), new MemoryRememberedSessionStore(), s_now);
        await vm.LoadAsync(CancellationToken.None);

        Func<Task> act = () => vm.ContinueAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RememberAsync_GoogleSignIn_StoresTheSessionTokenAndTheProvider()
    {
        MemoryRememberedSessionStore store = new();
        RememberedSessionViewModel vm = Create(new ScriptedAccountClient(), store, s_now);

        await vm.RememberAsync(Session("session-token", s_later, "Lan"), RememberedAccountKind.Google, CancellationToken.None);

        store.Stored.Should().Be(new RememberedSession("session-token", s_later, "Lan", RememberedAccountKind.Google));
        vm.HasRemembered.Should().BeTrue();
    }

    [Fact]
    public async Task RememberAsync_GuestSession_IsKeptAsAGuest_WhateverTheKind()
    {
        MemoryRememberedSessionStore store = new();
        RememberedSessionViewModel vm = Create(new ScriptedAccountClient(), store, s_now);

        await vm.RememberAsync(Guest("guest-token", s_later), RememberedAccountKind.Microsoft, CancellationToken.None);

        store.Stored!.Kind.Should().Be(RememberedAccountKind.Guest);
    }

    [Fact]
    public async Task RememberAsync_NoToken_StoresNothing()
    {
        MemoryRememberedSessionStore store = new();
        RememberedSessionViewModel vm = Create(new ScriptedAccountClient(), store, s_now);

        await vm.RememberAsync(Session(string.Empty, s_later, "Lan"), RememberedAccountKind.Google, CancellationToken.None);

        store.Saved.Should().BeEmpty();
        vm.HasRemembered.Should().BeFalse();
    }

    [Fact]
    public async Task ForgetAsync_ServerAnswers_RevokesTheSessionWithALogoutAndForgetsIt()
    {
        MemoryRememberedSessionStore store = new() { Stored = new RememberedSession("token-1", s_later, "Lan", RememberedAccountKind.Google) };
        ScriptedAccountClient client = new() { ResumeReply = new AccountReply(AccountStatus.Ok, Session("token-1", s_later, "Lan")) };
        RememberedSessionViewModel vm = Create(client, store, s_now);
        await vm.LoadAsync(CancellationToken.None);

        await vm.ForgetAsync(CancellationToken.None);

        client.Logouts.Should().Be(1);
        vm.HasRemembered.Should().BeFalse();
        store.Stored.Should().BeNull();
    }

    [Fact]
    public async Task ForgetAsync_ServerUnreachable_StillForgetsLocally()
    {
        MemoryRememberedSessionStore store = new() { Stored = new RememberedSession("token-1", s_later, "Lan", RememberedAccountKind.Google) };
        ScriptedAccountClient client = new() { ResumeReply = null };
        RememberedSessionViewModel vm = Create(client, store, s_now);
        await vm.LoadAsync(CancellationToken.None);

        await vm.ForgetAsync(CancellationToken.None);

        client.Logouts.Should().Be(0);
        vm.HasRemembered.Should().BeFalse();
        store.Stored.Should().BeNull();
    }

    [Fact]
    public async Task Form_GoogleSignIn_RemembersTheSessionTokenNotTheIdToken()
    {
        using CultureScope scope = new("en");
        MemoryRememberedSessionStore store = new();
        ScriptedAccountClient client = new();
        RememberedSessionViewModel remembered = Create(client, store, s_now);
        AccountFormViewModel form = FormWith(client, remembered, new FakeExternalSignIn("id-token-secret"));

        client.SignInReply = new AccountReply(AccountStatus.Ok, Session("session-token", s_later, "Lan"));
        await form.SignInWithAsync(ExternalProvider.Google, CancellationToken.None);

        store.Stored.Should().Be(new RememberedSession("session-token", s_later, "Lan", RememberedAccountKind.Google));
        store.Saved.Should().ContainSingle().Which.SessionToken.Should().NotContain("id-token-secret");
    }

    [Fact]
    public async Task Form_ContinueRefused_ShowsTheSignInWithTheReason()
    {
        using CultureScope scope = new("en");
        MemoryRememberedSessionStore store = new() { Stored = new RememberedSession("token-revoked", s_later, "Lan", RememberedAccountKind.Google) };
        ScriptedAccountClient client = new() { ResumeReply = new AccountReply(AccountStatus.InvalidToken, null) };
        RememberedSessionViewModel remembered = Create(client, store, s_now);
        await remembered.LoadAsync(CancellationToken.None);
        AccountFormViewModel form = FormWith(client, remembered, new FakeExternalSignIn("unused"));
        form.ShowsContinue.Should().BeTrue();

        await form.ContinueAsync(CancellationToken.None);

        form.ShowsContinue.Should().BeFalse();
        form.CurrentSession.Should().BeNull();
        form.ErrorText.Should().Be("Your session has expired. Please sign in again.");
    }

    [Fact]
    public async Task Form_ContinueAccepted_SignsInWithoutAnotherSignIn()
    {
        using CultureScope scope = new("en");
        MemoryRememberedSessionStore store = new() { Stored = new RememberedSession("token-1", s_later, "Lan", RememberedAccountKind.Google) };
        ScriptedAccountClient client = new() { ResumeReply = new AccountReply(AccountStatus.Ok, Session("token-2", s_later, "Lan")) };
        RememberedSessionViewModel remembered = Create(client, store, s_now);
        await remembered.LoadAsync(CancellationToken.None);
        AccountFormViewModel form = FormWith(client, remembered, new FakeExternalSignIn("unused"));
        AccountSession? signedIn = null;
        form.SignedIn += s => signedIn = s;

        await form.ContinueAsync(CancellationToken.None);

        signedIn.Should().NotBeNull();
        signedIn!.SessionToken.Should().Be("token-2");
        form.ShowsContinue.Should().BeFalse();
        store.Stored!.SessionToken.Should().Be("token-2");
    }

    private static AccountSession Session(string token, DateTimeOffset expires, string nickname)
        => new(Guid.NewGuid(), "farmer@example.com", token, expires, GameLanguage.English, false, false, nickname);

    private static AccountSession Guest(string token, DateTimeOffset expires)
        => new(Guid.NewGuid(), string.Empty, token, expires, GameLanguage.English, true, false, "guest-4821");

    private static RememberedSessionViewModel Create(IAccountClient client, IRememberedSessionStore store, DateTimeOffset now)
        => new(client, store, Text(), new FixedClock(now));

    private static async Task<RememberedSessionViewModel> LoadedAsync(RememberedSession stored)
    {
        RememberedSessionViewModel vm = Create(new ScriptedAccountClient(), new MemoryRememberedSessionStore { Stored = stored }, s_now);
        await vm.LoadAsync(CancellationToken.None);
        return vm;
    }

    private static AccountFormViewModel FormWith(IAccountClient client, RememberedSessionViewModel remembered, IExternalSignIn signIn)
        => new(client, signIn, s_configured, Text(), remembered);

    private static StringLocalizer<Language> Text()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<Language>(factory);
    }

    private sealed class FakeExternalSignIn(string token) : IExternalSignIn
    {
        public Task<string> GetIdTokenAsync(ExternalProvider provider, CancellationToken cancellationToken) => Task.FromResult(token);

        public Task RenderGoogleButtonAsync(string elementId, GameLanguage language, Func<string, Task> onIdToken, Func<ExternalSignInFailure, Task> onFailure)
            => Task.CompletedTask;
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
