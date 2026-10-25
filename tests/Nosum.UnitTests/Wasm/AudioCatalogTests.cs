// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Weather;
using Nosum.UnitTests.Maps;
using Nosum.Wasm.ViewModels.Audio;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The hooks' rules of docs/adr/ADR-039: which sound or track plays when, and that the files and the catalogue agree.</summary>
public sealed class AudioCatalogTests
{
    [Fact]
    public void Files_EverySoundHasAFileOnDisk()
    {
        foreach (AudioFile file in AudioCatalog.Files)
        {
            File.Exists(RepoFiles.Path("wasm", "Nosum.Wasm", "wwwroot", "audio", file.File)).Should().BeTrue($"{file.File} is in the catalogue");
        }
    }

    [Fact]
    public void Files_EveryWavOnDiskIsInTheCatalogue()
    {
        string[] onDisk = [.. Directory.GetFiles(RepoFiles.Path("wasm", "Nosum.Wasm", "wwwroot", "audio"), "*.wav").Select(Path.GetFileName).OfType<string>()];

        onDisk.Should().BeEquivalentTo(AudioCatalog.Files.Select(f => f.File));
    }

    [Fact]
    public void Files_IdsAreUniqueWithinTheirKindAndFitTheQueue()
    {
        AudioCatalog.Files.GroupBy(f => (f.Kind, f.Id)).Should().OnlyContain(g => g.Count() == 1);
        AudioCatalog.Files.Where(f => f.Kind == AudioKind.Sound).Should().OnlyContain(f => f.Id >= 1 && f.Id < Nosum.Rendering.SoundCues.MaxSounds);
    }

    [Fact]
    public void Files_EveryCatalogueFileIsAValidWav()
    {
        foreach (AudioFile file in AudioCatalog.Files)
        {
            byte[] bytes = File.ReadAllBytes(RepoFiles.Path("wasm", "Nosum.Wasm", "wwwroot", "audio", file.File));
            System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("RIFF", file.File);
            System.Text.Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WAVE", file.File);
            bytes.Length.Should().BeInRange(1000, 200_000, file.File);
        }
    }

    [Theory]
    [InlineData(ToolKind.Hoe, SoundIds.ToolHoe)]
    [InlineData(ToolKind.WateringCan, SoundIds.ToolWater)]
    [InlineData(ToolKind.Axe, SoundIds.ToolAxe)]
    [InlineData(ToolKind.Pickaxe, SoundIds.ToolPickaxe)]
    [InlineData(ToolKind.Scythe, SoundIds.ToolScythe)]
    [InlineData(ToolKind.MilkPail, SoundIds.ToolScythe)]
    [InlineData(ToolKind.None, 0)]
    public void ToolSound_PerTool(ToolKind tool, int expected)
    {
        AudioCatalog.ToolSound(tool).Should().Be(expected);
    }

    [Fact]
    public void ToolSound_EveryToolHasADistinctOrSharedSoundButNeverNone()
    {
        foreach (ToolKind tool in System.Enum.GetValues<ToolKind>().Where(t => t != ToolKind.None))
        {
            AudioCatalog.ToolSound(tool).Should().BeGreaterThan(0, tool.ToString());
        }
    }

    [Theory]
    [InlineData(FarmAction.Harvested, SoundIds.Harvest)]
    [InlineData(FarmAction.Planted, SoundIds.Plant)]
    [InlineData(FarmAction.PlantedTree, SoundIds.Plant)]
    [InlineData(FarmAction.Refilled, SoundIds.ToolWater)]
    [InlineData(FarmAction.Descended, SoundIds.Door)]
    [InlineData(FarmAction.Tilled, 0)]
    [InlineData(FarmAction.None, 0)]
    public void OutcomeSound_PerAnswer(FarmAction action, int expected)
    {
        AudioCatalog.OutcomeSound(action).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5 * 60 + 59, true)]
    [InlineData(6 * 60, false)]
    [InlineData(12 * 60, false)]
    [InlineData(19 * 60 + 59, false)]
    [InlineData(20 * 60, true)]
    [InlineData(23 * 60 + 59, true)]
    public void IsNight_FollowsDawnAndDusk(int minute, bool night)
    {
        AudioCatalog.IsNight(minute).Should().Be(night);
    }

    [Fact]
    public void MusicTrack_EverySeasonAndHalfDayHasItsOwnIdFromOneToEight()
    {
        int[] ids = [.. System.Enum.GetValues<Season>().SelectMany(s => new[] { MusicTrackIds.For(s, false), MusicTrackIds.For(s, true) })];

        ids.Should().BeEquivalentTo(Enumerable.Range(1, 8));
    }

    [Theory]
    [InlineData(WeatherKind.Rain, false, LoopIds.Rain, AudioCatalog.RainVolume)]
    [InlineData(WeatherKind.Storm, false, LoopIds.Rain, AudioCatalog.StormVolume)]
    [InlineData(WeatherKind.Rain, true, LoopIds.None, 0)]
    [InlineData(WeatherKind.Storm, true, LoopIds.None, 0)]
    [InlineData(WeatherKind.Sun, false, LoopIds.None, 0)]
    [InlineData(WeatherKind.Snow, false, LoopIds.None, 0)]
    public void AmbientLoopFor_RainOutdoorsOnly(WeatherKind weather, bool indoors, int loop, int volume)
    {
        AudioCatalog.AmbientLoopFor(weather, indoors, out int actual).Should().Be(loop);
        actual.Should().Be(volume);
    }

    [Fact]
    public void PickupCue_OwnPickup_IsLoudAndCentred()
    {
        AudioCatalog.PickupCue(5, -5, out int volume, out int pan).Should().BeTrue();

        (volume, pan).Should().Be((100, 0));
    }

    [Fact]
    public void PickupCue_NeighbourToTheRight_IsQuietAndPannedRight()
    {
        AudioCatalog.PickupCue(120, 0, out int volume, out int pan).Should().BeTrue();

        volume.Should().BeLessThan(100);
        pan.Should().BePositive();
    }

    [Fact]
    public void PickupCue_NeighbourToTheLeft_IsPannedLeft()
    {
        AudioCatalog.PickupCue(-150, 40, out _, out int pan).Should().BeTrue();

        pan.Should().BeNegative();
    }

    [Fact]
    public void PickupCue_OutOfEarshot_IsSilent()
    {
        AudioCatalog.PickupCue(AudioCatalog.EarshotRadius + 1, 0, out int volume, out _).Should().BeFalse();

        volume.Should().Be(0);
    }

    [Fact]
    public void FootstepTimer_Standing_NeverSteps()
    {
        FootstepTimer timer = default;

        bool any = Enumerable.Range(0, 120).Any(_ => timer.Advance(false, 1.0 / 60));

        any.Should().BeFalse();
    }

    [Fact]
    public void FootstepTimer_Walking_StepsAtTheInterval()
    {
        FootstepTimer timer = default;

        int steps = Enumerable.Range(0, 60 * 4).Count(_ => timer.Advance(true, 1.0 / 60));

        // Four seconds of walking: the first step after half an interval, then one every interval.
        steps.Should().Be(1 + (int)((4 - (AudioCatalog.StepIntervalSeconds / 2)) / AudioCatalog.StepIntervalSeconds));
    }

    [Fact]
    public void FootstepTimer_StoppingResetsTheCadence()
    {
        FootstepTimer timer = default;
        _ = Enumerable.Range(0, 5).Count(_ => timer.Advance(true, 1.0 / 60));

        _ = timer.Advance(false, 1.0 / 60);
        bool immediately = timer.Advance(true, 1.0 / 60);

        immediately.Should().BeFalse();
    }
}
