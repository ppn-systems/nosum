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

[Collection(SharedUiCulture.Name)]
public sealed class QuestTrackerViewModelTests
{
    [Fact]
    public void PushedCompletion_OfAMidChainQuest_ShowsItsBannerWithTheReward()
    {
        // The chain's next quest is already active when the till completes: the banner must still name the quest that finished (critique item 11).
        using CultureScope scope = new("vi");
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Rows(
            new QuestRow { QuestId = QuestCatalog.TillSoil, State = QuestState.Active, Progress = [0] },
            new QuestRow { QuestId = QuestCatalog.WaterCrop, State = QuestState.Active, Progress = [0] }));
        using QuestChannel channel = new(socket);
        using QuestTrackerViewModel vm = new(channel, Localizer<Catalogue>(), Localizer<Language>());
        channel.Load();

        using QuestUpdate update = CompletionOf(QuestCatalog.TillSoil, 1);
        socket.Push(update);

        vm.IsCompleted.Should().BeTrue();
        vm.IsVisible.Should().BeTrue();
        vm.CompletedText.Should().StartWith("Hoàn thành nhiệm vụ: ").And.EndWith("Bạn nhận được 10 xu.");
    }

    [Fact]
    public void Login_WithAFinishedQuest_ShowsNoBanner()
    {
        // Only a completion that happens while the player watches gets a banner; the list at login does not replay old ones.
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Rows(
            new QuestRow { QuestId = QuestCatalog.TillSoil, State = QuestState.Completed, Progress = [1] },
            new QuestRow { QuestId = QuestCatalog.WaterCrop, State = QuestState.Active, Progress = [0] }));
        using QuestChannel channel = new(socket);
        using QuestTrackerViewModel vm = new(channel, Localizer<Catalogue>(), Localizer<Language>());

        channel.Load();

        vm.IsCompleted.Should().BeFalse();
        vm.CompletedText.Should().BeEmpty();
    }

    private static QuestLog Rows(params QuestRow[] rows)
    {
        QuestLog log = new();
        log.Quests.AddRange(rows);
        return log;
    }

    private static QuestUpdate CompletionOf(int questId, params int[] progress)
    {
        QuestUpdate update = new();
        update.Quest.QuestId = questId;
        update.Quest.State = QuestState.Completed;
        update.Quest.Progress = [.. progress];
        return update;
    }

    private static QuestLog Log(QuestState state, params int[] progress)
    {
        QuestLog log = new();
        log.Quests.Add(new QuestRow { QuestId = QuestCatalog.FirstHarvest, State = state, Progress = [.. progress] });
        return log;
    }

    [Fact]
    public void Fold_StartsOpenUnlessAskedToStartFolded_AndTheHeaderToggles()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        using QuestChannel channel = new(socket);
        using QuestTrackerViewModel vm = new(channel, Localizer<Catalogue>(), Localizer<Language>());
        vm.Folded.Should().BeFalse("the card is open on a desktop");

        vm.StartFolded(true);
        vm.Folded.Should().BeTrue("a phone starts with the title line only");

        vm.ToggleFolded();
        vm.Folded.Should().BeFalse();
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
    public void Tip_ActiveQuest_IsTheOneLineOnWhatToDoAndWhere()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Log(QuestState.Active, 0, 0));
        using QuestChannel channel = new(socket);
        using QuestTrackerViewModel vm = new(channel, Localizer<Catalogue>(), Localizer<Language>());

        channel.Load();

        vm.Tip.Should().Be("Plant a seed on tilled soil, water it, wait for it to grow and harvest it.");
    }

    [Fact]
    public void Tip_CompletedQuest_IsEmpty()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Log(QuestState.Completed, 1, 1));
        using QuestChannel channel = new(socket);
        using QuestTrackerViewModel vm = new(channel, Localizer<Catalogue>(), Localizer<Language>());

        channel.Load();

        vm.IsCompleted.Should().BeTrue();
        vm.Tip.Should().BeEmpty();
    }

    [Fact]
    public void Tip_EveryQuest_HasTextInBothLanguages()
    {
        foreach (QuestDefinition quest in QuestCatalog.All)
        {
            foreach (string culture in new[] { "vi", "en" })
            {
                using CultureScope scope = new(culture);
                LocalizedString tip = Localizer<Catalogue>()["Quest_" + quest.Key + "_Tip"];
                tip.ResourceNotFound.Should().BeFalse($"quest {quest.Key} needs a tip in {culture}");
            }
        }
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
        vm.CompletedText.Should().Be("Hoàn thành nhiệm vụ: Vụ thu hoạch đầu tiên. Bạn nhận được 40 xu.");
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
        AccountSession guest = new(Guid.NewGuid(), string.Empty, "t", DateTimeOffset.UnixEpoch, GameLanguage.English, true, false, "guest-0002");
        NoAccountClient client = new(guest);
        AccountFormViewModel form = new(
            client,
            new FakeExternalSignIn(),
            ExternalSignInOptions.None,
            Localizer<Language>(),
            new RememberedSessionViewModel(client, new MemoryRememberedSessionStore(), Localizer<Language>(), TimeProvider.System));
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

        public System.Threading.Tasks.Task<AccountReply> SignInWithExternalAsync(ExternalProvider provider, string idToken, GameLanguage language, System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();

        public System.Threading.Tasks.Task<AccountReply> LinkExternalIdentityAsync(ExternalProvider provider, string idToken, string nickname, System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();

        public System.Threading.Tasks.Task<AccountReply> StartGuestAsync(GameLanguage language, System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.FromResult(new AccountReply(AccountStatus.Ok, _guest));

        public System.Threading.Tasks.Task<NicknameReply> SetNicknameAsync(string nickname, System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();

        public System.Threading.Tasks.Task<AccountStatus> SetLanguageAsync(GameLanguage language, System.Threading.CancellationToken cancellationToken) => throw new NotSupportedException();

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
