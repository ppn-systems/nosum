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
