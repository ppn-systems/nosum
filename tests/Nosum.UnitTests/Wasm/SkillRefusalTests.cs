// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.S2C;
using Nosum.Wasm.Socket.Combat;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Progress;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>
/// A refused skill cast says why (docs/adr/ADR-056 D5), and its cooldown sweep runs over the cooldown the server started: the full cooldown after a cast,
/// the time left after a refusal as cooling down.
/// </summary>
public sealed class SkillRefusalTests
{
    private static readonly string[] s_refusalKeys =
    [
        "Combat_Refused_NotJoined",
        "Combat_Refused_UnknownSkill",
        "Combat_Refused_UnknownMonster",
        "Combat_Refused_OutOfRange",
        "Combat_NotEnoughMp",
        "Combat_Refused_AlreadyFull",
        "Combat_Refused_OnCooldown",
    ];

    [Theory]
    [InlineData(CombatStatus.NotJoined)]
    [InlineData(CombatStatus.UnknownSkill)]
    [InlineData(CombatStatus.UnknownMonster)]
    [InlineData(CombatStatus.OutOfRange)]
    [InlineData(CombatStatus.NotEnoughMp)]
    [InlineData(CombatStatus.AlreadyFull)]
    [InlineData(CombatStatus.OnCooldown)]
    public void KeyFor_EveryRefusalOfACast_NamesAMessage(CombatStatus status)
        => SkillRefusalText.KeyFor(status).Should().NotBeNullOrEmpty("the player must be told why the cast did nothing");

    [Fact]
    public void KeyFor_LandedCast_HasNoMessage()
        => SkillRefusalText.KeyFor(CombatStatus.Ok).Should().BeNull();

    [Theory]
    [MemberData(nameof(RefusalKeys))]
    public void RefusalKeys_ResolveInVietnameseAndEnglish(string key)
    {
        LocalizedString vi = Text("vi", key);
        LocalizedString en = Text("en", key);

        vi.ResourceNotFound.Should().BeFalse($"{key} is in the default (Vietnamese) .resx");
        en.ResourceNotFound.Should().BeFalse($"{key} is in Language.en.resx");
        en.Value.Should().NotBe(vi.Value, "the English text is a translation, not a copy of the Vietnamese");
    }

    public static TheoryData<string> RefusalKeys()
    {
        TheoryData<string> data = [];
        foreach (string key in s_refusalKeys)
        {
            data.Add(key);
        }

        return data;
    }

    [Fact]
    public void CooldownStyle_AfterARefusedCast_SweepsOverTheTimeLeft()
    {
        // The server refused Whirlwind because it is still cooling down, with 1.5 s left of its 3 s cooldown.
        FakeGameSocket socket = SocketAnswering(CombatStatus.OnCooldown, cooldownMs: 1500);
        using ProgressChannel progress = new(socket);
        using CombatChannel combat = new(socket);
        using ProgressViewModel vm = new(progress, Localizer<Language>(), combat, Localizer<Catalogue>());

        vm.CastSkill(1);

        vm.IsSkillCoolingDown(1).Should().BeTrue();
        vm.CooldownStyle(1).Should().Be("--cd-ms:1500ms", "the sweep must end when the server's cooldown does, not 3 s later");
    }

    [Fact]
    public void CooldownStyle_AfterALandedCast_SweepsOverTheFullCooldown()
    {
        FakeGameSocket socket = SocketAnswering(CombatStatus.Ok, cooldownMs: 3000);
        using ProgressChannel progress = new(socket);
        using CombatChannel combat = new(socket);
        using ProgressViewModel vm = new(progress, Localizer<Language>(), combat, Localizer<Catalogue>());

        vm.CastSkill(1);

        vm.CooldownStyle(1).Should().Be("--cd-ms:3000ms");
    }

    private static LocalizedString Text(string culture, string key)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
            StringLocalizer<Language> localizer = new(factory);
            return localizer[key];
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    // Answers every cast with the same outcome, so the channel's cooldown bookkeeping is the thing under test.
    private static FakeGameSocket SocketAnswering(CombatStatus status, int cooldownMs)
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(req => new CastSkillResult
        {
            Status = status,
            SkillId = ((Nosum.Contracts.Combat.C2S.CastSkill)req).SkillId,
            CooldownMs = (ushort)cooldownMs,
            Hp = 100,
            MaxHp = 100,
            Mp = 50,
            MaxMp = 50,
        });

        return socket;
    }
}
