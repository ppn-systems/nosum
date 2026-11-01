// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The toast line above the hotbar: up to two stacked toasts, each three seconds (docs/design/ui-standard.md section 5).</summary>
[SuppressMessage("Reliability", "CA2000", Justification = "Short-lived test fixtures: the channels live as long as the test.")]
public sealed class GameMessagesViewModelTests
{
    [Fact]
    public void MessageDuration_IsThreeSeconds()
    {
        GameMessagesViewModel.MessageDuration.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Toasts_EmptyUntilSomethingIsShown()
    {
        GameMessagesViewModel vm = Create();

        vm.Toasts.Should().BeEmpty();
        vm.Message.Should().BeNull();
    }

    [Fact]
    public void Notify_TwoInARow_BothShowNewestLast()
    {
        GameMessagesViewModel vm = Create();

        vm.Notify("Till the soil first.");
        vm.Notify("Something is in the way.");

        vm.Toasts.Select(t => t.Text).Should().Equal("Till the soil first.", "Something is in the way.");
        vm.Message.Should().Be("Something is in the way.");
    }

    [Fact]
    public void Notify_ThirdWhileTwoShow_PushesOutTheOldest()
    {
        GameMessagesViewModel vm = Create();

        vm.Notify("one");
        vm.Notify("two");
        vm.Notify("three");

        vm.Toasts.Should().HaveCount(GameMessagesViewModel.MaxToasts);
        vm.Toasts.Select(t => t.Text).Should().Equal("two", "three");
    }

    [Fact]
    public void Notify_GivesEachToastItsOwnIncreasingId()
    {
        GameMessagesViewModel vm = Create();

        vm.Notify("one");
        vm.Notify("two");

        vm.Toasts[0].Id.Should().BeLessThan(vm.Toasts[1].Id);
    }

    [Fact]
    public void SetHoverHint_RaisesChangedOnlyWhenTheTextChanges_AndClears()
    {
        GameMessagesViewModel vm = Create();
        int raised = 0;
        vm.Changed += () => raised++;

        vm.SetHoverHint("Growth stage 2 of 4");
        vm.SetHoverHint("Growth stage 2 of 4");
        vm.HoverHint.Should().Be("Growth stage 2 of 4");
        raised.Should().Be(1, "the same hint is not a change");

        vm.SetHoverHint(null);
        vm.HoverHint.Should().BeNull();
        raised.Should().Be(2);
    }

    [Fact]
    public void Notify_RaisesChanged()
    {
        GameMessagesViewModel vm = Create();
        int raised = 0;
        vm.Changed += () => raised++;

        vm.Notify("Something is in the way.");

        raised.Should().Be(1);
    }

    [Fact]
    public void Notify_SameTextTwice_ShowsOneToastCountedTwice()
    {
        GameMessagesViewModel vm = Create();

        vm.Notify("Hãy xới đất trước.");
        vm.Notify("Hãy xới đất trước.");

        vm.Toasts.Should().HaveCount(1);
        vm.Toasts[0].Text.Should().Be("Hãy xới đất trước.");
        vm.Toasts[0].Count.Should().Be(2);
    }

    [Fact]
    public void Notify_SameTextAgain_KeepsTheToastIdAndItsPlace()
    {
        GameMessagesViewModel vm = Create();

        vm.Notify("one");
        vm.Notify("two");
        long idOfOne = vm.Toasts[0].Id;
        vm.Notify("one");

        vm.Toasts.Select(t => t.Text).Should().Equal("one", "two");
        vm.Toasts[0].Id.Should().Be(idOfOne);
        vm.Toasts[0].Count.Should().Be(2);
        vm.Toasts[1].Count.Should().Be(1);
    }

    [Fact]
    public void Notify_SameTextAgain_RaisesChangedAgain()
    {
        GameMessagesViewModel vm = Create();
        int raised = 0;
        vm.Changed += () => raised++;

        vm.Notify("Hãy xới đất trước.");
        vm.Notify("Hãy xới đất trước.");

        raised.Should().Be(2);
    }

    [Fact]
    public async Task Notify_SameTextAgainLater_RestartsTheThreeSecondTimer()
    {
        GameMessagesViewModel vm = Create();

        vm.Notify("Hãy xới đất trước.");
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(true);
        vm.Notify("Hãy xới đất trước.");

        // The first showing's timer ends at about three seconds; the toast must still be there because its timer was restarted.
        await Task.Delay(TimeSpan.FromSeconds(1.5)).ConfigureAwait(true);
        vm.Toasts.Should().ContainSingle(t => t.Text == "Hãy xới đất trước.");

        // Once the restarted timer ends, the toast is gone.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (vm.Toasts.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50)).ConfigureAwait(true);
        }

        vm.Toasts.Should().BeEmpty();
    }

    private static GameMessagesViewModel Create()
    {
        FakeGameSocket socket = new();
        FarmChannel farm = new(socket);
        PlaceableChannel placeable = new(socket);
        InventoryChannel inventoryChannel = new(socket);
        InventoryViewModel inventory = new(inventoryChannel, new NoIcons(), Localizer<Catalogue>(), Localizer<Language>());
        return new GameMessagesViewModel(farm, placeable, inventory, Localizer<Language>());
    }

    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    private sealed class NoIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = new ItemIcon("sheet.png", 0, 0, 16, 16, 64, 64);
            return true;
        }
    }
}
