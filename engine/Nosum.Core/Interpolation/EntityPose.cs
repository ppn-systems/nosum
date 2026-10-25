// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Core.Interpolation;

/// <summary>An entity's interpolated pose at one render time.</summary>
/// <param name="X">World x.</param>
/// <param name="Y">World y.</param>
/// <param name="State">The caller's discrete state (facing, animation flags) taken from the older of the two samples.</param>
/// <param name="Held">
/// <see langword="true"/> when the render time lies past the newest sample, so the pose is the newest
/// sample held in place (no extrapolation).
/// </param>
public readonly record struct EntityPose(float X, float Y, int State, bool Held);
