// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Mail;
using Nosum.Contracts.Mail.C2S;
using Nosum.Contracts.Mail.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Mail;
using Nosum.Wasm.ViewModels.Mail;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[SuppressMessage("Reliability", "CA2000", Justification = "Packets and channels here are plain objects over a fake socket that dies with the test; nothing is rented from a pool.")]
public sealed class MailTests
{
    private const long Now = 1_800_000_000_000;
    private const long Day = 24L * 60 * 60 * 1000;

    private static readonly Guid s_sold = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid s_invite = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid s_odd = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private static MailRow Row(Guid id, string code, long expiresIn, bool read = false, long coins = 0, params string[] args)
    {
        MailRow row = new() { Id = id, Code = code, SentAtMs = Now - Day, ExpiresAtMs = Now + expiresIn, Read = read, Coins = coins };
        row.Args.AddRange(args.Select(a => new MailArgRow { Value = a }));
        return row;
    }

    private static MailList Box(int unread)
    {
        MailList list = new() { Unread = unread };
        MailRow sold = Row(s_sold, "market.sold", 29 * Day, false, 95, ItemCatalog.Parsnip.ToString(System.Globalization.CultureInfo.InvariantCulture), "5", "95");
        sold.Items.Add(new MailItemRow { ItemId = ItemCatalog.Parsnip, Quantity = 2 });
        list.Mails.Add(sold);
        list.Mails.Add(Row(s_invite, "coop.invite", (3 * Day) - 1000, true, 0, "i18n{\"vi\":\"Vườn Xanh\",\"en\":\"Green Garden\"}", "Lan Anh"));
        list.Mails.Add(Row(s_odd, "something.new", 1000, true));
        return list;
    }

    [Fact]
    public void Load_SetsTheUnreadCountAndTheMails()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Box(1));
        using MailChannel channel = new(socket);

        channel.Load();

        channel.Unread.Should().Be(1);
        channel.Mails.Select(m => m.Id).Should().Equal(s_sold, s_invite, s_odd);
        channel.Mails[0].Items.Should().Equal(new MailItemInfo(ItemCatalog.Parsnip, 2));
        channel.Mails[0].Args.Should().Equal("200", "5", "95");
    }

    [Fact]
    public void UnreadPush_BeforeThePageIsEverOpened_UpdatesTheBadge()
    {
        FakeGameSocket socket = new();
        using MailChannel channel = new(socket);
        using MailViewModel vm = Create(socket, channel);
        int changed = 0;
        vm.Changed += () => changed++;

        socket.Push(new MailUnread { Count = 3 });
        int afterFirst = vm.Unread;
        socket.Push(new MailUnread { Count = 0 });

        afterFirst.Should().Be(3);
        vm.Unread.Should().Be(0);
        changed.Should().Be(2);
        socket.Requests.Should().BeEmpty("the closed page does not refetch the mailbox");
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(4, "4")]
    [InlineData(9, "9")]
    [InlineData(10, "9+")]
    [InlineData(250, "9+")]
    public void UnreadText_CapsTheBadge(int count, string expected)
    {
        FakeGameSocket socket = new();
        using MailChannel channel = new(socket);
        using MailViewModel vm = Create(socket, channel);

        socket.Push(new MailUnread { Count = count });

        vm.UnreadText.Should().Be(expected);
    }

    [Fact]
    public void UnreadPush_WhilePageOpen_ReadsTheMailboxAgain()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Box(1));
        using MailChannel channel = new(socket);
        using MailViewModel vm = Create(socket, channel);
        vm.Open();

        socket.Push(new MailUnread { Count = 2 });
        vm.Close();
        socket.Push(new MailUnread { Count = 3 });

        socket.Requests.OfType<GetMail>().Should().HaveCount(2, "one for opening and one for the push while open");
    }

    [Fact]
    public void Mails_MarketSold_RendersItemNameQuantityAndCoinsFromTheCode()
    {
        MailDisplay sold = Rendered().Single(m => m.Id == s_sold);

        string parsnip = SocialClientFixtures.Items().NameOf(ItemCatalog.Parsnip);
        sold.Body.Should().Contain(parsnip).And.Contain("5").And.Contain("95");
        sold.Title.Should().NotBeNullOrEmpty().And.NotStartWith("Mail_");
        sold.Coins.Should().Be(95);
        sold.Items.Should().ContainSingle();
        sold.CanCollect.Should().BeTrue();
        sold.CanDelete.Should().BeFalse("an uncollected attachment blocks deleting");
        sold.Read.Should().BeFalse();
    }

    [Fact]
    public void Mails_CoopInvite_ResolvesTheAuthoredCoopNameForTheLanguage()
    {
        using (new SocialClientFixtures.CultureScope("en"))
        {
            MailDisplay invite = Rendered().Single(m => m.Id == s_invite);

            invite.Body.Should().Contain("Green Garden").And.Contain("Lan Anh").And.NotContain("i18n");
        }

        using (new SocialClientFixtures.CultureScope("vi"))
        {
            Rendered().Single(m => m.Id == s_invite).Body.Should().Contain("Vườn Xanh").And.NotContain("i18n");
        }
    }

    [Fact]
    public void Describe_UnknownCode_GivesAGenericNoticeNotTheRawCode()
    {
        MailDisplay odd = Rendered().Single(m => m.Id == s_odd);

        odd.Title.Should().NotContain("something.new").And.NotStartWith("Mail_");
        odd.Body.Should().NotContain("something.new").And.NotStartWith("Mail_");
        odd.CanDelete.Should().BeTrue();
    }

    [Fact]
    public void Describe_GoalRewardAndBrokenMarketArguments_AreGenericNotices()
    {
        FakeGameSocket socket = new();
        using MailChannel channel = new(socket);
        using MailViewModel vm = Create(socket, channel);

        (string rewardTitle, string rewardBody) = vm.Describe(new MailInfo(Guid.NewGuid(), "goal.reward", ["whatever"], Now, Now + Day, false, 0, [], false));
        (string brokenTitle, _) = vm.Describe(new MailInfo(Guid.NewGuid(), "market.sold", ["not-a-number"], Now, Now + Day, false, 0, [], false));
        (string unknownTitle, _) = vm.Describe(new MailInfo(Guid.NewGuid(), "x", [], Now, Now + Day, false, 0, [], false));

        rewardTitle.Should().NotStartWith("Mail_");
        rewardBody.Should().NotStartWith("Mail_").And.NotContain("whatever");
        brokenTitle.Should().Be(unknownTitle, "arguments that do not fit the code are shown as the generic notice");
    }

    [Fact]
    public void Expiry_ShowsDaysLeftRoundedUp_AndExpired()
    {
        IReadOnlyList<MailDisplay> rows = Rendered();

        using (new SocialClientFixtures.CultureScope("en"))
        {
            Rendered().Single(m => m.Id == s_sold).Expiry.Should().Be("29 days left");
            Rendered().Single(m => m.Id == s_invite).Expiry.Should().Be("3 days left");
            Rendered().Single(m => m.Id == s_odd).Expiry.Should().Be("1 day left");
        }

        rows.Should().HaveCount(3);
    }

    [Fact]
    public void Mails_AlreadyExpired_CannotBeCollectedButCanBeDeleted()
    {
        FakeGameSocket socket = new();
        MailList list = new();
        MailRow row = Row(s_sold, "market.sold", -1000, true, 10, "200", "1", "10");
        list.Mails.Add(row);
        socket.ReplyWith(_ => list);
        using MailChannel channel = new(socket);
        channel.Load();
        using MailViewModel vm = Create(socket, channel);

        MailDisplay mail = vm.Mails.Single();

        mail.CanCollect.Should().BeFalse();
        mail.CanDelete.Should().BeTrue();
        using (new SocialClientFixtures.CultureScope("en"))
        {
            vm.Mails.Single().Expiry.Should().Be("Expired");
        }
    }

    [Fact]
    public void Toggle_OpeningAnUnreadMail_MarksItReadAndTheUnreadPushLowersTheBadge()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Box(1));
        List<Guid> read = [];
        socket.ReplyWith(request =>
        {
            read.Add(((ReadMail)request).MailId);
            return new MailResult { Status = MailStatus.Ok };
        });
        using MailChannel channel = new(socket);
        channel.Load();
        using MailViewModel vm = Create(socket, channel);

        vm.Toggle(s_sold);
        socket.Push(new MailUnread { Count = 0 });

        read.Should().Equal(s_sold);
        vm.Selected.Should().Be(s_sold);
        vm.Mails.Single(m => m.Id == s_sold).Read.Should().BeTrue();
        vm.Unread.Should().Be(0);
    }

    [Fact]
    public void Toggle_OpeningAReadMail_SendsNothing_AndTogglingAgainCloses()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Box(1));
        using MailChannel channel = new(socket);
        channel.Load();
        using MailViewModel vm = Create(socket, channel);
        int requestsBefore = socket.Requests.Count;

        vm.Toggle(s_invite);
        Guid? opened = vm.Selected;
        vm.Toggle(s_invite);

        socket.Requests.Count.Should().Be(requestsBefore);
        opened.Should().Be(s_invite);
        vm.Selected.Should().BeNull();
    }

    [Fact]
    public void Collect_Accepted_SendsTheMailIdAndReadsTheBoxAgain()
    {
        FakeGameSocket socket = new();
        MailList after = Box(0);
        after.Mails[0].Items.Clear();
        after.Mails[0].Coins = 0;
        MailList next = Box(1);
        socket.ReplyWith(_ => next);
        List<Guid> collected = [];
        socket.ReplyWith(request =>
        {
            collected.Add(((CollectMail)request).MailId);
            next = after;
            return new MailResult { Status = MailStatus.Ok };
        });
        using MailChannel channel = new(socket);
        channel.Load();
        using MailViewModel vm = Create(socket, channel);

        vm.Collect(s_sold);

        collected.Should().Equal(s_sold);
        vm.Mails.Single(m => m.Id == s_sold).CanCollect.Should().BeFalse();
        vm.Status.Should().NotBeNullOrEmpty();
        vm.StatusIsGood.Should().BeTrue();
    }

    [Fact]
    public void Collect_NoSpace_ShowsTheLocalizedRefusal()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Box(1));
        socket.ReplyWith(_ => new MailResult { Status = MailStatus.NoSpace });
        using MailChannel channel = new(socket);
        using MailViewModel vm = Create(socket, channel);

        vm.Collect(s_sold);

        vm.Status.Should().Be(vm.StatusText(MailStatus.NoSpace)).And.NotStartWith("Mail_Status_");
        vm.StatusIsGood.Should().BeFalse();
    }

    [Fact]
    public void CollectAll_CollectsEveryMailWithAGiftThatIsNotExpired_AndNoOthers()
    {
        Guid second = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");
        Guid expired = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005");
        FakeGameSocket socket = new();
        MailList box = Box(0);
        box.Mails.Add(Row(second, "market.sold", 20 * Day, false, 40, ItemCatalog.Parsnip.ToString(System.Globalization.CultureInfo.InvariantCulture), "1", "40"));
        box.Mails.Add(Row(expired, "market.sold", -Day, false, 10, ItemCatalog.Parsnip.ToString(System.Globalization.CultureInfo.InvariantCulture), "1", "10"));
        socket.ReplyWith(_ => box);
        List<Guid> collected = [];
        socket.ReplyWith(request =>
        {
            collected.Add(((CollectMail)request).MailId);
            return new MailResult { Status = MailStatus.Ok };
        });
        using MailChannel channel = new(socket);
        channel.Load();
        using MailViewModel vm = Create(socket, channel);

        vm.CollectableCount.Should().Be(2);
        vm.CollectAll();

        collected.Should().Equal(s_sold, second);
        vm.StatusIsGood.Should().BeTrue();
    }

    [Fact]
    public void CollectAll_StopsAtTheFirstRefusal_AndShowsItsReason()
    {
        Guid second = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006");
        FakeGameSocket socket = new();
        MailList box = Box(0);
        box.Mails.Add(Row(second, "market.sold", 20 * Day, false, 40, ItemCatalog.Parsnip.ToString(System.Globalization.CultureInfo.InvariantCulture), "1", "40"));
        socket.ReplyWith(_ => box);
        List<Guid> collected = [];
        socket.ReplyWith(request =>
        {
            collected.Add(((CollectMail)request).MailId);
            return new MailResult { Status = MailStatus.NoSpace };
        });
        using MailChannel channel = new(socket);
        channel.Load();
        using MailViewModel vm = Create(socket, channel);

        vm.CollectAll();

        collected.Should().Equal(s_sold);
        vm.StatusIsGood.Should().BeFalse();
        vm.Status.Should().Be(vm.StatusText(MailStatus.NoSpace));
    }

    [Fact]
    public void CollectableCount_NoGifts_IsZeroSoTheButtonIsHidden()
    {
        FakeGameSocket socket = new();
        MailList box = new() { Unread = 0 };
        box.Mails.Add(Row(s_odd, "something.new", 1000, true));
        socket.ReplyWith(_ => box);
        using MailChannel channel = new(socket);
        channel.Load();
        using MailViewModel vm = Create(socket, channel);

        vm.CollectableCount.Should().Be(0);
    }

    [Fact]
    public void Language_EveryMailStatusAndCodeHasATextInBothLanguages()
    {
        List<string> needed = [];
        needed.AddRange(Enum.GetValues<MailStatus>().Select(s => "Mail_Status_" + s));
        needed.AddRange(["Mail_Title_MarketSold", "Mail_Body_MarketSold", "Mail_Title_CoopInvite", "Mail_Body_CoopInvite", "Mail_Body_CoopInviteFrom", "Mail_Title_GoalReward", "Mail_Body_GoalReward", "Mail_Title_Unknown", "Mail_Body_Unknown", "Mail_Expiry", "Mail_Expiry_One", "Mail_Expired", "Toolbar_Mail", "Toolbar_Friends"]);

        SocialClientFixtures.Keys("Language.resx").Should().Contain(needed);
        SocialClientFixtures.Keys("Language.en.resx").Should().Contain(needed);
    }

    [Fact]
    public void Language_ViAndEnCarryTheSameKeys()
    {
        SocialClientFixtures.Keys("Language.resx").Should().BeEquivalentTo(SocialClientFixtures.Keys("Language.en.resx"));
    }

    private static IReadOnlyList<MailDisplay> Rendered()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Box(1));
        MailChannel channel = new(socket);
        channel.Load();
        using MailViewModel vm = Create(socket, channel);
        return vm.Mails;
    }

    private static MailViewModel Create(FakeGameSocket socket, MailChannel channel)
        => new(channel, SocialClientFixtures.Items(), SocialClientFixtures.Names(socket), SocialClientFixtures.Localizer<Language>(), () => Now);
}
