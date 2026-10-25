// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Framework.Injection;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Admin;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Observability;
using Nosum.Abstractions.Presence;
using Nosum.Infrastructure.Admin;

namespace Nosum.Host.Ops;

/// <summary>Registers the operations services (admin, mute list, live gauges) in one call, so <c>Program</c> stays small (docs/adr/ADR-031).</summary>
internal static class OpsServices
{
    /// <summary>Registers the mute list and the admin service and points the player gauges at the presence service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="sessions">Ends connections for kick and ban.</param>
    /// <param name="transfers">Moves granted items and coins.</param>
    /// <param name="presence">Who is on which map, for the gauges.</param>
    /// <param name="clock">The server clock.</param>
    internal static void Register(IUnitOfWorkFactory unitOfWork, ISessionRegistry sessions, ITransferService transfers, IMapPresence presence, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(clock);

        MuteList mutes = new(clock);
        InstanceManager.Instance.Register<IMuteList>(mutes);
        InstanceManager.Instance.Register(mutes);
        InstanceManager.Instance.Register<IAdminService>(new AdminService(unitOfWork, sessions, mutes, transfers, clock));

        // Private copies of a map (a farm per player) all count under their map id, so the label set stays as small as the map catalogue.
        List<string> keys = [];
        Dictionary<string, int> perMap = [];
        List<PlayerPresence> players = [];
        NosumMetrics.ObservePlayers(
            () => presence.PlayerCount,
            () =>
            {
                lock (keys)
                {
                    keys.Clear();
                    perMap.Clear();
                    presence.CopyActiveMapIds(keys);
                    foreach (string key in keys)
                    {
                        int at = key.IndexOf('@', StringComparison.Ordinal);
                        string map = at < 0 ? key : key[..at];
                        players.Clear();
                        presence.CopyPlayers(key, players);
                        perMap[map] = perMap.GetValueOrDefault(map) + players.Count;
                    }

                    List<Measurement<int>> measurements = new(perMap.Count);
                    foreach ((string map, int count) in perMap)
                    {
                        measurements.Add(new Measurement<int>(count, new KeyValuePair<string, object?>("instance", map)));
                    }

                    return measurements;
                }
            });
    }

    /// <summary>Loads the mutes that are still running from the database; call once after <c>BuildApplication</c>.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the mute list is filled.</returns>
    internal static async Task LoadMutesAsync(CancellationToken cancellationToken)
    {
        MuteList mutes = InstanceManager.Instance.GetExistingInstance<MuteList>()
            ?? throw new InvalidOperationException("OpsServices.Register must run first.");
        IUnitOfWorkFactory unitOfWork = InstanceManager.Instance.GetExistingInstance<IUnitOfWorkFactory>()
            ?? throw new InvalidOperationException("HostServices.Register must run first.");
        await mutes.LoadAsync(unitOfWork, cancellationToken).ConfigureAwait(false);
    }
}
