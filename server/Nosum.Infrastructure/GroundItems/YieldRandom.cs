// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading;
using Nosum.Abstractions.GroundItems;
using Nosum.Gameplay.GroundItems;

namespace Nosum.Infrastructure.GroundItems;

/// <summary><see cref="IYieldRandom"/> from one server seed; thread-safe.</summary>
public sealed class YieldRandom : IYieldRandom
{
    private readonly ulong _seed;
    private long _counter;

    /// <summary>Initializes the source.</summary>
    /// <param name="seed">The server seed; a fixed value makes every roll reproducible.</param>
    public YieldRandom(ulong seed) => _seed = seed;

    /// <inheritdoc/>
    public GameRandom Create(ulong salt = 0)
        => new(GameRandom.Mix(GameRandom.Mix(_seed, salt), (ulong)Interlocked.Increment(ref _counter)));
}
