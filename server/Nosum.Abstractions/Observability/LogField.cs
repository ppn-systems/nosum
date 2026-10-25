// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Abstractions.Observability;

/// <summary>One name/value pair of a structured log event.</summary>
/// <param name="Name">The field name, lower snake case.</param>
/// <param name="Value">The value; null, a number, a bool, a string, a <see cref="System.Guid"/> or a time.</param>
public readonly record struct LogField(string Name, object? Value);
