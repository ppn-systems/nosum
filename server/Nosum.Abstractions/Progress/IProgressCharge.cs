// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.Abstractions.Progress;

/// <summary>
/// Energy and experience of one tool use, charged inside the unit of work that carries the tool's effect. Holds the player's progress lock until it is disposed,
/// so dispose it after the unit saved (or after the effect was refused).
/// </summary>
public interface IProgressCharge : IDisposable
{
    /// <summary>Gets whether the player had the energy. When <see langword="false"/> nothing was changed and the tool use must be refused.</summary>
    bool Allowed { get; }

    /// <summary>Tells the player the new energy, experience and any level reached. Call it after the unit of work was saved.</summary>
    /// <param name="cancellationToken">Cancels the pushes.</param>
    /// <returns>A task that completes when the pushes were sent.</returns>
    ValueTask PublishAsync(CancellationToken cancellationToken = default);
}
