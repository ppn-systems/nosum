// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using Nosum.Rendering;
using Xunit;

namespace Nosum.UnitTests.Engine;

/// <summary>The audio commands of docs/adr/ADR-039: their encoding, the cue queue's throttling and allocation, and the settings volume mapping.</summary>
public sealed class AudioTests
{
    private static int[] Ints(RenderCommandBuffer buffer) => MemoryMarshal.Cast<byte, int>(buffer.Encode()).ToArray();

    [Fact]
    public void PlaySound_RecordsOneCommandWithTheArgumentsInTheSourceFields()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(2);

        buffer.PlaySound(7, 80, -30);

        Ints(buffer).Should().Equal(1, 2, RenderCommandBuffer.SoundTexture, 7, 80, -30, 0, 0, 0, 0, RenderCommandBuffer.NoTint);
    }

    [Fact]
    public void PlaySound_ClampsVolumeAndPan()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(1);

        buffer.PlaySound(3, 500, -900);

        Ints(buffer).Skip(RenderCommandBuffer.HeaderInts + 1).Take(3).Should().Equal(3, 100, -100);
    }

    [Fact]
    public void PlaySound_WithoutAnId_RecordsNothing()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(1);

        buffer.PlaySound(0, 100, 0);

        buffer.Count.Should().Be(0);
    }

    [Fact]
    public void SetMusicAndSetAmbientLoop_RecordTheirOwnTextures()
    {
        RenderCommandBuffer buffer = new(4);
        buffer.Begin(1);

        buffer.SetMusic(5);
        buffer.SetAmbientLoop(1, 55);

        int[] ints = Ints(buffer);
        ints[RenderCommandBuffer.HeaderInts].Should().Be(RenderCommandBuffer.MusicTexture);
        ints[RenderCommandBuffer.HeaderInts + 1].Should().Be(5);
        ints[RenderCommandBuffer.HeaderInts + RenderCommandBuffer.IntsPerCommand].Should().Be(RenderCommandBuffer.AmbientLoopTexture);
        ints[RenderCommandBuffer.HeaderInts + RenderCommandBuffer.IntsPerCommand + 1].Should().Be(1);
        ints[RenderCommandBuffer.HeaderInts + RenderCommandBuffer.IntsPerCommand + 2].Should().Be(55);
    }

    [Fact]
    public void Flush_QueuedSounds_AreWrittenOnceAndTheQueueEmpties()
    {
        SoundCues cues = new();
        RenderCommandBuffer buffer = new(8);
        _ = cues.Play(2, 0, 90, 10);
        _ = cues.Play(3, 0);

        buffer.Begin(1);
        int written = cues.Flush(buffer);
        buffer.Begin(1);
        int again = cues.Flush(buffer);

        written.Should().Be(2);
        again.Should().Be(0);
        cues.Pending.Should().Be(0);
    }

    [Fact]
    public void Flush_PutsMusicAndLoopBeforeTheSounds()
    {
        SoundCues cues = new();
        RenderCommandBuffer buffer = new(8);
        _ = cues.Play(4, 0);
        cues.SetMusic(2);
        cues.SetAmbientLoop(1, 60);

        buffer.Begin(1);
        _ = cues.Flush(buffer);

        int[] ints = Ints(buffer);
        int[] textures = [.. Enumerable.Range(0, 3).Select(i => ints[RenderCommandBuffer.HeaderInts + (i * RenderCommandBuffer.IntsPerCommand)])];
        textures.Should().Equal(RenderCommandBuffer.MusicTexture, RenderCommandBuffer.AmbientLoopTexture, RenderCommandBuffer.SoundTexture);
    }

    [Fact]
    public void Flush_MusicAndLoopUnchanged_AreNotSentAgain()
    {
        SoundCues cues = new();
        RenderCommandBuffer buffer = new(8);
        cues.SetMusic(2);
        cues.SetAmbientLoop(1, 60);
        buffer.Begin(1);
        _ = cues.Flush(buffer);

        cues.SetMusic(2);
        cues.SetAmbientLoop(1, 60);
        buffer.Begin(1);
        int written = cues.Flush(buffer);

        written.Should().Be(0);
    }

    [Fact]
    public void Flush_ChangedMusicOrLoopVolume_IsSentOnce()
    {
        SoundCues cues = new();
        RenderCommandBuffer buffer = new(8);
        cues.SetMusic(2);
        cues.SetAmbientLoop(1, 55);
        buffer.Begin(1);
        _ = cues.Flush(buffer);

        cues.SetMusic(4);
        cues.SetAmbientLoop(1, 85);
        buffer.Begin(1);
        int written = cues.Flush(buffer);

        written.Should().Be(2);
    }

    [Fact]
    public void Flush_StoppedLoop_SendsLoopZeroWithNoVolume()
    {
        SoundCues cues = new();
        RenderCommandBuffer buffer = new(8);
        cues.SetAmbientLoop(1, 55);
        buffer.Begin(1);
        _ = cues.Flush(buffer);

        cues.SetAmbientLoop(0, 55);
        buffer.Begin(1);
        _ = cues.Flush(buffer);

        int[] ints = Ints(buffer);
        ints.Skip(RenderCommandBuffer.HeaderInts).Take(3).Should().Equal(RenderCommandBuffer.AmbientLoopTexture, 0, 0);
    }

    [Fact]
    public void Play_SoundAgainSoonerThanItsInterval_IsThrottled()
    {
        SoundCues cues = new();
        cues.SetMinInterval(5, 0.2);

        bool first = cues.Play(5, 1.0);
        bool tooSoon = cues.Play(5, 1.1);
        bool later = cues.Play(5, 1.25);

        first.Should().BeTrue();
        tooSoon.Should().BeFalse();
        later.Should().BeTrue();
        cues.Pending.Should().Be(2);
    }

    [Fact]
    public void Play_ThrottleIsPerSound()
    {
        SoundCues cues = new();
        cues.SetMinInterval(5, 1.0);

        bool a = cues.Play(5, 0);
        bool b = cues.Play(6, 0);

        (a && b).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(SoundCues.MaxSounds)]
    public void Play_InvalidId_IsRefused(int id)
    {
        SoundCues cues = new();

        cues.Play(id, 0).Should().BeFalse();
        cues.Pending.Should().Be(0);
    }

    [Fact]
    public void Play_QueueFull_DropsFurtherSounds()
    {
        SoundCues cues = new();

        int accepted = Enumerable.Range(0, SoundCues.PendingCapacity + 5).Count(_ => cues.Play(1, 0));

        accepted.Should().Be(SoundCues.PendingCapacity);
    }

    [Fact]
    public void Flush_AudioFirst_SurvivesAFullDrawBuffer()
    {
        SoundCues cues = new();
        RenderCommandBuffer buffer = new(2);
        _ = cues.Play(1, 0);
        buffer.Begin(1);

        _ = cues.Flush(buffer);
        buffer.Draw(1, 0, 0, 1, 1, 0, 0);
        buffer.Draw(1, 0, 0, 1, 1, 0, 0);

        Ints(buffer)[RenderCommandBuffer.HeaderInts].Should().Be(RenderCommandBuffer.SoundTexture);
    }

    [Fact]
    public void SetMinInterval_InvalidId_Throws()
    {
        SoundCues cues = new();

        Action act = () => cues.SetMinInterval(0, 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Flush_SteadyFrames_DoNotAllocate()
    {
        SoundCues cues = new();
        cues.SetMinInterval(3, 0.2);
        RenderCommandBuffer buffer = new(64);
        double now = 0;
        for (int warm = 0; warm < 3; warm++)
        {
            Frame();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            Frame();
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);

        void Frame()
        {
            now += 1.0 / 60;
            _ = cues.Play(3, now);
            _ = cues.Play(4, now, 70, -20);
            cues.SetMusic(((int)(now * 4) % 3) + 1);
            cues.SetAmbientLoop(1, 55);
            buffer.Begin(1);
            _ = cues.Flush(buffer);
            _ = buffer.Encode();
        }
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(100, 1.0)]
    [InlineData(50, 0.25)]
    [InlineData(70, 0.49)]
    public void Gain_Slider_IsTheSquareOfTheLevel(int percent, double expected)
    {
        VolumeCurve.Gain(percent).Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData(-20, 0.0)]
    [InlineData(250, 1.0)]
    public void Gain_OutOfRange_IsClamped(int percent, double expected)
    {
        VolumeCurve.Gain(percent).Should().Be(expected);
    }

    [Fact]
    public void Gain_Muted_IsSilentWhateverTheSlider()
    {
        VolumeCurve.Gain(100, muted: true).Should().Be(0);
        VolumeCurve.Gain(100, muted: false).Should().Be(1);
    }

    [Fact]
    public void Gain_RisesWithTheSlider()
    {
        double[] gains = [.. Enumerable.Range(0, 101).Select(VolumeCurve.Gain)];

        gains.Zip(gains.Skip(1), (a, b) => b >= a).Should().OnlyContain(rising => rising);
    }
}
