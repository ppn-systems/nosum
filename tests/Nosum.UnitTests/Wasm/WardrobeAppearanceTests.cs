// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Look;
using Nosum.Contracts.Look.C2S;
using Nosum.Contracts.Look.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Looks;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// The wardrobe sends a whole appearance (skin, hair style, dye) with each request, so the requests must go one at a time and each must carry the newest
/// choice. Two quick choices must not revert each other. Every look change (appearance, wear, take-off) goes out one at a time, in the order chosen, and
/// the wardrobe shows each one while it waits (docs/adr/ADR-120).
/// </summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures over a fake socket that dies with the test.")]
public sealed class WardrobeAppearanceTests
{
    [Fact]
    public async Task SetAppearance_WhileAnAnswerIsPending_SendsTheNextChoiceOnlyAfterIt()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new LookResult { Status = LookStatus.Ok });
        socket.HoldReplies<LookResult>();
        using LookChannel looks = new(socket, () => 7);
        List<(int Skin, int Hair, int Dye)> sent = Record(socket);

        looks.SetAppearance(2, 1, 1);
        looks.SetAppearance(2, 3, 1);

        sent.Should().Equal(new[] { (2, 1, 1) }, "the second choice waits for the first answer");

        socket.ReleaseOldestHeld().Should().BeTrue();
        await Until(() => sent.Count == 2);

        sent.Should().Equal(new[] { (2, 1, 1), (2, 3, 1) });
        looks.Pending.Should().ContainSingle("the second choice is still waiting for its answer");

        socket.ReleaseOldestHeld().Should().BeTrue();
        await Until(() => looks.Pending.Count == 0);

        socket.ReleaseOldestHeld().Should().BeFalse("no third request was sent");
        sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task SetAppearance_ManyChoicesWhileWaiting_SendsOnlyTheNewestOfThem()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new LookResult { Status = LookStatus.Ok });
        socket.HoldReplies<LookResult>();
        using LookChannel looks = new(socket, () => 7);
        List<(int Skin, int Hair, int Dye)> sent = Record(socket);

        looks.SetAppearance(2, 1, 1);
        looks.SetAppearance(3, 1, 1);
        looks.SetAppearance(3, 5, 2);
        looks.SetAppearance(4, 5, 2);

        socket.ReleaseOldestHeld();
        await Until(() => sent.Count == 2);

        sent.Should().Equal(new[] { (2, 1, 1), (4, 5, 2) });
    }

    [Fact]
    public async Task Wardrobe_SkinThenHairBeforeAnAnswer_KeepsTheSkin()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new LookResult { Status = LookStatus.Ok });
        socket.HoldReplies<LookResult>();
        using FakeWardrobe host = new(socket);
        List<(int Skin, int Hair, int Dye)> sent = Record(socket);

        host.Wardrobe.SetSkin(2);
        host.Wardrobe.SetHairStyle(3);

        host.Wardrobe.Look.SkinId.Should().Be(2, "the chosen skin shows at once, before the server answers");
        host.Wardrobe.Look.HairStyleId.Should().Be(3);

        socket.ReleaseOldestHeld();
        await Until(() => sent.Count == 2);
        sent.Last().Should().Be((2, 3, 1), "the hair request carries the skin chosen before it");

        socket.ReleaseOldestHeld();
        await Until(() => host.Wardrobe.Look.SkinId == 1 && host.Wardrobe.Look.HairStyleId == 1);
        host.Wardrobe.Look.SkinId.Should().Be(1, "no look has arrived from the server, so the default shows again");
    }

    [Fact]
    public async Task Wardrobe_ARefusedChoice_ShowsTheServersLookAgain()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new LookResult { Status = LookStatus.InvalidSkin });
        socket.HoldReplies<LookResult>();
        using FakeWardrobe host = new(socket);

        host.Wardrobe.SetSkin(4);
        host.Wardrobe.Look.SkinId.Should().Be(4);

        socket.ReleaseOldestHeld();
        await Until(() => host.Wardrobe.Look.SkinId == 1);

        host.Wardrobe.Look.SkinId.Should().Be(1, "the server refused the change, so the look it sent stays");
    }

    [Fact]
    public async Task Unequip_AfterAnEquip_IsSentOnlyAfterTheEquipIsAnswered()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new LookResult { Status = LookStatus.Ok });
        socket.HoldReplies<LookResult>();
        using LookChannel looks = new(socket, () => 7);

        looks.Equip(5, 1001, 1);
        looks.Unequip(1);

        Kinds(socket).Should().Equal(new[] { typeof(EquipItem) }, "the take-off must not overtake the wear it follows");

        socket.ReleaseOldestHeld().Should().BeTrue();
        await Until(() => Kinds(socket).Count == 2);

        Kinds(socket).Should().Equal(new[] { typeof(EquipItem), typeof(UnequipItem) }, "the answer to the wear released the take-off");
    }

    [Fact]
    public async Task SetAppearance_AfterAWearWhileWaiting_IsSentAfterIt_AndNewerChoicesReplaceIt()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new LookResult { Status = LookStatus.Ok });
        socket.HoldReplies<LookResult>();
        using LookChannel looks = new(socket, () => 7);
        List<(int Skin, int Hair, int Dye)> sent = Record(socket);

        looks.SetAppearance(2, 1, 1);
        looks.Equip(5, 1001, 1);
        looks.SetAppearance(3, 1, 1);
        looks.SetAppearance(4, 1, 1);

        looks.Pending.Should().HaveCount(3, "the skin in flight, the wear, and the newest skin choice");

        socket.ReleaseOldestHeld();
        await Until(() => Kinds(socket).Count == 2);
        socket.ReleaseOldestHeld();
        await Until(() => Kinds(socket).Count == 3);
        socket.ReleaseOldestHeld();
        await Until(() => looks.Pending.Count == 0);

        Kinds(socket).Should().Equal(new[] { typeof(SetAppearance), typeof(EquipItem), typeof(SetAppearance) });
        sent.Should().Equal(new[] { (2, 1, 1), (4, 1, 1) }, "the skin that waited behind the wear is the newest one");
    }

    [Fact]
    public void Apply_WearThenTakeOff_ShowsEachChoiceInTurn()
    {
        LookInfo worn = new PendingWear(5, 1, 1001).Apply(LookInfo.Default);
        LookInfo bare = new PendingTakeOff(1).Apply(worn);

        worn.ShirtItemId.Should().Be(1001, "the shirt shows at once, before the server answers");
        bare.ShirtItemId.Should().Be(0);
    }

    // The types of the requests in the order they were sent.
    private static List<Type> Kinds(FakeGameSocket socket) => socket.Requests.Select(request => request.GetType()).ToList();

    // The appearance of every request as it was sent (read while the pooled packet is alive).
    private static List<(int Skin, int Hair, int Dye)> Record(FakeGameSocket socket)
    {
        List<(int Skin, int Hair, int Dye)> sent = [];
        socket.RequestSeen = request =>
        {
            if (request is SetAppearance set)
            {
                sent.Add(((int)set.SkinId, (int)set.HairStyleId, (int)set.HairDyeId));
            }
        };
        return sent;
    }

    // A released answer continues the sender on the thread pool, so the next request follows a moment later.
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 2000 && !condition(); i++)
        {
            await Task.Delay(1);
        }

        condition().Should().BeTrue("the sender should have moved on after the answer");
    }

    private sealed class FakeWardrobe : IDisposable
    {
        private readonly LookChannel _looks;
        private readonly InventoryChannel _channel;
        private readonly InventoryViewModel _inventory;

        public FakeWardrobe(FakeGameSocket socket)
        {
            _looks = new LookChannel(socket, () => 1);
            _channel = new InventoryChannel(socket);
            ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
            StringLocalizer<Language> text = new(factory);
            StringLocalizer<Catalogue> catalogue = new(factory);
            _inventory = new InventoryViewModel(_channel, new NoIcons(), catalogue, text);
            Wardrobe = new WardrobeViewModel(_looks, _inventory, new ItemDescriber(new NoIcons(), catalogue, text), text);
        }

        public WardrobeViewModel Wardrobe { get; }

        public void Dispose()
        {
            Wardrobe.Dispose();
            _inventory.Dispose();
            _channel.Dispose();
            _looks.Dispose();
        }

        private sealed class NoIcons : IItemIconSource
        {
            public bool TryGet(int itemId, out ItemIcon icon)
            {
                icon = default!;
                return false;
            }
        }
    }
}
