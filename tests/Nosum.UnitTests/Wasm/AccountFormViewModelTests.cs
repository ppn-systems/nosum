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

public sealed class AccountFormViewModelTests
{
    private static readonly AccountSession s_session = new(Guid.NewGuid(), "farmer01@gmail.com", "token", DateTimeOffset.UnixEpoch, GameLanguage.English, false, false, "Farmer01");
    private static readonly AccountSession s_guest = new(Guid.NewGuid(), string.Empty, "guest-token", DateTimeOffset.UnixEpoch, GameLanguage.Vietnamese, true, true, "guest-0001");

    [Fact]
    public async Task SubmitAsync_LoginOk_RaisesSignedInAndClearsPassword()
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
        vm.Email = "farmer01@gmail.com";
        vm.Password = "password-1";
        AccountSession? signedIn = null;
        vm.SignedIn += s => signedIn = s;

        await vm.SubmitAsync(CancellationToken.None);

        signedIn.Should().Be(s_session);
        vm.Password.Should().BeEmpty();
        vm.ErrorText.Should().BeNull();
        vm.IsBusy.Should().BeFalse();
        client.Calls.Should().Equal("login:farmer01@gmail.com");
    }

    [Fact]
    public async Task SubmitAsync_RegisterOk_SendsCurrentUiLanguage()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
        vm.SwitchMode();
        vm.Email = "farmer01@gmail.com";
        vm.Password = "password-1";
        vm.ConfirmPassword = "password-1";
        vm.Nickname = "  Nong Dan_1 ";

        await vm.SubmitAsync(CancellationToken.None);

        client.Calls.Should().Equal("register:farmer01@gmail.com:Nong Dan_1:English");
    }

    [Theory]
    [InlineData("ab", AccountStatus.InvalidNickname)]
    [InlineData("has-hyphen", AccountStatus.InvalidNickname)]
    [InlineData("Admin", AccountStatus.NicknameReserved)]
    [InlineData("Nông Dân", AccountStatus.InvalidNickname)]
    [InlineData("Mình", AccountStatus.InvalidNickname)]
    [InlineData("Fuck_You", AccountStatus.NicknameProfane)]
    [InlineData("vcl 99", AccountStatus.NicknameProfane)]
    public async Task SubmitAsync_RegisterWithBadNickname_ShowsTheCodeTextAndSendsNothing(string nickname, AccountStatus expected)
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
        vm.SwitchMode();
        vm.Email = "farmer01@gmail.com";
        vm.Password = "password-1";
        vm.ConfirmPassword = "password-1";
        vm.Nickname = nickname;

        await vm.SubmitAsync(CancellationToken.None);

        client.Calls.Should().BeEmpty();
        vm.ErrorText.Should().Be(Text()["Account_Status_" + expected].Value);
    }

    [Fact]
    public async Task SubmitAsync_RegisterPasswordsDiffer_ShowsMismatchAndSendsNothing()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
        vm.SwitchMode();
        vm.Email = "farmer01@gmail.com";
        vm.Password = "password-1";
        vm.ConfirmPassword = "password-2";

        await vm.SubmitAsync(CancellationToken.None);

        vm.ErrorText.Should().Be("The two passwords do not match.");
        client.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("vi", "Sai email hoặc mật khẩu.")]
    [InlineData("en", "Wrong email or password.")]
    public async Task SubmitAsync_ServerRefusesCredentials_ShowsTextInCurrentLanguage(string language, string expected)
    {
        using CultureScope scope = new(language);
        AccountFormViewModel vm = Create(new FakeClient(new AccountReply(AccountStatus.InvalidCredentials, null)));
        vm.Email = "farmer01@gmail.com";
        vm.Password = "password-1";

        await vm.SubmitAsync(CancellationToken.None);

        vm.ErrorText.Should().Be(expected);
    }

    [Fact]
    public async Task SubmitAsync_ConnectionFails_ShowsConnectionTextAndStaysUsable()
    {
        using CultureScope scope = new("en");
        AccountFormViewModel vm = Create(new FakeClient(null));
        vm.Email = "farmer01@gmail.com";
        vm.Password = "password-1";

        await vm.SubmitAsync(CancellationToken.None);

        vm.ErrorText.Should().Be("Could not reach the server. Please try again.");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_EmailNotGmail_IsRefusedLocally()
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
        vm.Email = "farmer01@yahoo.com";
        vm.Password = "password-1";

        await vm.SubmitAsync(CancellationToken.None);

        vm.ErrorText.Should().NotBeNullOrEmpty();
        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Labels_SwitchWithCurrentCulture()
    {
        AccountFormViewModel vm = Create(new FakeClient(null));

        string vietnamese;
        string english;
        using (new CultureScope("vi"))
        {
            vietnamese = vm.EmailLabel;
        }

        using (new CultureScope("en"))
        {
            english = vm.EmailLabel;
        }

        vietnamese.Should().Be("Địa chỉ Gmail");
        english.Should().Be("Gmail address");
    }

    [Fact]
    public void Resources_EnglishAndVietnameseDefineTheSameKeys()
    {
        HashSet<string> vi = Keys("Language.resx");
        HashSet<string> en = Keys("Language.en.resx");

        en.Should().BeEquivalentTo(vi);
    }

    [Fact]
    public void Resources_EveryRefusalStatusHasTextInBothLanguages()
    {
        string[] expected = Enum.GetValues<AccountStatus>()
            .Where(s => s != AccountStatus.Ok)
            .Select(s => "Account_Status_" + s)
            .ToArray();

        Keys("Language.resx").Should().Contain(expected);
        Keys("Language.en.resx").Should().Contain(expected);
    }

    [Fact]
    public async Task PlayAsGuestAsync_Ok_RaisesSignedInWithTheGuestSession()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
        AccountFormViewModel vm = Create(client);
        AccountSession? signedIn = null;
        vm.SignedIn += s => signedIn = s;

        await vm.PlayAsGuestAsync(CancellationToken.None);

        signedIn.Should().Be(s_guest);
        vm.CurrentSession.Should().Be(s_guest);
        vm.ShowsForm.Should().BeFalse();
        client.Calls.Should().Equal("guest:English");
    }

    [Fact]
    public async Task SubmitAsync_UpgradeOk_RaisesSessionUpdatedNotSignedInAndLeavesUpgradeMode()
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
        AccountFormViewModel vm = Create(client);
        await vm.PlayAsGuestAsync(CancellationToken.None);
        client.Reply = new AccountReply(AccountStatus.Ok, s_session);
        int signedIn = 0;
        AccountSession? updated = null;
        vm.SignedIn += _ => signedIn++;
        vm.SessionUpdated += s => updated = s;
        vm.BeginUpgrade();
        vm.Email = "farmer01@gmail.com";
        vm.Password = "password-1";
        vm.ConfirmPassword = "password-1";
        vm.Nickname = "Farmer01";

        await vm.SubmitAsync(CancellationToken.None);

        updated.Should().Be(s_session);
        signedIn.Should().Be(0);
        vm.Mode.Should().Be(AccountFormMode.Login);
        vm.ShowsForm.Should().BeFalse();
        client.Calls.Should().Contain("upgrade:farmer01@gmail.com:Farmer01");
    }

    [Theory]
    [InlineData("Nông Dân", AccountStatus.InvalidNickname)]
    [InlineData("Dit_Me", AccountStatus.NicknameProfane)]
    public async Task SubmitAsync_UpgradeWithBadNickname_ShowsTheCodeTextAndSendsNothing(string nickname, AccountStatus expected)
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
        AccountFormViewModel vm = Create(client);
        await vm.PlayAsGuestAsync(CancellationToken.None);
        client.Calls.Clear();
        vm.BeginUpgrade();
        vm.Email = "farmer01@gmail.com";
        vm.Password = "password-1";
        vm.ConfirmPassword = "password-1";
        vm.Nickname = nickname;

        await vm.SubmitAsync(CancellationToken.None);

        client.Calls.Should().BeEmpty();
        vm.ErrorText.Should().Be(Text()["Account_Status_" + expected].Value);
        vm.Mode.Should().Be(AccountFormMode.Upgrade);
    }

    [Fact]
    public void BeginUpgrade_NotSignedIn_DoesNothing()
    {
        AccountFormViewModel vm = Create(new FakeClient(null));

        vm.BeginUpgrade();

        vm.Mode.Should().Be(AccountFormMode.Login);
    }

    [Fact]
    public async Task CanUpgrade_GuestWithPromptUnlocked_IsTrueOtherwiseFalse()
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
        await vm.PlayAsGuestAsync(CancellationToken.None);
        bool registered = vm.CanUpgrade;
        client.Reply = new AccountReply(AccountStatus.Ok, s_guest);
        AccountFormViewModel guestVm = Create(client);
        await guestVm.PlayAsGuestAsync(CancellationToken.None);

        registered.Should().BeFalse();
        guestVm.CanUpgrade.Should().BeTrue();
    }

    [Fact]
    public async Task LogoutAsync_SignedIn_CallsClientClearsSessionAndRaisesSignedOut()
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
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
        AccountFormViewModel vm = Create(client);

        await vm.LogoutAsync(CancellationToken.None);

        client.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetLanguageAsync_SignedIn_SwitchesTheUiAtOnceAndStoresItOnTheAccount()
    {
        using CultureScope scope = new("vi");
        CultureInfo? defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            FakeClient client = new(new AccountReply(AccountStatus.Ok, s_guest));
            AccountFormViewModel vm = Create(client);
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
            AccountFormViewModel vm = Create(client);
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
        AccountFormViewModel vm = Create(client);
        await vm.PlayAsGuestAsync(CancellationToken.None);

        await vm.SetLanguageAsync(GameLanguage.English, CancellationToken.None);

        client.Calls.Should().NotContain(c => c.StartsWith("language:", StringComparison.Ordinal));
    }

    private static StringLocalizer<Language> Text()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<Language>(factory);
    }

    private static AccountFormViewModel Create(IAccountClient client) => new(client, Text());

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

        public Task<AccountReply> RegisterAsync(string email, string password, string nickname, GameLanguage language, CancellationToken cancellationToken)
        {
            this.Calls.Add($"register:{email}:{nickname}:{language}");
            return Answer();
        }

        public Task<AccountReply> LoginAsync(string email, string password, CancellationToken cancellationToken)
        {
            this.Calls.Add($"login:{email}");
            return Answer();
        }

        public Task<AccountReply> StartGuestAsync(GameLanguage language, CancellationToken cancellationToken)
        {
            this.Calls.Add($"guest:{language}");
            return Answer();
        }

        public Task<AccountReply> UpgradeGuestAsync(string email, string password, string nickname, CancellationToken cancellationToken)
        {
            this.Calls.Add($"upgrade:{email}:{nickname}");
            return Answer();
        }

        public Task<NicknameReply> SetNicknameAsync(string nickname, CancellationToken cancellationToken)
            => Task.FromResult(new NicknameReply(AccountStatus.Ok, nickname, null));

        public AccountStatus LanguageStatus { get; set; } = AccountStatus.Ok;

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
            => Answer();

        private Task<AccountReply> Answer()
            => this.Reply is null ? throw new AccountConnectionException("test") : Task.FromResult(this.Reply);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
