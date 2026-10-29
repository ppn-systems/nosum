// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Combat.S2C;
using Nosum.Gameplay.Combat;
using Nosum.Wasm.Socket.Bestiary;
using Nosum.Wasm.Socket.Player;
using Nosum.Wasm.ViewModels.Progress;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class BestiaryTests
{
    private static BestiarySnapshot Snapshot()
    {
        BestiarySnapshot snapshot = new() { EquippedSpecies = (byte)MonsterKind.CaveBat };
        snapshot.Rows.Add(new BestiaryRow { Species = (byte)MonsterKind.CaveBat, Kills = 100 });
        snapshot.Rows.Add(new BestiaryRow { Species = (byte)MonsterKind.GreenSlime, Kills = 12 });
        return snapshot;
    }

    [Fact]
    public void Load_StoresTheKillCountsAndTheEquippedTitle()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        BestiaryChannel channel = new(socket);

        channel.Load();

        channel.Current.Equipped.Should().Be(MonsterKind.CaveBat);
        channel.Current.KillsOf(MonsterKind.GreenSlime).Should().Be(12);
        channel.Current.KillsOf(MonsterKind.FrostSlime).Should().Be(0);
    }

    [Fact]
    public void Equip_SendsTheSpeciesAsTheServerReadsIt_AndReportsTheAnswer()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => new EquipBestiaryTitleResult { Status = BestiaryStatus.NotEarned });
        socket.ReplyWith(_ => Snapshot());
        BestiaryChannel channel = new(socket);
        BestiaryStatus? completed = null;
        channel.Completed += status => completed = status;

        channel.Equip(MonsterKind.GreenSlime);

        socket.Requests.OfType<EquipBestiaryTitle>().Single().Species.Should().Be((byte)MonsterKind.GreenSlime);
        completed.Should().Be(BestiaryStatus.NotEarned);
    }

    [Fact]
    public void Entries_ShowTheProgressToTheNextMilestone_AndTheEarnedTitle()
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ => Snapshot());
        BestiaryChannel bestiary = new(socket);
        bestiary.Load();
        using ProgressChannel progress = new(socket);
        using ProgressViewModel vm = new(progress, Localizer<Language>(), null, Localizer<Catalogue>(), bestiary);

        BestiaryEntryDisplay cave = vm.BestiaryEntries.Single(b => b.Species == MonsterKind.CaveBat);
        cave.Kills.Should().Be(100);
        (cave.Fraction, cave.TitleEarned, cave.TitleEquipped).Should().Be((1d, true, true));

        BestiaryEntryDisplay slime = vm.BestiaryEntries.Single(b => b.Species == MonsterKind.GreenSlime);
        slime.Kills.Should().Be(12);
        (slime.Fraction, slime.TitleEarned, slime.TitleEquipped).Should().Be((0.05d, false, false));
    }

    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }
}
