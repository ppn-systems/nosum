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
using Nosum.Wasm.ViewModels.Account;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class AccountFormViewModelTests
{
    private static readonly AccountSession s_session = new(Guid.NewGuid(), "farmer", "token", DateTimeOffset.UnixEpoch, "en");

    [Fact]
    public async Task SubmitAsync_LoginOk_RaisesSignedInAndClearsPassword()
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
        vm.Username = "farmer";
        vm.Password = "password-1";
        AccountSession? signedIn = null;
        vm.SignedIn += s => signedIn = s;

        await vm.SubmitAsync(CancellationToken.None);

        signedIn.Should().Be(s_session);
        vm.Password.Should().BeEmpty();
        vm.ErrorText.Should().BeNull();
        vm.IsBusy.Should().BeFalse();
        client.Calls.Should().Equal("login:farmer");
    }

    [Fact]
    public async Task SubmitAsync_RegisterOk_SendsCurrentUiLanguage()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
        vm.SwitchMode();
        vm.Username = "farmer";
        vm.Password = "password-1";
        vm.ConfirmPassword = "password-1";

        await vm.SubmitAsync(CancellationToken.None);

        client.Calls.Should().Equal("register:farmer:en");
    }

    [Fact]
    public async Task SubmitAsync_RegisterPasswordsDiffer_ShowsMismatchAndSendsNothing()
    {
        using CultureScope scope = new("en");
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
        vm.SwitchMode();
        vm.Username = "farmer";
        vm.Password = "password-1";
        vm.ConfirmPassword = "password-2";

        await vm.SubmitAsync(CancellationToken.None);

        vm.ErrorText.Should().Be("The two passwords do not match.");
        client.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("vi", "Sai tên đăng nhập hoặc mật khẩu.")]
    [InlineData("en", "Wrong username or password.")]
    public async Task SubmitAsync_ServerRefusesCredentials_ShowsTextInCurrentLanguage(string language, string expected)
    {
        using CultureScope scope = new(language);
        AccountFormViewModel vm = Create(new FakeClient(new AccountReply(AccountStatus.InvalidCredentials, null)));
        vm.Username = "farmer";
        vm.Password = "password-1";

        await vm.SubmitAsync(CancellationToken.None);

        vm.ErrorText.Should().Be(expected);
    }

    [Fact]
    public async Task SubmitAsync_ConnectionFails_ShowsConnectionTextAndStaysUsable()
    {
        using CultureScope scope = new("en");
        AccountFormViewModel vm = Create(new FakeClient(null));
        vm.Username = "farmer";
        vm.Password = "password-1";

        await vm.SubmitAsync(CancellationToken.None);

        vm.ErrorText.Should().Be("Could not reach the server. Please try again.");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task SubmitAsync_UsernameTooShort_IsRefusedLocally()
    {
        FakeClient client = new(new AccountReply(AccountStatus.Ok, s_session));
        AccountFormViewModel vm = Create(client);
        vm.Username = "ab";
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
            vietnamese = vm.UsernameLabel;
        }

        using (new CultureScope("en"))
        {
            english = vm.UsernameLabel;
        }

        vietnamese.Should().Be("Tên đăng nhập");
        english.Should().Be("Username");
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

    private static AccountFormViewModel Create(IAccountClient client)
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new AccountFormViewModel(client, new StringLocalizer<Language>(factory));
    }

    private static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }

    private sealed class FakeClient : IAccountClient
    {
        private readonly AccountReply? _reply;

        public FakeClient(AccountReply? reply) => _reply = reply;

        public List<string> Calls { get; } = [];

        public Task<AccountReply> RegisterAsync(string username, string password, string language, CancellationToken cancellationToken)
        {
            this.Calls.Add($"register:{username}:{language}");
            return Reply();
        }

        public Task<AccountReply> LoginAsync(string username, string password, CancellationToken cancellationToken)
        {
            this.Calls.Add($"login:{username}");
            return Reply();
        }

        public Task<AccountReply> ResumeAsync(string sessionToken, CancellationToken cancellationToken)
            => Reply();

        private Task<AccountReply> Reply()
            => _reply is null ? throw new AccountConnectionException("test") : Task.FromResult(_reply);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
