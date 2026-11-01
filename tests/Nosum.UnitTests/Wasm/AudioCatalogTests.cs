// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FluentAssertions;
using Nosum.Contracts.Farm;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Mines;
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
            bytes.Length.Should().BeInRange(1000, file.Kind == AudioKind.Track ? 1_000_000 : 200_000, file.File);
        }
    }

    [Fact]
    public void Files_EveryMusicTrackIdHasAFile()
    {
        AudioFile[] tracks = [.. AudioCatalog.Files.Where(f => f.Kind == AudioKind.Track)];

        tracks.Select(f => f.Id).Should().BeEquivalentTo(Enumerable.Range(1, 8));
        foreach (AudioFile track in tracks)
        {
            File.Exists(RepoFiles.Path("wasm", "Nosum.Wasm", "wwwroot", "audio", track.File)).Should().BeTrue($"{track.File} is in the catalogue");
        }
    }

    [Fact]
    public void Files_EveryMusicTrackIsAMonoEightBitLoopOfThirtyToSixtySeconds()
    {
        foreach (AudioFile track in AudioCatalog.Files.Where(f => f.Kind == AudioKind.Track))
        {
            // The canonical 44-byte WAV header that tools/synth_music.py writes: channels at 22, rate at 24, bits at 34, data size at 40.
            byte[] bytes = File.ReadAllBytes(RepoFiles.Path("wasm", "Nosum.Wasm", "wwwroot", "audio", track.File));
            ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(22));
            uint rate = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24));
            ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(34));
            uint dataBytes = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40));

            (channels, rate, bits).Should().Be(((ushort)1, 22050u, (ushort)8), track.File);
            ((double)dataBytes / rate).Should().BeInRange(30, 60, track.File);
        }
    }

    [Fact]
    public void Files_MeasuredLevelsMatchTheFilesOnDisk()
    {
        // MeasuredDbfs is what tools/measure_audio.py prints for each file (docs/adr/ADR-090). Re-measuring here makes a changed file fail the test.
        foreach (AudioFile file in AudioCatalog.Files)
        {
            MeasureActiveRmsDbfs(ReadMono(file.File)).Should().BeApproximately(file.MeasuredDbfs, 0.15, file.File);
        }
    }

    [Fact]
    public void Files_GainLeavesHeadroomBelowFullScale()
    {
        // After the load gain, no file may reach 0 dBFS: a decoded buffer is not clamped, so a peak above full scale would distort.
        foreach (AudioFile file in AudioCatalog.Files)
        {
            (MeasurePeakDbfs(ReadMono(file.File)) + file.GainDb).Should().BeLessThan(-0.5, file.File);
        }
    }

    [Fact]
    public void Mix_ClassesKeepTheirOrder()
    {
        AudioMix.Footstep.Should().BeLessThan(AudioMix.Action, "footsteps are felt under the tools");
        AudioMix.Interface.Should().BeLessThan(AudioMix.Action, "clicks are quieter than the cues the player acts on");
        AudioMix.Action.Should().Be(AudioMix.Music, "the cues and the bed share a level");
        AudioMix.Ambience.Should().Be(AudioMix.Music, "the rain bed sits at the music's level before its per-cue volume");
    }

    [Fact]
    public void Mix_EveryMusicTrackIsNormalizedToTheMusicTarget()
    {
        AudioCatalog.Files.Where(f => f.Kind == AudioKind.Track).Should().OnlyContain(f => f.TargetDbfs == AudioMix.Music);
    }

    [Fact]
    public void Mix_GainsStayWithinTwelveDecibels()
    {
        // A gain beyond this would mean the file was recorded far from its class, and the measurement or the class is wrong.
        AudioCatalog.Files.Should().OnlyContain(f => Math.Abs(f.GainDb) <= 12, "no file is more than 12 dB from its class");
    }

    private static double[] ReadMono(string name)
    {
        byte[] bytes = File.ReadAllBytes(RepoFiles.Path("wasm", "Nosum.Wasm", "wwwroot", "audio", name));
        int at = 12;
        while (System.Text.Encoding.ASCII.GetString(bytes, at, 4) != "fmt ")
        {
            at += 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4));
        }

        ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 10));
        ushort bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at + 22));
        while (System.Text.Encoding.ASCII.GetString(bytes, at, 4) != "data")
        {
            at += 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4));
        }

        int dataStart = at + 8;
        int width = bits / 8;
        int frames = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4)) / (width * channels);
        double[] mono = new double[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int sample = dataStart + (((f * channels) + c) * width);
                sum += width == 1
                    ? (bytes[sample] - 128) / 128.0
                    : BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(sample)) / 32768.0;
            }

            mono[f] = sum / channels;
        }

        return mono;
    }

    private static double MeasurePeakDbfs(double[] samples)
    {
        double peak = samples.Max(s => Math.Abs(s));
        return 20 * Math.Log10(peak);
    }

    /// <summary>The RMS over the samples above a -50 dBFS gate, as tools/measure_audio.py computes it.</summary>
    private static double MeasureActiveRmsDbfs(double[] samples)
    {
        const double gate = 0.0031622776601683794; // 10^(-50/20)
        double[] active = [.. samples.Where(s => Math.Abs(s) >= gate)];
        double meanSquare = active.Sum(s => s * s) / active.Length;
        return 10 * Math.Log10(meanSquare);
    }

    [Theory]
    [InlineData(ToolKind.Hoe, SoundIds.ToolHoe)]
    [InlineData(ToolKind.WateringCan, SoundIds.ToolWater)]
    [InlineData(ToolKind.Axe, SoundIds.ToolAxe)]
    [InlineData(ToolKind.Pickaxe, SoundIds.ToolPickaxe)]
    [InlineData(ToolKind.Scythe, SoundIds.ToolScythe)]
    [InlineData(ToolKind.MilkPail, SoundIds.ToolScythe)]
    [InlineData(ToolKind.Sword, SoundIds.SwordSlash)]
    [InlineData(ToolKind.Staff, SoundIds.StaffCast)]
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

    [Fact]
    public void MusicTrack_EachMineBiomeHasADistinctExistingTrack()
    {
        int[] tracks = [.. System.Enum.GetValues<MineBiome>().Select(MusicTrackIds.ForMine)];

        tracks.Should().OnlyHaveUniqueItems();
        tracks.Should().OnlyContain(id => id >= 1 && id <= 8);
    }

    [Fact]
    public void MusicTrack_ForMineEarth_IsSpringDay()
    {
        int track = MusicTrackIds.ForMine(MineBiome.Earth);

        track.Should().Be(MusicTrackIds.For(Season.Spring, false));
    }
}
