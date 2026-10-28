// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.Game;

/// <summary>
/// Frame-scoped timing snapshot passed to <see cref="IMapDecorator"/>s so animations and visual effects
/// read a consistent monotonic clock without querying wall-clock APIs independently.
/// </summary>
/// <param name="LocalMs">Local monotonic tick count in milliseconds.</param>
/// <param name="ServerMs">Estimated server clock in milliseconds, or <paramref name="LocalMs"/> when offline.</param>
/// <param name="TotalSeconds">Accumulated scene simulation time in seconds.</param>
internal readonly record struct FrameClock(long LocalMs, long ServerMs, double TotalSeconds);
