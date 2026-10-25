// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Common;
using Nosum.Contracts.Quest;
using Nosum.Contracts.Quest.S2C;
using Nosum.Gameplay.Quests;
using Nosum.Wasm.Socket.Quest;
using Nosum.Wasm.ViewModels.Account;
using Nosum.Wasm.ViewModels.Quest;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class QuestTrackerViewModelTests
{
    private static QuestLog Log(QuestState state, params int[] progress)
    {
        QuestLog log = new();
        log.Quests.Add(new QuestRow { QuestId = QuestCatalog.FirstHarvest, State = state, Progress = [.. progress] });
        return log;
    }

    [Fact]
    public void Objectives_ActiveQuest_ShowLocalizedTextAndCounts()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Log(QuestState.Active, 1, 0));
        using QuestChannel channel = new(socket);
        using QuestTrackerViewModel vm = new(channel, Localizer<Catalogue>(), Localizer<Language>());

        channel.Load();

        vm.IsVisible.Should().BeTrue();
        vm.IsCompleted.Should().BeFalse();
        vm.Title.Should().Be("Your first harvest");
        vm.Objectives.Should().Equal(new ObjectiveLine("Plant a seed", "1/1", true), new ObjectiveLine("Harvest a crop", "0/1", false));
        vm.Hint.Should().NotBeEmpty();
    }

    [Fact]
    public void PushedUpdate_Completion_ShowsTheBannerInVietnamese()
    {
        using CultureScope scope = new("vi");
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Log(QuestState.Active, 1, 0));
        using QuestChannel channel = new(socket);
        using QuestTrackerViewModel vm = new(channel, Localizer<Catalogue>(), Localizer<Language>());
        channel.Load();
        int changes = 0;
        vm.Changed += () => changes++;
        QuestUpdate update = new();
        update.Quest.QuestId = QuestCatalog.FirstHarvest;
        update.Quest.State = QuestState.Completed;
        update.Quest.Progress = [1, 1];

        socket.Push(update);

        vm.IsCompleted.Should().BeTrue();
        vm.CompletedText.Should().Be("Hoàn thành nhiệm vụ: Vụ thu hoạch đầu tiên");
        vm.Hint.Should().BeEmpty();
        changes.Should().Be(1);
        update.Dispose();
    }

    [Fact]
    public void IsVisible_BeforeTheLogArrives_IsFalse()
    {
        FakeGameSocket socket = new();
        using QuestChannel channel = new(socket);
        using QuestTrackerViewModel vm = new(channel, Localizer<Catalogue>(), Localizer<Language>());

        vm.IsVisible.Should().BeFalse();
    }

    [Fact]
    public async System.Threading.Tasks.Task UnlockRegistrationPrompt_ForAGuest_ShowsTheUpgradeOffer()
    {
        AccountSession guest = new(Guid.NewGuid(), string.Empty, "t", DateTimeOffset.UnixEpoch, GameLanguage.English, true, false);
        AccountFormViewModel form = new(new NoAccountClient(guest), Localizer<Language>());
        await form.PlayAsGuestAsync(System.Threading.CancellationToken.None);
        int changes = 0;
        form.Changed += () => changes++;
        form.CanUpgrade.Should().BeFalse();

        form.UnlockRegistrationPrompt();

        form.CanUpgrade.Should().BeTrue();
        changes.Should().Be(1);
    }

    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    private sealed class NoAccountClient : IAccountClient
    {
        private readonly AccountSession _guest;

        public NoAccountClient(AccountSession guest) => _guest = guest;

        public System.Threading.Tasks.Task<AccountReply> RegisterAsync(string email, string password, GameLanguage language, System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();

        public System.Threading.Tasks.Task<AccountReply> LoginAsync(string email, string password, System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();

        public System.Threading.Tasks.Task<AccountReply> StartGuestAsync(GameLanguage language, System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.FromResult(new AccountReply(AccountStatus.Ok, _guest));

        public System.Threading.Tasks.Task<AccountReply> UpgradeGuestAsync(string email, string password, System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();

        public System.Threading.Tasks.Task LogoutAsync(System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();

        public System.Threading.Tasks.Task<AccountReply> ResumeAsync(string sessionToken, System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
