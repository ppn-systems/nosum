// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using Nalix.Network.Options;

namespace Nosum.Host;

/// <summary>
/// Per-address connection and handshake limits for production (docs/adr/ADR-032 D6), through Nalix's <c>ConnectionQuotaOptions</c> (the connection guard).
/// The defaults leave room for a classroom behind one NAT; each can be changed by an environment variable. Loopback is exempt unless
/// <c>NOSUM_ABUSE_ENFORCE_LOOPBACK=true</c>: behind a reverse proxy on the same host, either forward the real client address or turn the exemption off.
/// </summary>
internal static class AbuseLimits
{
    /// <summary>Whether loopback addresses are limited like any other (the default is no, for local development and tests).</summary>
    /// <returns><see langword="true"/> when <c>NOSUM_ABUSE_ENFORCE_LOOPBACK</c> is <c>true</c>.</returns>
    internal static bool EnforceLoopback()
        => string.Equals(Environment.GetEnvironmentVariable("NOSUM_ABUSE_ENFORCE_LOOPBACK"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Applies the limits to the connection guard's options.</summary>
    /// <param name="options">The options to fill.</param>
    internal static void Apply(ConnectionQuotaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MaxConnectionsPerIpAddress = Read("NOSUM_MAX_CONNECTIONS_PER_IP", 20);
        options.MaxConnectionsPerWindow = Read("NOSUM_MAX_CONNECTIONS_PER_WINDOW", 20);
        options.ConnectionRateWindow = TimeSpan.FromSeconds(5);
        options.MaxConnectionsPerSubnet = Read("NOSUM_MAX_CONNECTIONS_PER_SUBNET", 200);
        options.MaxSubnetConnectionsPerWindow = Read("NOSUM_MAX_SUBNET_CONNECTIONS_PER_WINDOW", 200);
        options.ExemptLoopback = !EnforceLoopback();
    }

    private static int Read(string name, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : fallback;
}
