// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Farm;
using Nosum.Contracts.Farm.S2C;
using Nosum.Contracts.Inventory.S2C;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.C2S;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Skills;
using Nosum.Wasm.Socket.Farm;
using Nosum.Wasm.Socket.Inventory;
using Nosum.Wasm.Socket.Market;
using Nosum.Wasm.Socket.Placeable;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels;
using Nosum.Wasm.ViewModels.Feedback;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Progress;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The client side of energy and skills (docs/adr/ADR-023 D6): the mirror, the energy bar and Skills page view model, the toasts and the menu tab.</summary>
[Collection(SharedUiCulture.Name)]
public sealed class ProgressTests
{
    private static void PushEnergy(FakeGameSocket socket, int energy, int max = 270)
    {
        using EnergyChanged packet = new() { Energy = (ushort)energy, MaxEnergy = (ushort)max };
        socket.Push(packet);
    }

    private static void PushXp(FakeGameSocket socket, int farming, int foraging = 0, int mining = 0)
    {
        using SkillXpChanged packet = new() { FarmingXp = farming, ForagingXp = foraging, MiningXp = mining };
        socket.Push(packet);
    }

    [Fact]
    public void Channel_PushedEnergyAndExperience_AreMirroredAndRaiseChanged()
    {
        FakeGameSocket socket = new();
        using ProgressChannel channel = new(socket);
        int raised = 0;
        channel.Changed += () => raised++;
        channel.MaxEnergy.Should().Be(0, "the bar stays hidden until the server speaks");

        PushEnergy(socket, 268);
        PushXp(socket, 100, 7, 123_456);

        (channel.Energy, channel.MaxEnergy).Should().Be((268, 270));
        (channel.FarmingXp, channel.ForagingXp, channel.MiningXp).Should().Be((100, 7, 123_456));
        raised.Should().Be(2);
    }

    [Fact]
    public void Channel_LevelUp_RaisesLevelledUpWithTheSkillAndLevel()
    {
        FakeGameSocket socket = new();
        using ProgressChannel channel = new(socket);
        (byte Skill, int Level)? seen = null;
        channel.LevelledUp += (skill, level) => seen = (skill, level);

        using LevelUp packet = new() { Skill = 3, Level = 4 };
        socket.Push(packet);

        seen.Should().Be(((byte)3, 4));
    }

    [Fact]
    public void Channel_Eat_SendsTheSlot_AndReportsTheServersAnswer()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(request => new EatResult { Status = ((Eat)request).Slot == 8 ? EatStatus.Ok : EatStatus.NotEdible });
        using ProgressChannel channel = new(socket);
        EatStatus? answer = null;
        channel.EatCompleted += status => answer = status;

        channel.Eat(8);

        socket.Requests.Should().ContainSingle().Which.Should().BeOfType<Eat>();
        answer.Should().Be(EatStatus.Ok);
    }

    [Fact]
    public void ViewModel_BeforeTheServerSpeaks_HasNoBar_AndAfterwardsShowsTheFraction()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        using ProgressChannel channel = new(socket);
        using ProgressViewModel vm = new(channel, Localizer<Language>());
        int raised = 0;
        vm.Changed += () => raised++;
        vm.HasEnergy.Should().BeFalse();

        PushEnergy(socket, 135);

        vm.HasEnergy.Should().BeTrue();
        vm.EnergyFraction.Should().Be(0.5);
        vm.EnergyLow.Should().BeFalse();
        vm.EnergyLabel.Should().Be("Energy 135 / 270");
        raised.Should().Be(1);

        PushEnergy(socket, 0);
        vm.EnergyFraction.Should().Be(0);
        vm.EnergyLow.Should().BeTrue("an empty bar warns");
    }

    [Fact]
    public void ViewModel_Skills_AreLevelledByTheServersTable_AndShowProgressToTheNextLevel()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        using ProgressChannel channel = new(socket);
        using ProgressViewModel vm = new(channel, Localizer<Language>());

        PushXp(socket, farming: 0, foraging: 240, mining: SkillRules.XpCap + 500);

        vm.Skills.Should().HaveCount(3);
        vm.Skills[0].Should().BeEquivalentTo(new { Name = "Farming", Level = 0, Xp = 0, Fraction = 0.0, Caption = "0 / 100" });
        vm.Skills[1].Should().BeEquivalentTo(new { Name = "Foraging", Level = 1, Xp = 240, Fraction = 140.0 / 280.0, Caption = "140 / 280" });
        vm.Skills[2].Should().BeEquivalentTo(new { Name = "Mining", Level = 10, Fraction = 1.0, Caption = "Max level" });
    }

    [Theory]
    [InlineData("en", "Foraging")]
    [InlineData("vi", "Hái lượm")]
    public void ViewModel_SkillName_FollowsTheLanguage(string culture, string expected)
    {
        using CultureScope scope = new(culture);
        FakeGameSocket socket = new();
        using ProgressChannel channel = new(socket);
        using ProgressViewModel vm = new(channel, Localizer<Language>());

        vm.SkillName((byte)SkillKind.Foraging).Should().Be(expected);
        vm.SkillName(0).Should().BeEmpty();
    }

    [Theory]
    [InlineData("en", "Mining reached level 2!")]
    [InlineData("vi", "Khai khoáng lên cấp 2!")]
    public void Messages_ALevelUp_ShowsAToast(string culture, string expected)
    {
        using CultureScope scope = new(culture);
        (GameMessagesViewModel messages, FakeGameSocket socket) = CreateMessages();
        using (messages)
        {
            using LevelUp packet = new() { Skill = (byte)SkillKind.Mining, Level = 2 };
            socket.Push(packet);

            messages.Message.Should().Be(expected);
        }
    }

    [Fact]
    public void Messages_RefusedEating_AndNoEnergy_SayWhy_ButASuccessfulMealIsSilent()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new EatResult { Status = EatStatus.EnergyFull });
        socket.ReplyWith(_ => new UseItemResult { Status = FarmStatus.NoEnergy });
        (GameMessagesViewModel messages, _, ProgressChannel progress, FarmChannel farm) = CreateMessages(socket);
        using (messages)
        {
            progress.Eat(8);
            messages.Message.Should().Be("You are full of energy already.");

            farm.Use(0, 3, 3);
            messages.Message.Should().Be("You are out of energy. Eat some produce, or wait for the next day.");

            socket.ReplyWith(_ => new EatResult { Status = EatStatus.Ok });
            progress.Eat(8);
            messages.Message.Should().Be("You are out of energy. Eat some produce, or wait for the next day.", "a good meal adds no message");
        }
    }

    [Fact]
    public void Toolbar_HasASkillsTab_WithItsOwnIcon_BeforeTheProfile()
    {
        using ToolbarViewModel toolbar = new(Localizer<Language>());

        ToolbarItem skills = toolbar.Items.Should().ContainSingle(i => i.Kind == PanelKind.Skills).Subject;
        skills.Icon.Should().Be("tab-skills");
        skills.Label.Should().NotBeNullOrWhiteSpace().And.NotStartWith("Toolbar_");
        toolbar.Items.Select(i => i.Kind).ToList().IndexOf(PanelKind.Skills).Should().BeLessThan(toolbar.Items.Select(i => i.Kind).ToList().IndexOf(PanelKind.Settings));
    }

    [Fact]
    public void EdibleItems_AreTheRawCrops_AndNothingElseInTheStarterKit()
    {
        ItemCatalog.TryGet(ItemCatalog.Parsnip, out ItemDefinition? parsnip).Should().BeTrue();
        parsnip!.IsEdible.Should().BeTrue();
        ItemCatalog.TryGet(ItemCatalog.Hoe, out ItemDefinition? hoe).Should().BeTrue();
        hoe!.IsEdible.Should().BeFalse();
    }

    private static (GameMessagesViewModel, FakeGameSocket) CreateMessages()
    {
        FakeGameSocket socket = new();
        (GameMessagesViewModel messages, _, _, _) = CreateMessages(socket);
        return (messages, socket);
    }

    [Theory]
    [InlineData("en", "Broodmother", "Green slime", "Boss")]
    [InlineData("vi", "Chúa nhện hang sâu", "Slime xanh", "Thủ lĩnh")]
    public void Bestiary_ContainsAllMonsterCatalogSpecies_AndFollowsTheLanguage(string culture, string expectedBoss, string expectedRegular, string expectedBossCategory)
    {
        using CultureScope scope = new(culture);
        FakeGameSocket socket = new();
        using ProgressChannel channel = new(socket);
        using ProgressViewModel vm = new(channel, Localizer<Language>(), null, Localizer<Catalogue>());

        IReadOnlyList<BestiaryEntryDisplay> bestiary = vm.BestiaryEntries;
        bestiary.Should().HaveCount(12);
        bestiary.Count(b => b.IsBoss).Should().Be(4);
        bestiary.Should().Contain(b => b.Name == expectedBoss && b.Category == expectedBossCategory && b.IsBoss);
        bestiary.Should().Contain(b => b.Name == expectedRegular && !b.IsBoss);
        bestiary.Should().OnlyContain(b => !b.Name.StartsWith("Monster_", StringComparison.Ordinal) && !b.PrimaryDrop.StartsWith("Item_", StringComparison.Ordinal));
    }

    [Fact]
    public void CombatChannel_TriggerLevelUp_ActivatesRadianceBeaconAndHudBadge()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        using ProgressChannel progress = new(socket);
        using Nosum.Wasm.Socket.Combat.CombatChannel combat = new(socket);
        using ProgressViewModel vm = new(progress, Localizer<Language>(), combat, Localizer<Catalogue>());

        int celebrated = 0;
        combat.LevelledUp += lvl => celebrated = lvl;
        combat.SetLocalPosition(10, 9);
        combat.TriggerLevelUp(2);

        celebrated.Should().Be(2);
        vm.IsLevelUpActive.Should().BeTrue();
        vm.ActiveLevelUpValue.Should().Be(2);
        vm.LevelUpBadgeText.Should().Be("Level 2!");
        combat.TryGetActiveLevelUp(Environment.TickCount64, out float prog, out int lvlOut, out int px, out int py).Should().BeTrue();
        lvlOut.Should().Be(2);
        (px, py).Should().Be((10, 9));
        prog.Should().BeInRange(0f, 1f);
    }

    [Fact]
    public void CombatChannel_TriggerSkillEffect_ActivatesImmediateVisualAnimationForAllThreeSkills()
    {
        FakeGameSocket socket = new();
        using Nosum.Wasm.Socket.Combat.CombatChannel combat = new(socket);
        combat.SetLocalPosition(12, 15, 1);

        combat.TriggerSkillEffect(1);
        combat.TryGetActiveSkillEffect(Environment.TickCount64, out byte skillId, out float progress, out int px, out int py).Should().BeTrue();
        skillId.Should().Be(1);
        (px, py).Should().Be((12, 15));
        progress.Should().BeInRange(0f, 1f);

        combat.TriggerSkillEffect(2);
        List<Nosum.Wasm.Socket.Combat.ProjectileClientRow> projectiles = [];
        combat.CopyProjectiles(projectiles, Environment.TickCount64);
        projectiles.Should().Contain(p => p.Kind == 2);

        combat.TriggerSkillEffect(3);
        combat.TryGetActiveSkillEffect(Environment.TickCount64, out skillId, out _, out _, out _).Should().BeTrue();
        skillId.Should().Be(3);
    }

    [Fact]
    public async System.Threading.Tasks.Task CombatSkills_ReflectCooldownAndMpReadiness()
    {
        using CultureScope scope = new("en");
        FakeGameSocket socket = new();
        socket.ReplyWith(req =>
        {
            Nosum.Contracts.Combat.C2S.CastSkill cast = (Nosum.Contracts.Combat.C2S.CastSkill)req;
            return new Nosum.Contracts.Combat.S2C.CastSkillResult
            {
                Status = Nosum.Contracts.Combat.CombatStatus.Ok,
                SkillId = cast.SkillId,
                CooldownMs = 120,
                Hp = 100,
                MaxHp = 100,
                Mp = 10,
                MaxMp = 50,
            };
        });
        using ProgressChannel progress = new(socket);
        using Nosum.Wasm.Socket.Combat.CombatChannel combat = new(socket);
        using ProgressViewModel vm = new(progress, Localizer<Language>(), combat, Localizer<Catalogue>());

        vm.CanCastSkill(1).Should().BeTrue();
        vm.WhirlwindDisabled.Should().BeFalse();

        vm.CastSkill(1);

        vm.GetSkillCooldown(1).Should().BeGreaterThan(0f);
        vm.WhirlwindDisabled.Should().BeTrue("the skill is on cooldown and MP dropped to 10");
        vm.WhirlwindLabel.Should().Contain("(1s)");
        vm.FireballDisabled.Should().BeTrue("MP (10) is below Fireball's cost (20)");

        await System.Threading.Tasks.Task.Delay(220);

        vm.GetSkillCooldown(1).Should().Be(0f);
    }

    [SuppressMessage("Reliability", "CA2000", Justification = "The channels live as long as the test; the view model that owns the subscriptions is disposed by the test.")]
    private static (GameMessagesViewModel Messages, FakeGameSocket Socket, ProgressChannel Progress, FarmChannel Farm) CreateMessages(FakeGameSocket socket)
    {
        FarmChannel farm = new(socket);
        PlaceableChannel placeable = new(socket);
        InventoryChannel inventoryChannel = new(socket);
        InventoryViewModel inventory = new(inventoryChannel, new NoIcons(), Localizer<Catalogue>(), Localizer<Language>());
        ProgressChannel progress = new(socket);
        MarketChannel market = new(socket);
        return (new GameMessagesViewModel(farm, placeable, inventory, Localizer<Language>(), market, progress), socket, progress, farm);
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
            icon = default!;
            return false;
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

        public CultureScope(string name) => CultureInfo.CurrentUICulture = new CultureInfo(name);

        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }
}
