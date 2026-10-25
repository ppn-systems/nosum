// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Gameplay.Maps;

namespace Nosum.Abstractions.Placeables;

/// <summary>
/// The lock that serializes changes to the placed objects of one instance. A behaviour that changes state tied to an object (a furnace job) takes it, so it can never
/// interleave with the object being moved or picked up (docs/adr/ADR-024). Take it before any inventory lock, never while already holding one.
/// </summary>
public interface IInstanceLock
{
    /// <summary>Waits for the instance's lock.</summary>
    /// <param name="instance">The place.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The held lock; dispose to release.</returns>
    ValueTask<IDisposable> EnterAsync(MapInstance instance, CancellationToken cancellationToken = default);
}
