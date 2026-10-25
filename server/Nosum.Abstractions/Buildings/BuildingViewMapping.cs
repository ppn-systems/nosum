// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Contracts.Building.S2C;

namespace Nosum.Abstractions.Buildings;

/// <summary>Turns the service's views into the rows that go on the wire.</summary>
public static class BuildingViewMapping
{
    /// <summary>Maps a building to its wire row.</summary>
    /// <param name="view">The building.</param>
    /// <returns>The row.</returns>
    public static BuildingRow ToRow(this BuildingView view)
    {
        BuildingRow row = BuildingRow.Create();
        row.Id = view.Id;
        row.ObjectId = view.ObjectId;
        row.Kind = (byte)view.Kind;
        row.MapId = view.MapId;
        row.RemainingMs = view.RemainingMs;
        row.Animals = (byte)Math.Clamp(view.Animals, 0, byte.MaxValue);
        row.Capacity = (byte)Math.Clamp(view.Capacity, 0, byte.MaxValue);
        return row;
    }

    /// <summary>Maps an animal to its wire row.</summary>
    /// <param name="view">The animal.</param>
    /// <returns>The row.</returns>
    public static AnimalRow ToRow(this AnimalView view)
    {
        AnimalRow row = AnimalRow.Create();
        row.Id = view.Id;
        row.Kind = (byte)view.Kind;
        row.Name = view.Name;
        row.Friendship = view.Friendship;
        row.Flags = view.Flags;
        row.X = view.X;
        row.Y = view.Y;
        row.TargetX = view.TargetX;
        row.TargetY = view.TargetY;
        row.MoveMs = view.MoveMs;
        return row;
    }
}
