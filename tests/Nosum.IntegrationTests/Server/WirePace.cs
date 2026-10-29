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
    private static readonly SemaphoreSlim s_credentialGate = new(1, 1);
    private static readonly SemaphoreSlim s_joinGate = new(1, 1);
    private static DateTime s_lastJoin = DateTime.MinValue;
    private static DateTime s_lastCredential = DateTime.MinValue;
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

    /// <summary>
    /// Spaces the Google/Microsoft sign-in and link requests: the server allows one per address every five seconds, and a burst on loopback
    /// is locked out for a minute (docs/adr/ADR-069 D7, <c>account-credential</c>).
    /// </summary>
    public static async Task CredentialAsync(CancellationToken ct)
    {
        await s_credentialGate.WaitAsync(ct);
        try
        {
            TimeSpan wait = TimeSpan.FromSeconds(5.2) - (DateTime.UtcNow - s_lastCredential);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            s_lastCredential = DateTime.UtcNow;
        }
        finally
        {
            _ = s_credentialGate.Release();
        }
    }

    /// <summary>Spaces the map joins: the server allows four per address every five seconds (<c>player-join</c>), and the loopback bucket is shared by the whole suite.</summary>
    public static async Task JoinAsync(CancellationToken ct)
    {
        await s_joinGate.WaitAsync(ct);
        try
        {
            TimeSpan wait = TimeSpan.FromSeconds(1.3) - (DateTime.UtcNow - s_lastJoin);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            s_lastJoin = DateTime.UtcNow;
        }
        finally
        {
            _ = s_joinGate.Release();
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
