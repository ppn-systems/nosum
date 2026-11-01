// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
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

[Collection(SharedUiCulture.Name)]
public sealed class AccountFormViewModelTests
{
    private static readonly AccountSession s_session = new(Guid.NewGuid(), "farmer01@gmail.com", "token", DateTimeOffset.UnixEpoch, GameLanguage.English, false, false, "Farmer01");
    private static readonly AccountSession s_guest = new(Guid.NewGuid(), string.Empty, "guest-token", DateTimeOffset.UnixEpoch, GameLanguage.Vietnamese, true, true, "guest-0001");
    private static readonly ExternalSignInOptions s_configured = new("google-client-id", "microsoft-client-id", "common");

    [Theory]
    [InlineData(ExternalProvider.Google)]
    [InlineData(ExternalProvider.Microsoft)]
    public async Task SignInWithAsync_ProviderButton_SendsExternalSignInWithThatProvider(ExternalProvider provider)
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        FakeExternalSignIn signIn = new() { Token = "id-token-1" };
        AccountFormViewModel vm = Create(client, signIn);
        AccountSession? signedIn = null;
        vm.SignedIn += s => signedIn = s;

        await vm.SignInWithAsync(provider, CancellationToken.None);

        signIn.Requested.Should().Equal(provider);
        client.Calls.Should().Equal($"external:{provider}:id-token-1:English");
        signedIn.Should().Be(s_session);
        vm.ErrorText.Should().BeNull();
        vm.IsBusy.Should().BeFalse();
        vm.IsAwaitingProvider.Should().BeFalse();
    }

    [Fact]
    public async Task SignInWithAsync_ProviderNotConfigured_IsRefusedLocally()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        FakeExternalSignIn signIn = new();
        AccountFormViewModel vm = Create(client, signIn, new ExternalSignInOptions(string.Empty, "microsoft-client-id", "common"));

        await vm.SignInWithAsync(ExternalProvider.Google, CancellationToken.None);

        vm.GoogleEnabled.Should().BeFalse();
        vm.ErrorText.Should().Be("Sign-in with this provider is not set up on the server yet.");
        signIn.Requested.Should().BeEmpty();
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public void ProviderHint_OneProviderOff_PointsToGuestPlay()
    {
        using CultureScope scope = new("en");
        AccountFormViewModel vm = Create(new FakeClient(null), new FakeExternalSignIn(), new ExternalSignInOptions(string.Empty, "microsoft-client-id", "common"));

        vm.ProviderHint.Should().Contain("Play as a guest");
    }

    [Fact]
    public void ProviderHint_BothProvidersOn_IsNull()
    {
        using CultureScope scope = new("en");
        AccountFormViewModel vm = Create(new FakeClient(null), new FakeExternalSignIn(), new ExternalSignInOptions("google-client-id", "microsoft-client-id", "common"));

        vm.ProviderHint.Should().BeNull();
    }

    [Theory]
    [InlineData("vi", "Không xác minh được tài khoản của bạn. Vui lòng thử lại.")]
    [InlineData("en", "We could not verify your account. Please try again.")]
    public async Task SignInWithAsync_ServerRefusesTheToken_ShowsInvalidIdTokenInCurrentLanguage(string language, string expected)
    {
        using CultureScope scope = new(language);
        AccountFormViewModel vm = Create(new FakeClient(new AccountReply(AccountStatus.InvalidIdToken, null)), new FakeExternalSignIn());

        await vm.SignInWithAsync(ExternalProvider.Google, CancellationToken.None);

        vm.ErrorText.Should().Be(expected);
        vm.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task AttachGoogleButtonAsync_DrawsGoogleButtonInTheSlotInTheUiLanguage()
    {
        using CultureScope scope = new("vi");
        FakeExternalSignIn signIn = new();
        AccountFormViewModel vm = Create(new FakeClient(null), signIn);

        await vm.AttachGoogleButtonAsync("account-google-slot");

        signIn.GoogleElementId.Should().Be("account-google-slot");
        signIn.GoogleLanguage.Should().Be(GameLanguage.Vietnamese);
        signIn.RenderedGoogleButtons.Should().Be(1);
    }

    [Fact]
    public async Task CompleteGoogleAsync_CredentialFromTheButton_SignsInWithGoogleAndSendsTheToken()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        FakeExternalSignIn signIn = new();
        AccountFormViewModel vm = Create(client, signIn);
        AccountSession? signedIn = null;
        vm.SignedIn += s => signedIn = s;
        await vm.AttachGoogleButtonAsync("account-google-slot");

        await signIn.ClickGoogleButton("google-token-1");

        client.Calls.Should().Equal("external:Google:google-token-1:English");
        signIn.Requested.Should().BeEmpty();
        signedIn.Should().Be(s_session);
        vm.ErrorText.Should().BeNull();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task CompleteGoogleAsync_UpgradeMode_LinksGoogleWithTheNicknameTyped()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
        FakeExternalSignIn signIn = new();
        AccountFormViewModel vm = await GuestAsync(client, signIn);
        client.Calls.Clear();
        vm.BeginUpgrade();
        vm.Nickname = "Farmer02";
        await vm.AttachGoogleButtonAsync("account-google-slot");

        await signIn.ClickGoogleButton("google-token-2");

        client.Calls.Should().Equal("link:Google:google-token-2:Farmer02");
    }

    [Fact]
    public async Task ReportGoogleFailure_ButtonCouldNotBeDrawn_ShowsTheTextForThatFailure()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(null);
        FakeExternalSignIn signIn = new();
        AccountFormViewModel vm = Create(client, signIn);
        await vm.AttachGoogleButtonAsync("account-google-slot");

        await signIn.FailGoogleButton(ExternalSignInFailure.Failed);

        vm.ErrorText.Should().Be(Text()["Account_Error_ProviderFailed"].Value);
        client.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExternalSignInFailure.Cancelled, "Sign-in was cancelled.")]
    [InlineData(ExternalSignInFailure.PopupBlocked, "Your browser blocked the sign-in window. Allow pop-ups for this site and try again.")]
    [InlineData(ExternalSignInFailure.NotDisplayed, "Google could not show its sign-in prompt. Check your browser settings and try again.")]
    [InlineData(ExternalSignInFailure.Failed, "Sign-in did not succeed. Please try again.")]
    public async Task SignInWithAsync_ProviderFails_ShowsTheTextForThatFailureAndSendsNothing(ExternalSignInFailure failure, string expected)
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client, new FakeExternalSignIn { Failure = failure });

        await vm.SignInWithAsync(ExternalProvider.Google, CancellationToken.None);

        vm.ErrorText.Should().Be(expected);
        client.Calls.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task SignInWithAsync_ConnectionFails_ShowsConnectionTextAndStaysUsable()
    {
        using CultureScope scope = new("en");
        AccountFormViewModel vm = Create(new FakeClient(null), new FakeExternalSignIn());

        await vm.SignInWithAsync(ExternalProvider.Microsoft, CancellationToken.None);

        vm.ErrorText.Should().Be("Could not reach the server. Please try again.");
        vm.IsBusy.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("has-hyphen")]
    [InlineData("Admin")]
    [InlineData("Nông Dân")]
    [InlineData("Fuck_You")]
    public async Task LinkIdentityAsync_NicknameNotAllowed_ShowsTheNicknameTextAndAsksNoProvider(string nickname)
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
        FakeExternalSignIn signIn = new();
        AccountFormViewModel vm = await GuestAsync(client, signIn);
        client.Calls.Clear();
        vm.BeginUpgrade();
        vm.Nickname = nickname;

        await vm.LinkIdentityAsync(ExternalProvider.Google, CancellationToken.None);

        vm.ErrorText.Should().NotBeNullOrEmpty();
        vm.ErrorText.Should().NotBe(Text()["Account_Error_ProviderFailed"].Value);
        signIn.Requested.Should().BeEmpty();
        client.Calls.Should().BeEmpty();
        vm.Mode.Should().Be(AccountFormMode.Upgrade);
    }

    [Fact]
    public async Task LinkIdentityAsync_NicknameMissing_ShowsTheInvalidNicknameText()
    {
        using CultureScope scope = new("en");
        AccountFormViewModel vm = await GuestAsync(new FakeClient(new AccountReply(AccountStatus.Ok, s_guest)), new FakeExternalSignIn());
        vm.BeginUpgrade();

        await vm.LinkIdentityAsync(ExternalProvider.Microsoft, CancellationToken.None);

        vm.ErrorText.Should().Be(Text()["Account_Status_InvalidNickname"].Value);
    }

    [Fact]
    public async Task LinkIdentityAsync_GuestWithNickname_SendsTheLinkAndRaisesSessionUpdated()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
        FakeExternalSignIn signIn = new() { Token = "id-token-2" };
        AccountFormViewModel vm = await GuestAsync(client, signIn);
        client.Reply = new AccountReply(AccountStatus.Ok, s_session);
        client.Calls.Clear();
        int signedIn = 0;
        AccountSession? updated = null;
        vm.SignedIn += _ => signedIn++;
        vm.SessionUpdated += s => updated = s;
        vm.BeginUpgrade();
        vm.Nickname = "  Farmer01 ";

        await vm.LinkIdentityAsync(ExternalProvider.Microsoft, CancellationToken.None);

        signIn.Requested.Should().Equal(ExternalProvider.Microsoft);
        client.Calls.Should().Equal("link:Microsoft:id-token-2:Farmer01");
        updated.Should().Be(s_session);
        signedIn.Should().Be(0);
        vm.Mode.Should().Be(AccountFormMode.Login);
        vm.ShowsForm.Should().BeFalse();
        vm.ErrorText.Should().BeNull();
    }

    [Theory]
    [InlineData("vi", "Tài khoản Google hoặc Microsoft này đã được liên kết với người chơi khác.")]
    [InlineData("en", "That Google or Microsoft account is already linked to another player.")]
    public async Task LinkIdentityAsync_IdentityTaken_ShowsItsTextAndStaysInUpgrade(string language, string expected)
    {
        using CultureScope scope = new(language);
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
        AccountFormViewModel vm = await GuestAsync(client, new FakeExternalSignIn());
        client.Reply = new AccountReply(AccountStatus.IdentityTaken, null);
        vm.BeginUpgrade();
        vm.Nickname = "Farmer01";

        await vm.LinkIdentityAsync(ExternalProvider.Google, CancellationToken.None);

        vm.ErrorText.Should().Be(expected);
        vm.Mode.Should().Be(AccountFormMode.Upgrade);
        vm.CurrentSession.Should().Be(s_guest);
    }

    [Fact]
    public async Task LinkIdentityAsync_ProviderNotConfigured_IsRefusedBeforeAnyWindowOpens()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
        FakeExternalSignIn signIn = new();
        AccountFormViewModel vm = await GuestAsync(client, signIn, new ExternalSignInOptions(string.Empty, string.Empty, "common"));
        vm.BeginUpgrade();
        vm.Nickname = "Farmer01";

        await vm.LinkIdentityAsync(ExternalProvider.Google, CancellationToken.None);

        vm.ErrorText.Should().Be("Sign-in with this provider is not set up on the server yet.");
        signIn.Requested.Should().BeEmpty();
        client.Calls.Should().NotContain(c => c.StartsWith("link:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlayAsGuestAsync_Ok_RaisesSignedInWithTheGuestSession()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
        AccountFormViewModel vm = Create(client, new FakeExternalSignIn());
        AccountSession? signedIn = null;
        vm.SignedIn += s => signedIn = s;

        await vm.PlayAsGuestAsync(CancellationToken.None);

        signedIn.Should().Be(s_guest);
        vm.CurrentSession.Should().Be(s_guest);
        vm.ShowsForm.Should().BeFalse();
        client.Calls.Should().Equal("guest:English");
    }

    [Fact]
    public void BeginUpgrade_NotSignedIn_DoesNothing()
    {
        AccountFormViewModel vm = Create(new FakeClient(null), new FakeExternalSignIn());

        vm.BeginUpgrade();

        vm.Mode.Should().Be(AccountFormMode.Login);
    }

    [Fact]
    public async Task CanUpgrade_GuestWithPromptUnlocked_IsTrueOtherwiseFalse()
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client, new FakeExternalSignIn());
        await vm.PlayAsGuestAsync(CancellationToken.None);
        bool registered = vm.CanUpgrade;
        client.Reply = new AccountReply(AccountStatus.Ok, s_guest);
        AccountFormViewModel guestVm = Create(client, new FakeExternalSignIn());
        await guestVm.PlayAsGuestAsync(CancellationToken.None);

        registered.Should().BeFalse();
        guestVm.CanUpgrade.Should().BeTrue();
    }

    [Fact]
    public async Task LogoutAsync_SignedIn_CallsClientClearsSessionAndRaisesSignedOut()
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client, new FakeExternalSignIn());
        await vm.PlayAsGuestAsync(CancellationToken.None);
        bool signedOut = false;
        vm.SignedOut += () => signedOut = true;

        await vm.LogoutAsync(CancellationToken.None);

        signedOut.Should().BeTrue();
        vm.CurrentSession.Should().BeNull();
        vm.ShowsForm.Should().BeTrue();
        client.Calls.Should().Contain("logout");
    }

    [Fact]
    public async Task LogoutAsync_NotSignedIn_DoesNothing()
    {
        FakeClient client = new(null);
        AccountFormViewModel vm = Create(client, new FakeExternalSignIn());

        await vm.LogoutAsync(CancellationToken.None);

        client.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("vi", "Đăng nhập bằng Google")]
    [InlineData("en", "Sign in with Google")]
    public void SignInGoogleText_SwitchesWithCurrentCulture(string language, string expected)
    {
        AccountFormViewModel vm = Create(new FakeClient(null), new FakeExternalSignIn());

        string text;
        using (new CultureScope(language))
        {
            text = vm.SignInGoogleText;
        }

        text.Should().Be(expected);
    }

    [Fact]
    public void Resources_EnglishAndVietnameseDefineTheSameKeys()
    {
        HashSet<string> vi = Keys("Language.resx");
        HashSet<string> en = Keys("Language.en.resx");

        en.Should().BeEquivalentTo(vi);
    }

    [Fact]
    public void Resources_EveryStatusTheServerSendsHasTextInBothLanguages()
    {
        // Codes the server still sends (docs/adr/ADR-069). Ok is a success and has no text.
        AccountStatus[] live =
        [
            AccountStatus.InvalidToken, AccountStatus.InvalidLanguage, AccountStatus.NotGuest, AccountStatus.SessionReplaced,
            AccountStatus.InvalidNickname, AccountStatus.NicknameReserved, AccountStatus.NicknameTaken, AccountStatus.NicknameTooSoon,
            AccountStatus.NicknameProfane, AccountStatus.NicknameRequiresAccount, AccountStatus.TooManyRequests, AccountStatus.Banned,
            AccountStatus.SessionKicked, AccountStatus.InvalidIdToken, AccountStatus.IdentityTaken,
        ];
        string[] expected = live.Select(s => "Account_Status_" + s).ToArray();

        Keys("Language.resx").Should().Contain(expected);
        Keys("Language.en.resx").Should().Contain(expected);
    }

    [Fact]
    public void Resources_RetiredStatusesHaveNoText()
    {
        // Retired with the password accounts and account recovery (docs/adr/ADR-069): never sent, so never shown.
        AccountStatus[] removed =
        [
            AccountStatus.InvalidEmail, AccountStatus.InvalidPassword, AccountStatus.EmailTaken, AccountStatus.InvalidCredentials,
            AccountStatus.InvalidResetCode, AccountStatus.SessionRevoked,
        ];
        string[] retired = removed.Select(s => "Account_Status_" + s).ToArray();

        Keys("Language.resx").Should().NotContain(retired);
        Keys("Language.en.resx").Should().NotContain(retired);
    }

    [Fact]
    public async Task SetLanguageAsync_SignedIn_SwitchesTheUiAtOnceAndStoresItOnTheAccount()
    {
        using CultureScope scope = new("vi");
        CultureInfo? defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
            AccountFormViewModel vm = Create(client, new FakeExternalSignIn());
            await vm.PlayAsGuestAsync(CancellationToken.None);
            bool changed = false;
            vm.Changed += () => changed = true;

            await vm.SetLanguageAsync(GameLanguage.English, CancellationToken.None);

            vm.Language.Should().Be(GameLanguage.English);
            vm.CurrentSession!.Language.Should().Be(GameLanguage.English);
            vm.LanguageNote.Should().BeNull();
            changed.Should().BeTrue();
            client.Calls.Should().Contain("language:English");
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
        }
    }

    [Fact]
    public async Task SetLanguageAsync_ServerRefuses_KeepsTheNewLanguageAndSaysItWasNotSaved()
    {
        using CultureScope scope = new("vi");
        CultureInfo? defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest)) { LanguageStatus = AccountStatus.InvalidLanguage };
            AccountFormViewModel vm = Create(client, new FakeExternalSignIn());
            await vm.PlayAsGuestAsync(CancellationToken.None);

            await vm.SetLanguageAsync(GameLanguage.English, CancellationToken.None);

            vm.Language.Should().Be(GameLanguage.English);
            vm.LanguageNote.Should().NotBeNullOrEmpty();
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = defaultCulture;
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
        }
    }

    [Fact]
    public async Task SetLanguageAsync_SameLanguage_SendsNothing()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client, new FakeExternalSignIn());
        await vm.PlayAsGuestAsync(CancellationToken.None);

        await vm.SetLanguageAsync(GameLanguage.English, CancellationToken.None);

        client.Calls.Should().NotContain(c => c.StartsWith("language:", StringComparison.Ordinal));
    }

    private static StringLocalizer<Language> Text()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<Language>(factory);
    }

    private static AccountFormViewModel Create(IAccountClient client, FakeExternalSignIn signIn, ExternalSignInOptions? options = null)
        => new(client, signIn, options ?? s_configured, Text(), new RememberedSessionViewModel(client, new MemoryRememberedSessionStore(), Text(), TimeProvider.System));

    // Signs in as the guest in the form (the session the upgrade starts from).
    private static async Task<AccountFormViewModel> GuestAsync(FakeClient client, FakeExternalSignIn signIn, ExternalSignInOptions? options = null)
    {
        AccountFormViewModel vm = Create(client, signIn, options);
        await vm.PlayAsGuestAsync(CancellationToken.None);
        return vm;
    }

    private static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }

    private sealed class FakeClient : IAccountClient
    {
        public FakeClient(AccountReply? reply) => this.Reply = reply;

        public AccountReply? Reply { get; set; }

        public List<string> Calls { get; } = [];

        public AccountStatus LanguageStatus { get; set; } = AccountStatus.Ok;

        public Task<AccountReply> SignInWithExternalAsync(ExternalProvider provider, string idToken, GameLanguage language, CancellationToken cancellationToken)
        {
            this.Calls.Add($"external:{provider}:{idToken}:{language}");
            return Answer(this.Reply);
        }

        public Task<AccountReply> LinkExternalIdentityAsync(ExternalProvider provider, string idToken, string nickname, CancellationToken cancellationToken)
        {
            this.Calls.Add($"link:{provider}:{idToken}:{nickname}");
            return Answer(this.Reply);
        }

        public Task<AccountReply> StartGuestAsync(GameLanguage language, CancellationToken cancellationToken)
        {
            this.Calls.Add($"guest:{language}");
            return Answer(this.Reply);
        }

        public Task<NicknameReply> SetNicknameAsync(string nickname, CancellationToken cancellationToken)
            => Task.FromResult(new NicknameReply(AccountStatus.Ok, nickname, null));

        public Task<AccountStatus> SetLanguageAsync(GameLanguage language, CancellationToken cancellationToken)
        {
            this.Calls.Add($"language:{language}");
            return this.Reply is null ? throw new AccountConnectionException("test") : Task.FromResult(this.LanguageStatus);
        }

        public Task LogoutAsync(CancellationToken cancellationToken)
        {
            this.Calls.Add("logout");
            return Task.CompletedTask;
        }

        public Task<AccountReply> ResumeAsync(string sessionToken, CancellationToken cancellationToken)
            => Answer(this.Reply);

        private static Task<AccountReply> Answer(AccountReply? reply)
            => reply is null ? throw new AccountConnectionException("test") : Task.FromResult(reply);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
