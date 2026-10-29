// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Coops;

/// <summary>
/// The shared ledger of one co-op objective (docs/adr/ADR-067 D2): what the members have harvested so far, and when the co-op earned the trophy. The co-op keeps
/// its ledger on its own row, keyed by objective id, so the co-op view reads it with the row it already reads.
/// </summary>
/// <param name="Contributed">The total harvested by every member.</param>
/// <param name="CompletedAt">When the trophy was earned; <see langword="null"/> while the objective is open.</param>
public sealed record CoopObjectiveProgress(int Contributed, DateTimeOffset? CompletedAt);
