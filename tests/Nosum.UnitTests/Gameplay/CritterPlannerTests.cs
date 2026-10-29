// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Nosum.Gameplay.Ambient;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Maps;
using Xunit;

namespace Nosum.UnitTests.Gameplay;

/// <summary>The wildlife planner of docs/adr/ADR-071 D2: the same chunk and day give the same critters, the cap holds, and each kind keeps to its season, time and ground.</summary>
public sealed class CritterPlannerTests
{
    private const long Seed = 0x4E6F73756D5F414DL;
    private const int Salt = 1234;
    private const int Day = 17;
    private const int Noon = 12 * 60;
    private const int Midnight = 1 * 60;

    [Fact]
    public void Plan_SameChunkAndDay_GivesTheSameCritters()
    {
        CritterSpawn[] first = PlanChunk(new ChunkCoord(3, -2), Day, Season.Summer, Noon, woods: true, Ground.Everywhere);
        CritterSpawn[] second = PlanChunk(new ChunkCoord(3, -2), Day, Season.Summer, Noon, woods: true, Ground.Everywhere);

        second.Should().Equal(first);
    }

    [Fact]
    public void Plan_NextDay_ChangesTheCritters()
    {
        bool changed = false;
        for (int x = 0; x < 20 && !changed; x++)
        {
            CritterSpawn[] today = PlanChunk(new ChunkCoord(x, 0), Day, Season.Summer, Noon, woods: true, Ground.Everywhere);
            CritterSpawn[] tomorrow = PlanChunk(new ChunkCoord(x, 0), Day + 1, Season.Summer, Noon, woods: true, Ground.Everywhere);
            changed = !today.AsSpan().SequenceEqual(tomorrow);
        }

        changed.Should().BeTrue("a new day reshuffles the wildlife of the chunks");
    }

    [Fact]
    public void Plan_ManyChunks_NeverExceedsTheCap()
    {
        for (int x = -10; x < 10; x++)
        {
            for (int y = -10; y < 10; y++)
            {
                CritterSpawn[] planned = PlanChunk(new ChunkCoord(x, y), Day, Season.Summer, Noon, woods: true, Ground.Everywhere);

                planned.Length.Should().BeLessThanOrEqualTo(CritterPlanner.MaxPerChunk);
            }
        }
    }

    [Fact]
    public void Plan_WithASmallBuffer_WritesNoMoreThanItsLength()
    {
        Span<CritterSpawn> small = stackalloc CritterSpawn[2];
        int written = CritterPlanner.Plan(Seed, Salt, new ChunkCoord(1, 1), Day, Season.Summer, Noon, true, Terrain(Ground.Everywhere), small);

        written.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void Plan_FirefliesAppearOnlyOnSummerNights()
    {
        AllCritters(Season.Summer, Midnight).Should().Contain(s => s.Kind == CritterKind.Firefly);
        AllCritters(Season.Summer, Noon).Should().NotContain(s => s.Kind == CritterKind.Firefly);
        AllCritters(Season.Winter, Midnight).Should().NotContain(s => s.Kind == CritterKind.Firefly);
        AllCritters(Season.Spring, Midnight).Should().NotContain(s => s.Kind == CritterKind.Firefly);
    }

    [Fact]
    public void Plan_ButterfliesAppearOnlyByDayInSpringAndSummer()
    {
        AllCritters(Season.Spring, Noon).Should().Contain(s => s.Kind == CritterKind.Butterfly);
        AllCritters(Season.Summer, Noon).Should().Contain(s => s.Kind == CritterKind.Butterfly);
        AllCritters(Season.Winter, Noon).Should().NotContain(s => s.Kind == CritterKind.Butterfly);
        AllCritters(Season.Summer, Midnight).Should().NotContain(s => s.Kind == CritterKind.Butterfly);
    }

    [Fact]
    public void Plan_SquirrelsAppearOnlyByDayInTheWoods()
    {
        AllCritters(Season.Fall, Noon, woods: true).Should().Contain(s => s.Kind == CritterKind.Squirrel);
        AllCritters(Season.Fall, Noon, woods: false).Should().NotContain(s => s.Kind == CritterKind.Squirrel);
        AllCritters(Season.Fall, Midnight, woods: true).Should().NotContain(s => s.Kind == CritterKind.Squirrel);
    }

    [Fact]
    public void Plan_FrogsAppearOnlyNextToWater()
    {
        AllCritters(Season.Spring, Noon, ground: Ground.DryLand).Should().NotContain(s => s.Kind == CritterKind.Frog);
        AllCritters(Season.Spring, Noon, ground: Ground.Everywhere).Should().Contain(s => s.Kind == CritterKind.Frog);
    }

    [Fact]
    public void Plan_Spawns_StayInsideTheirChunkAndTheSubTileRange()
    {
        var chunk = new ChunkCoord(-2, 3);
        CritterSpawn[] planned = PlanChunk(chunk, Day, Season.Summer, Noon, woods: true, Ground.Everywhere);

        foreach (CritterSpawn s in planned)
        {
            ChunkCoord.OfTile(s.TileX, s.TileY).Should().Be(chunk);
            s.SubX.Should().BeInRange(0, CritterPlanner.SubUnits - 1);
            s.SubY.Should().BeInRange(0, CritterPlanner.SubUnits - 1);
            s.Id.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(5 * 60 + 59, true)]
    [InlineData(6 * 60, false)]
    [InlineData(12 * 60, false)]
    [InlineData(18 * 60 + 59, false)]
    [InlineData(19 * 60, true)]
    public void IsNight_ByMinuteOfTheDay_IsNightBeforeSixAndFromSevenInTheEvening(int minute, bool night)
    {
        CritterPlanner.IsNight(minute).Should().Be(night);
    }

    private static CritterSpawn[] PlanChunk(ChunkCoord chunk, int day, Season season, int minute, bool woods, Ground ground)
    {
        CritterSpawn[] buffer = new CritterSpawn[CritterPlanner.MaxPerChunk];
        int written = CritterPlanner.Plan(Seed, Salt, chunk, day, season, minute, woods, Terrain(ground), buffer);
        return buffer[..written];
    }

    private static List<CritterSpawn> AllCritters(Season season, int minute, bool woods = true, Ground ground = Ground.Everywhere)
    {
        var all = new List<CritterSpawn>();
        for (int x = -8; x < 8; x++)
        {
            for (int y = -8; y < 8; y++)
            {
                all.AddRange(PlanChunk(new ChunkCoord(x, y), Day, season, minute, woods, ground));
            }
        }

        return all;
    }

    private enum Ground
    {
        Everywhere,
        DryLand,
    }

    private sealed class GroundFake : ICritterTerrain
    {
        private readonly bool _water;

        public GroundFake(bool water) => _water = water;

        public bool Clear(int tileX, int tileY) => true;

        public bool NearWater(int tileX, int tileY) => _water;
    }

    private static ICritterTerrain Terrain(Ground ground) => new GroundFake(ground == Ground.Everywhere);
}
