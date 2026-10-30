// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Nosum.Wasm.ViewModels.Pets;
using Xunit;

namespace Nosum.UnitTests.Wasm;

public sealed class PetFollowTests
{
    private const byte Down = 0;
    private const byte Right = 1;
    private const byte Up = 2;
    private const byte Left = 3;

    [Theory]
    [InlineData(Down, 100f, 36f)]
    [InlineData(Right, 86f, 50f)]
    [InlineData(Up, 100f, 64f)]
    [InlineData(Left, 114f, 50f)]
    public void PlaceOf_OwnerFacing_StandsBehindOwner(byte facing, float x, float y)
    {
        (float px, float py) = PetFollow.PlaceOf(100, 50, facing);

        px.Should().BeApproximately(x, 0.001f);
        py.Should().BeApproximately(y, 0.001f);
    }

    [Fact]
    public void Place_NewPet_StandsAtItsPlaceWithNoIdleTime()
    {
        PetTrack pet = PetFollow.Place(100, 50, Down);

        pet.X.Should().Be(100);
        pet.Y.Should().Be(50 - PetFollow.TrailDistance);
        pet.IdleSeconds.Should().Be(0);
        pet.Walking.Should().BeFalse();
    }

    [Fact]
    public void Step_OwnerStillAndPetInPlace_PetStaysAndDoesNotWalk()
    {
        PetTrack pet = PetFollow.Place(100, 50, Down);

        PetTrack after = PetFollow.Step(pet, 100, 50, Down, ownerMoving: false, seconds: 0.5);

        after.X.Should().Be(pet.X);
        after.Y.Should().Be(pet.Y);
        after.Walking.Should().BeFalse();
    }

    [Fact]
    public void Step_OwnerMovedAway_PetStepsTowardItsNewPlace()
    {
        PetTrack pet = PetFollow.Place(100, 50, Down);

        PetTrack after = PetFollow.Step(pet, 110, 50, Right, ownerMoving: true, seconds: 0.05);

        after.Walking.Should().BeTrue();
        after.X.Should().BeLessThan(pet.X);
        after.Y.Should().BeGreaterThan(pet.Y);
        float remaining = System.MathF.Sqrt(((96 - after.X) * (96 - after.X)) + ((50 - after.Y) * (50 - after.Y)));
        float before = System.MathF.Sqrt(((96 - pet.X) * (96 - pet.X)) + ((50 - pet.Y) * (50 - pet.Y)));
        remaining.Should().BeLessThan(before);
    }

    [Fact]
    public void Step_LongSpan_ReachesThePlaceWithoutOvershooting()
    {
        PetTrack pet = PetFollow.Place(100, 50, Down);

        PetTrack after = PetFollow.Step(pet, 110, 50, Right, ownerMoving: true, seconds: 10);

        after.X.Should().BeApproximately(96, 0.001f);
        after.Y.Should().BeApproximately(50, 0.001f);
    }

    [Fact]
    public void Step_PetAlreadyArrived_StopsWalking()
    {
        PetTrack pet = PetFollow.Place(100, 50, Down);

        PetTrack after = PetFollow.Step(pet, 100, 50, Down, ownerMoving: true, seconds: 0.1);

        after.Walking.Should().BeFalse();
        after.WalkSeconds.Should().Be(0);
    }

    [Fact]
    public void Step_PetFarBehindAfterAMapChange_IsPlacedAtOnce()
    {
        PetTrack pet = new(0, 0, Down, 0, false, 0);

        PetTrack after = PetFollow.Step(pet, 500, 500, Down, ownerMoving: true, seconds: 0.016);

        after.X.Should().Be(500);
        after.Y.Should().Be(500 - PetFollow.TrailDistance);
        after.Walking.Should().BeFalse();
    }

    [Fact]
    public void Step_OwnerMoves_ResetsIdleTime()
    {
        PetTrack pet = new(100, 36, Down, 1.0, false, 0);

        PetTrack after = PetFollow.Step(pet, 100, 50, Down, ownerMoving: true, seconds: 0.25);

        after.IdleSeconds.Should().Be(0);
    }

    [Fact]
    public void Step_OwnerStill_AccumulatesIdleTime()
    {
        PetTrack pet = new(100, 36, Down, 1.0, false, 0);

        PetTrack after = PetFollow.Step(pet, 100, 50, Down, ownerMoving: false, seconds: 0.25);

        after.IdleSeconds.Should().BeApproximately(1.25, 0.0001);
    }

    [Fact]
    public void IsSitting_OwnerStillLongEnoughAndPetArrived_IsTrue()
    {
        PetTrack pet = new(100, 36, Down, PetFollow.SitAfterIdleSeconds, false, 0);

        PetFollow.IsSitting(pet).Should().BeTrue();
    }

    [Fact]
    public void IsSitting_OwnerStillTooShort_IsFalse()
    {
        PetTrack pet = new(100, 36, Down, PetFollow.SitAfterIdleSeconds - 0.01, false, 0);

        PetFollow.IsSitting(pet).Should().BeFalse();
    }

    [Fact]
    public void IsSitting_PetStillWalking_IsFalse()
    {
        PetTrack pet = new(100, 36, Down, 10, true, 0);

        PetFollow.IsSitting(pet).Should().BeFalse();
    }

    [Fact]
    public void PoseOf_SleepingWhileSitting_ReturnsSleep()
    {
        PetTrack pet = new(100, 36, Down, 10, false, 0);

        PetFollow.PoseOf(pet, sleeping: true).Should().Be(PetPose.Sleep);
    }

    [Fact]
    public void PoseOf_OwnerMovingAndPetWalking_ReturnsWalk()
    {
        PetTrack pet = new(100, 36, Down, 0, true, 0.2);

        PetFollow.PoseOf(pet, sleeping: false).Should().Be(PetPose.Walk);
    }

    [Fact]
    public void PoseOf_PetArrivedAndOwnerRecentlyMoved_ReturnsStand()
    {
        PetTrack pet = new(100, 36, Down, 0.2, false, 0);

        PetFollow.PoseOf(pet, sleeping: false).Should().Be(PetPose.Stand);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void IsSleeping_InFarmhouseAtNight_OnlyWhenBoth(bool inFarmhouse, bool night, bool expected)
    {
        PetFollow.IsSleeping(inFarmhouse, night).Should().Be(expected);
    }

    [Theory]
    [InlineData(19 * 60 + 59, false)]
    [InlineData(20 * 60, true)]
    [InlineData(5 * 60 + 59, true)]
    [InlineData(6 * 60, false)]
    [InlineData(12 * 60, false)]
    public void IsNight_GameMinute_IsNightFromTwentyToSix(int minuteOfDay, bool expected)
    {
        PetFollow.IsNight(minuteOfDay).Should().Be(expected);
    }

    [Theory]
    [InlineData(PetPose.Stand, 0, 0, 0, false)]
    [InlineData(PetPose.Stand, 1, 0, 1, false)]
    [InlineData(PetPose.Stand, 3, 0, 1, true)]
    [InlineData(PetPose.Stand, 2, 0, 2, false)]
    [InlineData(PetPose.Sit, 2, 0, 3, false)]
    [InlineData(PetPose.Sleep, 2, 1, 3, false)]
    public void FrameOf_PoseAndFacing_PicksTheSheetCell(PetPose pose, byte facing, int column, int row, bool flip)
    {
        PetFrame frame = PetFollow.FrameOf(pose, facing, 0);

        frame.Column.Should().Be(column);
        frame.Row.Should().Be(row);
        frame.FlipX.Should().Be(flip);
    }

    [Fact]
    public void FrameOf_Walking_CyclesTheWalkColumns()
    {
        PetFrame first = PetFollow.FrameOf(PetPose.Walk, Down, 0.13);
        PetFrame fourth = PetFollow.FrameOf(PetPose.Walk, Down, 0.5);

        first.Column.Should().Be(1);
        fourth.Column.Should().Be(0);
    }
}
