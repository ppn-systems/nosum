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
using Nosum.Wasm.ViewModels.Account;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[Collection(SharedUiCulture.Name)]
public sealed class PasswordRecoveryViewModelTests
{
    [Fact]
    public async Task SubmitAsync_ValidEmail_AsksForACodeAndMovesToTheCodeStep()
    {
        using CultureScope scope = new("en");
        FakeRecovery client = new();
        PasswordRecoveryViewModel vm = Create(client);
        vm.Begin("farmer01@gmail.com");

        await vm.SubmitAsync(CancellationToken.None);

        client.Calls.Should().Equal("request:farmer01@gmail.com");
        vm.Step.Should().Be(RecoveryStep.Confirm);
        vm.InfoText.Should().Contain("a code was sent");
        vm.ErrorText.Should().BeNull();
    }

    [Fact]
    public async Task SubmitAsync_NotAGmailAddress_IsRefusedLocally()
    {
        FakeRecovery client = new();
        PasswordRecoveryViewModel vm = Create(client);
        vm.Begin("farmer01@yahoo.com");

        await vm.SubmitAsync(CancellationToken.None);

        client.Calls.Should().BeEmpty();
        vm.Step.Should().Be(RecoveryStep.Request);
        vm.ErrorText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SubmitAsync_CodeAndNewPassword_ChangeThePasswordAndFinishWithAcknowledgement()
    {
        using CultureScope scope = new("en");
        FakeRecovery client = new();
        PasswordRecoveryViewModel vm = Create(client);
        vm.Begin("farmer01@gmail.com");
        await vm.SubmitAsync(CancellationToken.None);
        vm.Code = " 12345678 ";
        vm.NewPassword = "new-password-2";
        vm.ConfirmPassword = "new-password-2";
        bool closed = false;
        vm.Closed += () => closed = true;

        await vm.SubmitAsync(CancellationToken.None);

        client.Calls.Should().Equal("request:farmer01@gmail.com", "confirm:farmer01@gmail.com:12345678:new-password-2");
        vm.Step.Should().Be(RecoveryStep.Done);
        vm.NewPassword.Should().BeEmpty();
        vm.ConfirmPassword.Should().BeEmpty();
        closed.Should().BeFalse();

        await vm.SubmitAsync(CancellationToken.None);
        closed.Should().BeTrue();
    }

    [Theory]
    [InlineData("1234567", "new-password-2", "new-password-2")]
    [InlineData("1234567a", "new-password-2", "new-password-2")]
    [InlineData("12345678", "short", "short")]
    [InlineData("12345678", "new-password-2", "different-3")]
    public async Task SubmitAsync_BadCodeOrPassword_IsRefusedLocallyWithoutCallingTheServer(string code, string password, string confirm)
    {
        FakeRecovery client = new();
        PasswordRecoveryViewModel vm = Create(client);
        vm.Begin("farmer01@gmail.com");
        await vm.SubmitAsync(CancellationToken.None);
        vm.Code = code;
        vm.NewPassword = password;
        vm.ConfirmPassword = confirm;

        await vm.SubmitAsync(CancellationToken.None);

        client.Calls.Should().Equal("request:farmer01@gmail.com");
        vm.Step.Should().Be(RecoveryStep.Confirm);
        vm.ErrorText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SubmitAsync_ServerRefusesTheCode_ShowsTheLocalizedStatusAndClearsThePasswords()
    {
        using CultureScope scope = new("en");
        FakeRecovery client = new() { Confirm = AccountStatus.InvalidResetCode };
        PasswordRecoveryViewModel vm = Create(client);
        vm.Begin("farmer01@gmail.com");
        await vm.SubmitAsync(CancellationToken.None);
        vm.Code = "12345678";
        vm.NewPassword = "new-password-2";
        vm.ConfirmPassword = "new-password-2";

        await vm.SubmitAsync(CancellationToken.None);

        vm.Step.Should().Be(RecoveryStep.Confirm);
        vm.ErrorText.Should().Contain("recovery code is wrong");
        vm.NewPassword.Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAsync_TooManyRequests_ShowsTheThrottleText_AndNoConnectionShowsTheConnectionText()
    {
        using CultureScope scope = new("en");
        FakeRecovery client = new() { Request = AccountStatus.TooManyRequests };
        PasswordRecoveryViewModel vm = Create(client);
        vm.Begin("farmer01@gmail.com");

        await vm.SubmitAsync(CancellationToken.None);
        vm.ErrorText.Should().Contain("Too many requests");

        client.Throw = true;
        await vm.SubmitAsync(CancellationToken.None);
        vm.ErrorText.Should().NotContain("Too many requests").And.NotBeNullOrEmpty();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void Cancel_RaisesClosed_AndBeginForgetsTheEarlierVisit()
    {
        PasswordRecoveryViewModel vm = Create(new FakeRecovery());
        bool closed = false;
        vm.Closed += () => closed = true;
        vm.Code = "99999999";

        vm.Cancel();
        vm.Begin("farmer01@gmail.com");

        closed.Should().BeTrue();
        vm.Code.Should().BeEmpty();
        vm.Step.Should().Be(RecoveryStep.Request);
        vm.Email.Should().Be("farmer01@gmail.com");
    }

    private static PasswordRecoveryViewModel Create(IRecoveryClient client)
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new PasswordRecoveryViewModel(client, new StringLocalizer<Language>(factory));
    }

    private sealed class FakeRecovery : IRecoveryClient
    {
        public AccountStatus Request { get; set; } = AccountStatus.Ok;

        public AccountStatus Confirm { get; set; } = AccountStatus.Ok;

        public bool Throw { get; set; }

        public List<string> Calls { get; } = [];

        public Task<AccountStatus> RequestResetAsync(string email, CancellationToken cancellationToken)
        {
            this.Calls.Add("request:" + email);
            return this.Throw ? throw new AccountConnectionException("down") : Task.FromResult(this.Request);
        }

        public Task<AccountStatus> ConfirmResetAsync(string email, string code, string newPassword, CancellationToken cancellationToken)
        {
            this.Calls.Add($"confirm:{email}:{code}:{newPassword}");
            return this.Throw ? throw new AccountConnectionException("down") : Task.FromResult(this.Confirm);
        }

        public Task<AccountStatus> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
