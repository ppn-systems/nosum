// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;

namespace Nosum.Core.Interpolation;

/// <summary>
/// Remembers the last few timestamped positions of many remote entities and answers "where was
/// entity N at time t". Rendering a little in the past (see <see cref="ServerClock"/>) lets the
/// buffer interpolate between two real samples instead of guessing. Not tied to any game object:
/// ids, a time, a position and one integer of discrete state are all it knows.
/// </summary>
/// <remarks>
/// All storage is allocated in the constructor; no method allocates. Calls are serialized with a
/// lock, so a socket callback may <see cref="Push"/> while the frame loop calls
/// <see cref="TrySample"/> (single-threaded WebAssembly never contends).
/// </remarks>
public sealed class SnapshotBuffer
{
    private readonly Lock _gate = new();
    private readonly int _maxEntities;
    private readonly int _samples;
    private readonly uint[] _ids;
    private readonly int[] _counts;
    private readonly double[] _times;
    private readonly float[] _xs;
    private readonly float[] _ys;
    private readonly int[] _states;
    private int _entityCount;

    /// <summary>Initializes an empty buffer.</summary>
    /// <param name="maxEntities">Most entities tracked at once; further ones are ignored.</param>
    /// <param name="samplesPerEntity">Samples kept per entity (at least 2).</param>
    public SnapshotBuffer(int maxEntities, int samplesPerEntity = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntities, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerEntity, 2);

        _maxEntities = maxEntities;
        _samples = samplesPerEntity;
        _ids = new uint[maxEntities];
        _counts = new int[maxEntities];
        _times = new double[maxEntities * samplesPerEntity];
        _xs = new float[maxEntities * samplesPerEntity];
        _ys = new float[maxEntities * samplesPerEntity];
        _states = new int[maxEntities * samplesPerEntity];
    }

    /// <summary>Gets the number of tracked entities.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entityCount;
            }
        }
    }

    /// <summary>Returns the id of the entity at an index below <see cref="Count"/>; the order changes when entities are removed.</summary>
    /// <param name="index">Index, 0 to <see cref="Count"/> - 1.</param>
    /// <returns>The entity id.</returns>
    public uint IdAt(int index)
    {
        lock (_gate)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _entityCount);
            return _ids[index];
        }
    }

    /// <summary>Adds a sample. A sample not newer than the entity's newest one is dropped.</summary>
    /// <param name="id">Entity id; an unknown id starts tracking the entity.</param>
    /// <param name="time">Sample time in seconds on one consistent clock.</param>
    /// <param name="x">World x.</param>
    /// <param name="y">World y.</param>
    /// <param name="state">Discrete state, for example a facing and an animation flag packed into an integer.</param>
    public void Push(uint id, double time, float x, float y, int state)
    {
        lock (_gate)
        {
            int e = this.Find(id);
            if (e < 0)
            {
                if (_entityCount == _maxEntities)
                {
                    return;
                }

                e = _entityCount++;
                _ids[e] = id;
                _counts[e] = 0;
            }

            int baseIndex = e * _samples;
            int n = _counts[e];
            if (n > 0 && time <= _times[baseIndex + n - 1])
            {
                return;
            }

            if (n == _samples)
            {
                Array.Copy(_times, baseIndex + 1, _times, baseIndex, n - 1);
                Array.Copy(_xs, baseIndex + 1, _xs, baseIndex, n - 1);
                Array.Copy(_ys, baseIndex + 1, _ys, baseIndex, n - 1);
                Array.Copy(_states, baseIndex + 1, _states, baseIndex, n - 1);
                n--;
            }

            _times[baseIndex + n] = time;
            _xs[baseIndex + n] = x;
            _ys[baseIndex + n] = y;
            _states[baseIndex + n] = state;
            _counts[e] = n + 1;
        }
    }

    /// <summary>Stops tracking an entity.</summary>
    /// <param name="id">Entity id.</param>
    /// <returns><see langword="true"/> when it was tracked.</returns>
    public bool Remove(uint id)
    {
        lock (_gate)
        {
            int e = this.Find(id);
            if (e < 0)
            {
                return false;
            }

            int last = --_entityCount;
            if (e != last)
            {
                _ids[e] = _ids[last];
                _counts[e] = _counts[last];
                Array.Copy(_times, last * _samples, _times, e * _samples, _samples);
                Array.Copy(_xs, last * _samples, _xs, e * _samples, _samples);
                Array.Copy(_ys, last * _samples, _ys, e * _samples, _samples);
                Array.Copy(_states, last * _samples, _states, e * _samples, _samples);
            }

            _counts[last] = 0;
            return true;
        }
    }

    /// <summary>Stops tracking every entity.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entityCount = 0;
        }
    }

    /// <summary>Computes an entity's pose at a time.</summary>
    /// <param name="id">Entity id.</param>
    /// <param name="time">Render time on the same clock as the samples.</param>
    /// <param name="pose">The pose when the entity is tracked.</param>
    /// <returns><see langword="true"/> when the entity has at least one sample.</returns>
    public bool TrySample(uint id, double time, out EntityPose pose)
    {
        lock (_gate)
        {
            int e = this.Find(id);
            int n = e < 0 ? 0 : _counts[e];
            if (n == 0)
            {
                pose = default;
                return false;
            }

            int b = e * _samples;
            if (time <= _times[b])
            {
                pose = new EntityPose(_xs[b], _ys[b], _states[b], Held: false);
                return true;
            }

            int last = b + n - 1;
            if (time >= _times[last])
            {
                pose = new EntityPose(_xs[last], _ys[last], _states[last], Held: time > _times[last]);
                return true;
            }

            int i = b;
            while (_times[i + 1] <= time)
            {
                i++;
            }

            float t = (float)((time - _times[i]) / (_times[i + 1] - _times[i]));
            pose = new EntityPose(
                _xs[i] + ((_xs[i + 1] - _xs[i]) * t),
                _ys[i] + ((_ys[i + 1] - _ys[i]) * t),
                _states[i],
                Held: false);
            return true;
        }
    }

    private int Find(uint id)
    {
        for (int i = 0; i < _entityCount; i++)
        {
            if (_ids[i] == id)
            {
                return i;
            }
        }

        return -1;
    }
}
