// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Skills;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Progress;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The top-of-screen level-up banner: it names the skill and the level, and follows the same sources as the message line (docs/adr/ADR-062).</summary>
[Collection(SharedUiCulture.Name)]
public sealed class LevelUpBannerViewModelTests
{
    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    [Fact]
    public void Banner_AGatheringLevelUp_NamesTheSkillAndTheLevel()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FakeGameSocket socket = new();
        using ProgressChannel progress = new(socket);
        using LevelUpBannerViewModel vm = new(progress, Localizer<Language>());
        int raised = 0;
        vm.Changed += () => raised++;

        using LevelUp packet = new() { Skill = (byte)SkillKind.Mining, Level = 4 };
        socket.Push(packet);

        vm.Title.Should().Be("LEVEL UP!");
        vm.Message.Should().Be("Mining reached level 4!");
        raised.Should().Be(1);
    }

    [Fact]
    public void Banner_ACombatLevelUpThatComesAsThePacketAndAsTheCombatEvent_IsShownOnce()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FakeGameSocket socket = new();
        using ProgressChannel progress = new(socket);
        using Nosum.Wasm.Socket.Combat.CombatChannel combat = new(socket);
        using LevelUpBannerViewModel vm = new(progress, Localizer<Language>());
        int raised = 0;
        vm.Changed += () => raised++;

        // The server sends the LevelUp packet for a combat level; the combat channel then raises its own event for the same level.
        using LevelUp packet = new() { Skill = (byte)SkillKind.Combat, Level = 3 };
        socket.Push(packet);
        combat.TriggerLevelUp(3);

        vm.Message.Should().Be("Combat reached level 3!");
        raised.Should().Be(1, "one level-up is one banner");
    }

    [Fact]
    public void Banner_ACombatChannelLevelUpWithNoPacket_IsIgnored()
    {
        using WasmTestSupport.CultureScope scope = new("en");
        FakeGameSocket socket = new();
        using ProgressChannel progress = new(socket);
        using Nosum.Wasm.Socket.Combat.CombatChannel combat = new(socket);
        using LevelUpBannerViewModel vm = new(progress, Localizer<Language>());

        combat.TriggerLevelUp(2);

        vm.Message.Should().BeNull("the server's LevelUp packet is the one source of the banner");
    }
}
