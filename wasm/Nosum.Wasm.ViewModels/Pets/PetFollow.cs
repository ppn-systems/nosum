// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.ViewModels.Pets;

/// <summary>
/// Where a pet is and how it looks this frame (docs/adr/ADR-071 D6). The pet keeps its own position, facing and the time it has been idle or walking;
/// everything is in world pixels and seconds, so the follow is pure and testable.
/// </summary>
/// <param name="X">Feet x, world pixels.</param>
/// <param name="Y">Feet y, world pixels.</param>
/// <param name="Facing">Facing: 0 down, 1 right, 2 up, 3 left (the farmer's facing).</param>
/// <param name="IdleSeconds">How long the owner has not moved, seconds.</param>
/// <param name="Walking">Whether the pet is still stepping towards its place this frame.</param>
/// <param name="WalkSeconds">Walk animation clock, seconds, advanced only while walking.</param>
public readonly record struct PetTrack(float X, float Y, byte Facing, double IdleSeconds, bool Walking, double WalkSeconds);

/// <summary>The pose a pet is drawn in.</summary>
public enum PetPose
{
    /// <summary>Standing still, facing its last direction.</summary>
    Stand = 0,

    /// <summary>Stepping along the trail.</summary>
    Walk = 1,

    /// <summary>Sitting: the owner has been still for a while.</summary>
    Sit = 2,

    /// <summary>Asleep: the owner is in the farmhouse at night.</summary>
    Sleep = 3,
}

/// <summary>A frame of the pet sheet: column and row of a 16 x 16 cell, and whether it is mirrored.</summary>
/// <param name="Column">Column, 0 to 3.</param>
/// <param name="Row">Row, 0 down, 1 right, 2 up, 3 sitting and sleeping.</param>
/// <param name="FlipX">Whether the right-facing row is drawn mirrored (facing left).</param>
public readonly record struct PetFrame(int Column, int Row, bool FlipX);

/// <summary>
/// The pet's follow, sit, sleep and frame decisions (docs/adr/ADR-071 D6). A pet trails its owner by <see cref="TrailDistance"/> behind the owner's facing,
/// walks at <see cref="PetSpeed"/> (a little faster than the farmer's walk, so it catches up), stops on its place, sits when the owner has been still for
/// <see cref="SitAfterIdleSeconds"/>, and sleeps while the owner is in the farmhouse at night. Pure: no Blazor, no time source.
/// </summary>
public static class PetFollow
{
    /// <summary>How far behind the owner the pet stands, world pixels.</summary>
    public const float TrailDistance = 14f;

    /// <summary>The pet's walk speed, world pixels per second; the farmer walks at 80.</summary>
    public const float PetSpeed = 92f;

    /// <summary>Closer than this to its place the pet has arrived, world pixels.</summary>
    public const float ArriveDistance = 1f;

    /// <summary>A pet this far behind (after a map change or a join) is placed at its place at once, world pixels.</summary>
    public const float TeleportDistance = 120f;

    /// <summary>How long the owner must stand still before the pet sits, seconds.</summary>
    public const double SitAfterIdleSeconds = 1.5;

    /// <summary>Seconds per walk frame of the pet's animation.</summary>
    public const double WalkFrameSeconds = 0.12;

    /// <summary>The first local hour of the night (20:00), minute of the game day.</summary>
    public const int NightStartMinute = 20 * 60;

    /// <summary>The local hour the night ends (06:00), minute of the game day.</summary>
    public const int NightEndMinute = 6 * 60;

    /// <summary>Places a new pet at its place behind the owner, standing, with no idle time.</summary>
    /// <param name="ownerX">Owner feet x, world pixels.</param>
    /// <param name="ownerY">Owner feet y, world pixels.</param>
    /// <param name="ownerFacing">Owner facing, 0 to 3.</param>
    /// <returns>The pet.</returns>
    public static PetTrack Place(float ownerX, float ownerY, byte ownerFacing)
    {
        (float x, float y) = PlaceOf(ownerX, ownerY, ownerFacing);
        return new PetTrack(x, y, ownerFacing, 0, false, 0);
    }

    /// <summary>The point a pet stands at: <see cref="TrailDistance"/> behind the owner, opposite to the owner's facing.</summary>
    /// <param name="ownerX">Owner feet x, world pixels.</param>
    /// <param name="ownerY">Owner feet y, world pixels.</param>
    /// <param name="ownerFacing">Owner facing, 0 down, 1 right, 2 up, 3 left.</param>
    /// <returns>The point, world pixels.</returns>
    public static (float X, float Y) PlaceOf(float ownerX, float ownerY, byte ownerFacing) => ownerFacing switch
    {
        1 => (ownerX - TrailDistance, ownerY),
        2 => (ownerX, ownerY + TrailDistance),
        3 => (ownerX + TrailDistance, ownerY),
        _ => (ownerX, ownerY - TrailDistance),
    };

    /// <summary>Advances a pet by a span of time: it steps towards its place, or stays when it has arrived; the idle time follows the owner.</summary>
    /// <param name="pet">The pet before.</param>
    /// <param name="ownerX">Owner feet x, world pixels.</param>
    /// <param name="ownerY">Owner feet y, world pixels.</param>
    /// <param name="ownerFacing">Owner facing, 0 to 3.</param>
    /// <param name="ownerMoving">Whether the owner walked this frame.</param>
    /// <param name="seconds">Time since the last step, seconds; never negative.</param>
    /// <returns>The pet after the step.</returns>
    public static PetTrack Step(PetTrack pet, float ownerX, float ownerY, byte ownerFacing, bool ownerMoving, double seconds)
    {
        double dt = Math.Max(0, seconds);
        double idle = ownerMoving ? 0 : pet.IdleSeconds + dt;
        (float targetX, float targetY) = PlaceOf(ownerX, ownerY, ownerFacing);
        float dx = targetX - pet.X;
        float dy = targetY - pet.Y;
        float distance = MathF.Sqrt((dx * dx) + (dy * dy));

        if (distance > TeleportDistance)
        {
            return new PetTrack(targetX, targetY, ownerFacing, idle, false, 0);
        }

        if (distance <= ArriveDistance)
        {
            return new PetTrack(targetX, targetY, pet.Facing, idle, false, 0);
        }

        float step = PetSpeed * (float)dt;
        float x = step >= distance ? targetX : pet.X + (dx / distance * step);
        float y = step >= distance ? targetY : pet.Y + (dy / distance * step);
        byte facing = FacingOf(dx, dy, pet.Facing);
        double walk = pet.WalkSeconds + dt;
        return new PetTrack(x, y, facing, idle, true, walk);
    }

    /// <summary>Whether the owner is inside the farmhouse at night: the pet sleeps.</summary>
    /// <param name="inFarmhouse">Whether the owner's map is the farmhouse.</param>
    /// <param name="night">Whether it is night on the game clock.</param>
    /// <returns><see langword="true"/> when the pet sleeps.</returns>
    public static bool IsSleeping(bool inFarmhouse, bool night) => inFarmhouse && night;

    /// <summary>Whether a game minute of the day is night.</summary>
    /// <param name="minuteOfDay">Minute of the game day, 0 to 1439.</param>
    /// <returns><see langword="true"/> from 20:00 to 06:00.</returns>
    public static bool IsNight(int minuteOfDay) => minuteOfDay >= NightStartMinute || minuteOfDay < NightEndMinute;

    /// <summary>Whether the pet is sitting: it has arrived and the owner has been still long enough.</summary>
    /// <param name="pet">The pet.</param>
    /// <returns><see langword="true"/> when the pet sits.</returns>
    public static bool IsSitting(PetTrack pet) => !pet.Walking && pet.IdleSeconds >= SitAfterIdleSeconds;

    /// <summary>The pose of a pet this frame. Sleeping wins over sitting, sitting over walking.</summary>
    /// <param name="pet">The pet.</param>
    /// <param name="sleeping">Whether it sleeps (see <see cref="IsSleeping"/>).</param>
    /// <returns>The pose.</returns>
    public static PetPose PoseOf(PetTrack pet, bool sleeping)
    {
        if (sleeping)
        {
            return PetPose.Sleep;
        }

        if (IsSitting(pet))
        {
            return PetPose.Sit;
        }

        return pet.Walking ? PetPose.Walk : PetPose.Stand;
    }

    /// <summary>The frame of the pet sheet for a pose: rows down, right, up (left is the right row mirrored), row 3 sitting and sleeping.</summary>
    /// <param name="pose">The pose.</param>
    /// <param name="facing">Facing, 0 to 3.</param>
    /// <param name="walkSeconds">The walk animation clock.</param>
    /// <returns>The frame.</returns>
    public static PetFrame FrameOf(PetPose pose, byte facing, double walkSeconds)
    {
        switch (pose)
        {
            case PetPose.Sit:
                return new PetFrame(0, 3, false);
            case PetPose.Sleep:
                return new PetFrame(1, 3, false);
        }

        int row = facing == 3 ? 1 : facing & 3;
        bool flip = facing == 3;
        int column = pose == PetPose.Walk ? (int)Math.Floor(walkSeconds / WalkFrameSeconds) % 4 : 0;
        return new PetFrame(column, row, flip);
    }

    // The direction of the last step: the dominant axis of the move (y grows downwards, so a step down faces 0).
    private static byte FacingOf(float dx, float dy, byte previous)
    {
        if (MathF.Abs(dx) > MathF.Abs(dy))
        {
            return dx > 0 ? (byte)1 : (byte)3;
        }

        if (MathF.Abs(dy) > 0)
        {
            return dy > 0 ? (byte)0 : (byte)2;
        }

        return previous;
    }
}
