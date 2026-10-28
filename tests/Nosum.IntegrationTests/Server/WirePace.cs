// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// Shared process-wide rate-limit gates for integration tests in the <see cref="NalixHostSerial"/> collection.
/// Resuming a session is limited to two per second per loopback address, and shop/blacksmith/mine/dev requests
/// share per-address token buckets across consecutive test classes.
/// </summary>
internal static class WirePace
{
    private static readonly SemaphoreSlim s_resumeGate = new(1, 1);
    private static readonly SemaphoreSlim s_requestGate = new(1, 1);
    private static DateTime s_lastResume = DateTime.MinValue;
    private static DateTime s_lastRequest = DateTime.MinValue;

    public static async Task WaitAsync(CancellationToken ct)
    {
        await s_resumeGate.WaitAsync(ct);
        try
        {
            TimeSpan wait = TimeSpan.FromMilliseconds(650) - (DateTime.UtcNow - s_lastResume);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            s_lastResume = DateTime.UtcNow;
        }
        finally
        {
            _ = s_resumeGate.Release();
        }
    }

    public static async Task RequestAsync(CancellationToken ct)
    {
        await s_requestGate.WaitAsync(ct);
        try
        {
            TimeSpan wait = TimeSpan.FromMilliseconds(330) - (DateTime.UtcNow - s_lastRequest);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            s_lastRequest = DateTime.UtcNow;
        }
        finally
        {
            _ = s_requestGate.Release();
        }
    }
}
