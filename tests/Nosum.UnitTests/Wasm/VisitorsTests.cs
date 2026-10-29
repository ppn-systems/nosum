// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Visit;
using Nosum.Contracts.Visit.C2S;
using Nosum.Contracts.Visit.S2C;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.Socket.Visit;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;
using Nosum.Wasm.ViewModels.Visit;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class VisitorsTests
{
    private static readonly Guid s_lan = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid s_guest = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static VisitorsSnapshot Snapshot()
    {
        VisitorsSnapshot snapshot = new() { CoopMembersMayVisit = true, IsGuest = false };
        snapshot.Visitors.Add(new VisitorRow { PlayerId = s_lan, Nickname = "Lan Anh", Listed = true, Present = true });
        snapshot.Visitors.Add(new VisitorRow { PlayerId = s_guest, Nickname = "guest-0042", Listed = false, Present = false });
        return snapshot;
    }

    [Fact]
    public void Load_StoresTheSnapshot()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        VisitChannel channel = new(socket);
        int changed = 0;
        channel.Changed += () => changed++;

        channel.Load();

        changed.Should().Be(1);
        channel.Current.CoopMembersMayVisit.Should().BeTrue();
        channel.Current.IsGuest.Should().BeFalse();
        channel.Current.Visitors.Should().Equal(new VisitorInfo(s_lan, "Lan Anh", true, true), new VisitorInfo(s_guest, "guest-0042", false, false));
    }

    [Fact]
    public void Add_SendsTheTypedNicknameAndReloadsTheList()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        socket.ReplyWith(_ => new VisitResult { Status = VisitStatus.Ok });
        VisitChannel channel = new(socket);
        List<VisitStatus> answers = [];
        channel.Completed += answers.Add;

        channel.Add("Lan Anh");

        socket.Requests.OfType<AddVisitor>().Should().ContainSingle();
        answers.Should().Equal(VisitStatus.Ok);
        channel.Current.Visitors.Should().HaveCount(2, "the list is read again after an edit");
    }

    [Fact]
    public void Visit_Accepted_RaisesVisiting_Refused_DoesNot()
    {
        FakeGameSocket socket = new();
        VisitStatus reply = VisitStatus.Ok;
        socket.ReplyWith(_ => new VisitResult { Status = reply });
        VisitChannel channel = new(socket);
        int visiting = 0;
        List<VisitStatus> answers = [];
        channel.Visiting += () => visiting++;
        channel.Completed += answers.Add;

        channel.Visit("Minh");
        reply = VisitStatus.NotAllowed;
        channel.Visit("Minh");

        visiting.Should().Be(1);
        answers.Should().Equal(VisitStatus.Ok, VisitStatus.NotAllowed);
        socket.Requests.OfType<VisitPlayer>().Should().HaveCount(2);
    }

    [Theory]
    [InlineData("Lan Anh", "Lan Anh")]
    [InlineData("  Lan Anh ", "Lan Anh")]
    [InlineData("Khách 0042", "guest-0042")]
    [InlineData("khach 42", "guest-42")]
    [InlineData("Guest 0042", "guest-0042")]
    [InlineData("Guest 4x", "Guest 4x")]
    [InlineData("guest-0042", "guest-0042")]
    [InlineData("", "")]
    public void WireNickname_ReadsTheGuestNamesPlayersSee(string typed, string expected)
    {
        VisitorsViewModel.WireNickname(typed).Should().Be(expected);
    }

    [Fact]
    public void Visitors_ShowGuestsWithTheirLocalizedName_AndTheFlagsOfTheRow()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        VisitChannel channel = new(socket);
        channel.Load();
        using VisitorsViewModel vm = Create(socket, channel);

        IReadOnlyList<VisitorDisplay> rows = vm.Visitors;

        rows.Should().HaveCount(2);
        rows[0].Should().BeEquivalentTo(new { PlayerId = s_lan, Label = "Lan Anh", Listed = true, Present = true });
        rows[1].Label.Should().Contain("0042").And.NotContain("guest-");
        vm.CoopMembersMayVisit.Should().BeTrue();
    }

    [Fact]
    public void ToggleCoopVisits_SendsTheOppositeOfTheCurrentSetting()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        socket.ReplyWith(_ => new VisitResult { Status = VisitStatus.Ok });
        VisitChannel channel = new(socket);
        channel.Load();
        using VisitorsViewModel vm = Create(socket, channel);

        vm.ToggleCoopVisits();

        socket.Requests.OfType<SetCoopVisits>().Should().ContainSingle().Which.Allowed.Should().BeFalse();
    }

    [Fact]
    public void Refusal_ShowsTheLocalizedText_AndAcceptedVisitRaisesEntered()
    {
        FakeGameSocket socket = new();
        VisitStatus reply = VisitStatus.NotAllowed;
        socket.ReplyWith(_ => new VisitResult { Status = reply });
        VisitChannel channel = new(socket);
        using VisitorsViewModel vm = Create(socket, channel);
        int entered = 0;
        vm.Entered += () => entered++;
        vm.VisitText = "Minh";

        vm.Visit();

        vm.Status.Should().NotBeNullOrEmpty().And.NotContain("Visit_Status_");
        entered.Should().Be(0);

        reply = VisitStatus.Ok;
        vm.Visit();

        entered.Should().Be(1);
        vm.Status.Should().BeEmpty();
    }

    [Fact]
    public void Language_EveryVisitCodeAndTheNewRefusalsHaveATextInBothLanguages()
    {
        List<string> needed = [];
        needed.AddRange(Enum.GetValues<VisitStatus>().Where(s => s != VisitStatus.Ok).Select(s => "Visit_Status_" + s));
        needed.AddRange(["Farm_Status_NotYourFarm", "Place_Status_NotYourPlace", "Market_Status_NotYourBin", "Toolbar_Visitors", "Visit_Title"]);

        Keys("Language.resx").Should().Contain(needed);
        Keys("Language.en.resx").Should().Contain(needed);
        Keys("Language.resx").Should().BeEquivalentTo(Keys("Language.en.resx"));
    }

    [SuppressMessage("Reliability", "CA2000", Justification = "The look channel only subscribes to the fake socket, which dies with the test.")]
    private static GuestbookSnapshot GuestbookReply()
    {
        GuestbookSnapshot snapshot = new() { UnreadCount = 2 };
        snapshot.Rows.Add(new GuestbookRow { VisitorId = s_lan, Nickname = "Lan Anh", DayNumber = 12, Waters = 7, Unread = true });
        snapshot.Rows.Add(new GuestbookRow { VisitorId = s_guest, Nickname = "guest-0042", DayNumber = 11, Waters = 2, Unread = false });
        return snapshot;
    }

    [Fact]
    public void LoadGuestbook_StoresTheRowsAndHowManyWereNew()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => GuestbookReply());
        VisitChannel channel = new(socket);
        int changed = 0;
        channel.Changed += () => changed++;

        channel.LoadGuestbook();

        changed.Should().Be(1);
        channel.Guestbook.UnreadCount.Should().Be(2);
        channel.Guestbook.Entries.Should().Equal(new GuestbookEntryInfo(s_lan, "Lan Anh", 12, 7, true), new GuestbookEntryInfo(s_guest, "guest-0042", 11, 2, false));
    }

    [Fact]
    public void Guestbook_ShowsEachVisitorsNameAndWaters()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => GuestbookReply());
        VisitChannel channel = new(socket);
        channel.LoadGuestbook();
        using VisitorsViewModel vm = Create(socket, channel);

        vm.GuestbookUnread.Should().Be(2);
        vm.Guestbook.Should().HaveCount(2);
        vm.Guestbook[0].Should().BeEquivalentTo(new GuestbookDisplay("Lan Anh", 12, 7, true));
        vm.Guestbook[1].Visitor.Should().Contain("0042").And.NotContain("guest-");
    }

    [SuppressMessage("Reliability", "CA2000", Justification = "The look channel only listens to the fake socket; nothing to dispose.")]
    private static VisitorsViewModel Create(FakeGameSocket socket, VisitChannel channel)
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        StringLocalizer<Language> text = new(factory);
        NicknameFormatter names = new(text, new LookChannel(socket, () => 0));
        return new VisitorsViewModel(channel, names, text);
    }

    private static HashSet<string> Keys(string file, [CallerFilePath] string here = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));
        string path = Path.Combine(root, "wasm", "Nosum.Wasm.ViewModels", "Resources", file);
        return XDocument.Load(path).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToHashSet();
    }
}
