// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Look;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Looks;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class LookChannelTests
{
    private static void PushLook(FakeGameSocket socket, uint playerId, int skin, string nickname, ushort shirtItemId = 0)
    {
        using LookChanged packet = new()
        {
            Look = new LookRow
            {
                PlayerId = playerId,
                SkinId = (byte)skin,
                Nickname = nickname,
                ShirtItemId = shirtItemId,
            },
        };
        socket.Push(packet);
    }

    [Theory]
    [InlineData(ItemCatalog.LeatherArmor, "#8b5a2b")]
    [InlineData(ItemCatalog.RangerTunic, "#92400e")]
    [InlineData(ItemCatalog.IronChestplate, "#94a3b8")]
    public void ArmorShirts_UseNeutralShirtFrameAndPropagateTintToPreview(int shirtItemId, string expectedCssTint)
    {
        ClothingSprite sprite = LookPalette.Shirt(shirtItemId);
        sprite.SheetIndex.Should().Be(LookPalette.PlainShirt.SheetIndex);
        LookPalette.Css(sprite.Tint).Should().Be(expectedCssTint);

        FakeGameSocket socket = new();
        using LookChannel looks = new(socket, () => 1);
        using InventoryChannel invChannel = new(socket);
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        StringLocalizer<Language> text = new(factory);
        StringLocalizer<Catalogue> catalogue = new(factory);
        ItemDescriber describer = new(new NoIcons(), catalogue, text);
        using InventoryViewModel inventory = new(invChannel, new NoIcons(), catalogue, text);
        using WardrobeViewModel wardrobe = new(looks, inventory, describer, text);

        PushLook(socket, 1, 1, "Mai", (ushort)shirtItemId);

        PreviewLayer shirtLayer = wardrobe.Preview.Single(layer => layer.SheetUrl.EndsWith("shirts.png", System.StringComparison.Ordinal));
        shirtLayer.Tint.Should().Be(expectedCssTint);
    }

    private sealed class NoIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = default!;
            return false;
        }
    }

    [Fact]
    public void PlayerLeft_ForgetsThatPlayersLookAndNickname_AndKeepsTheOthers()
    {
        FakeGameSocket socket = new();
        using LookChannel channel = new(socket, () => 1);
        PushLook(socket, 1, 2, "Mai");
        PushLook(socket, 7, 3, "Lan");
        int changed = 0;
        channel.Changed += () => changed++;
        PlayerLeft left = new() { PlayerId = 7 };

        socket.Push(left);

        channel.TryGet(7, out _).Should().BeFalse();
        channel.TryGetNickname(7, out _).Should().BeFalse();
        channel.TryGet(1, out LookInfo mine).Should().BeTrue();
        mine.Nickname.Should().Be("Mai");
        changed.Should().Be(1);
        left.Dispose();
    }

    [Fact]
    public void LookChanged_AfterAPlayerCameBackIntoSight_StoresTheLookAgain()
    {
        FakeGameSocket socket = new();
        using LookChannel channel = new(socket, () => 1);
        PushLook(socket, 7, 3, "Lan");
        PlayerLeft left = new() { PlayerId = 7 };
        socket.Push(left);

        PushLook(socket, 7, 4, "Lan");

        channel.TryGet(7, out LookInfo look).Should().BeTrue();
        look.SkinId.Should().Be(4);
        left.Dispose();
    }
}
