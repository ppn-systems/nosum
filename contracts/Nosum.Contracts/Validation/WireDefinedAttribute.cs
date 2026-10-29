// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Contracts.Validation;

/// <summary>Declares that a client-supplied enum must hold a value the enum defines.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class WireDefinedAttribute : Attribute
{
}
