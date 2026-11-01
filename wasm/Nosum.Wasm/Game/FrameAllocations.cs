// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Wasm.Game;

/// <summary>
/// Counts the managed bytes the frame's own work allocates, one call of <see cref="GameLoop.Frame"/> at a time (docs/adr/ADR-097 D1). The
/// counter is read through the tester build's QA hook; a frame that allocates shows up in the frame benchmark (bench/wasm-perf/frame.cjs) and
/// fails its idle gate. Reading the counter allocates nothing.
/// </summary>
internal static class FrameAllocations
{
    private static long s_bytes;
    private static long s_frames;

    /// <summary>The bytes the frame thread allocated, counted from the start of a frame to its end.</summary>
    internal static long Bytes => s_bytes;

    /// <summary>The number of frames counted.</summary>
    internal static long Frames => s_frames;

    /// <summary>Reads the allocation counter at the start of a frame.</summary>
    internal static long Start() => GC.GetAllocatedBytesForCurrentThread();

    /// <summary>Adds the bytes a frame allocated, from the counter read by <see cref="Start"/>.</summary>
    internal static void End(long start)
    {
        s_bytes += GC.GetAllocatedBytesForCurrentThread() - start;
        s_frames++;
    }
}
