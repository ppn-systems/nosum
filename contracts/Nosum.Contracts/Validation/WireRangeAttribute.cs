// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Contracts.Validation;

/// <summary>
/// Declares the accepted inclusive range of a client-supplied number. Not-a-number never lies inside a range.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class WireRangeAttribute : Attribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="minimum">Smallest accepted value.</param>
    /// <param name="maximum">Largest accepted value.</param>
    public WireRangeAttribute(double minimum, double maximum)
    {
        this.Minimum = minimum;
        this.Maximum = maximum;
    }

    /// <summary>Gets the smallest accepted value.</summary>
    public double Minimum { get; }

    /// <summary>Gets the largest accepted value.</summary>
    public double Maximum { get; }
}
