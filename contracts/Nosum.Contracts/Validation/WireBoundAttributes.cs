// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Contracts.Validation;

/// <summary>
/// Declares the longest accepted value of a client-supplied <see cref="string"/> (in characters) or collection (in entries).
/// The server checks it before the handler runs; a longer value drops the packet.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class WireMaxLengthAttribute : Attribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="maxLength">Longest accepted length or count.</param>
    public WireMaxLengthAttribute(int maxLength) => this.MaxLength = maxLength;

    /// <summary>Gets the longest accepted length or count.</summary>
    public int MaxLength { get; }
}

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

/// <summary>Declares that a client-supplied enum must hold a value the enum defines.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class WireDefinedAttribute : Attribute
{
}
