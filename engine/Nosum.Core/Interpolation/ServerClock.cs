// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Core.Interpolation;

/// <summary>
/// Maps the local clock onto a remote clock that has an unknown offset, so samples stamped by the
/// remote side can be placed on the local timeline. Each observation says "remote time S arrived
/// at local time L"; the offset L - S is the true offset plus the packet's delay. The smallest
/// delay seen is the best estimate, so the offset follows new minimums at once and drifts toward
/// larger values slowly.
/// </summary>
public struct ServerClock
{
    private const double DriftRate = 0.02;

    private double _offset;

    /// <summary>Gets whether at least one observation was made.</summary>
    public bool HasSync { get; private set; }

    /// <summary>Records that remote time <paramref name="remoteSeconds"/> arrived at local time <paramref name="localSeconds"/>.</summary>
    /// <param name="remoteSeconds">Remote timestamp, seconds.</param>
    /// <param name="localSeconds">Local time of arrival, seconds.</param>
    public void Observe(double remoteSeconds, double localSeconds)
    {
        double candidate = localSeconds - remoteSeconds;
        if (!this.HasSync || candidate < _offset)
        {
            _offset = candidate;
            this.HasSync = true;
        }
        else
        {
            _offset += (candidate - _offset) * DriftRate;
        }
    }

    /// <summary>Estimates the remote clock at a local time.</summary>
    /// <param name="localSeconds">Local time, seconds.</param>
    /// <returns>Remote time, seconds; meaningless until <see cref="HasSync"/>.</returns>
    public readonly double RemoteNow(double localSeconds) => localSeconds - _offset;
}
