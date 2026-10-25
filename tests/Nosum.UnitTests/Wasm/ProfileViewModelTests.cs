// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.ViewModels.Account;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Profile;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The profile panel's view model (docs/adr/ADR-009 amendments): guests cannot rename, registered players get early feedback on the rules.</summary>
public sealed class ProfileViewModelTests
{
    [Fact]
    public async Task Guest_CannotRename_SeesWhyAndNothingIsSent()
    {
        using CultureScope scope = new("en");
        (ProfileViewModel vm, FakeClient client) = Create();
        vm.Begin("guest-0042", isGuest: true);
        vm.NewNickname = "Farmer_Joe";

        await vm.SaveAsync(CancellationToken.None);

        vm.IsGuest.Should().BeTrue();
        vm.CanRename.Should().BeFalse();
        vm.GuestNoticeText.Should().Be("Guests keep their generated name. Register an account to choose your own nickname.");
        vm.CurrentText.Should().Be("Current nickname: Guest 0042");
        vm.Status.Should().Be("Register an account to choose a nickname.");
        client.Sent.Should().BeEmpty();
    }

    [Fact]
    public void Registering_TurnsTheRenameFormOn()
    {
        (ProfileViewModel vm, _) = Create();
        vm.Begin("guest-0042", isGuest: true);

        vm.Begin("Farmer_Joe", isGuest: false);

        vm.CanRename.Should().BeTrue();
        vm.Nickname.Should().Be("Farmer_Joe");
    }

    [Theory]
    [InlineData("Nông Dân", AccountStatus.InvalidNickname)]
    [InlineData("Mình", AccountStatus.InvalidNickname)]
    [InlineData("ab", AccountStatus.InvalidNickname)]
    [InlineData("Admin", AccountStatus.NicknameReserved)]
    [InlineData("Fuck_You", AccountStatus.NicknameProfane)]
    [InlineData("vcl_99", AccountStatus.NicknameProfane)]
    public async Task Registered_BadNickname_ShowsTheCodeTextAndSendsNothing(string typed, AccountStatus expected)
    {
        (ProfileViewModel vm, FakeClient client) = Create();
        vm.Begin("Farmer_Joe", isGuest: false);
        vm.NewNickname = typed;

        await vm.SaveAsync(CancellationToken.None);

        client.Sent.Should().BeEmpty();
        vm.Status.Should().Be(Text()["Account_Status_" + expected].Value);
    }

    [Fact]
    public async Task Registered_GoodNickname_IsSentAndShown()
    {
        (ProfileViewModel vm, FakeClient client) = Create();
        vm.Begin("Farmer_Joe", isGuest: false);
        vm.NewNickname = " Nguyen_Van ";

        await vm.SaveAsync(CancellationToken.None);

        client.Sent.Should().Equal("Nguyen_Van");
        vm.Nickname.Should().Be("Nguyen_Van");
        vm.NewNickname.Should().BeEmpty();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("vi")]
    public void NicknameRuleTexts_SayLettersWithoutAccentsAndDigits(string culture)
    {
        using CultureScope scope = new(culture);
        (ProfileViewModel vm, _) = Create();

        string hint = vm.RuleText;

        hint.Should().Contain(culture == "en" ? "without accents" : "không dấu");
        hint.Should().NotContain("(có dấu)").And.NotContain("accents allowed");
    }

    private static StringLocalizer<Language> Text()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<Language>(factory);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The look channel lives as long as the test.")]
    private static (ProfileViewModel Vm, FakeClient Client) Create()
    {
        FakeClient client = new();
        NicknameFormatter names = new(Text(), new LookChannel(new FakeGameSocket(), () => 0));
        return (new ProfileViewModel(client, names, Text()), client);
    }

    private sealed class FakeClient : IAccountClient
    {
        public List<string> Sent { get; } = [];

        public Task<NicknameReply> SetNicknameAsync(string nickname, CancellationToken cancellationToken)
        {
            this.Sent.Add(nickname);
            return Task.FromResult(new NicknameReply(AccountStatus.Ok, nickname, null));
        }

        public Task<AccountReply> RegisterAsync(string email, string password, string nickname, GameLanguage language, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountReply> LoginAsync(string email, string password, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountReply> StartGuestAsync(GameLanguage language, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountReply> UpgradeGuestAsync(string email, string password, string nickname, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task LogoutAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<AccountReply> ResumeAsync(string sessionToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
