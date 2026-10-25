// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Framework.Injection;
using Nalix.Hosting;
using Nalix.Hosting.Protocols;
using Nalix.Runtime.Groups;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Gameplay.Farm;
using Nosum.Handlers.Player;
using Nosum.Infrastructure.Maps;
using Nosum.Infrastructure.Player;
using Nosum.Infrastructure.Presence;

namespace Nosum.Host;

/// <summary>
/// Entry point for the Nosum realtime server. Every client connects over one WebSocket listener.
/// </summary>
internal static class Program
{
    /// <summary>WebSocket port; the client's transport options must use the same port.</summary>
    private const ushort WebSocketPort = 57300;

    /// <summary>WebSocket path; must match the client's <c>WebSocketTransportOptions.Path</c>.</summary>
    internal const string WebSocketPath = "/ws/";

    /// <summary>Id of the farm map, the only map so far.</summary>
    internal const string FarmMapId = "farm";

    /// <summary>Time between map snapshots (10 per second, docs/adr/ADR-008).</summary>
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(100);

    public static async Task<int> Main()
    {
        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        NetworkApplication app = BuildApplication(WebSocketPort);
        await using (app.ConfigureAwait(false))
        {
            await app.RunAsync(shutdown.Token).ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// Builds the Nalix application: services, system handlers, game handlers, then the WebSocket listener.
    /// </summary>
    /// <param name="webSocketPort">Port the WebSocket listener binds to.</param>
    /// <returns>The built, not yet activated, application.</returns>
    internal static NetworkApplication BuildApplication(ushort webSocketPort)
    {
        RegisterServices();

        return NetworkApplication.CreateBuilder()
            .UseGroups()
            .UseTimeSync()
            .UseSystemControl()
            .UseSecureConnections()
            .MapHandlers(typeof(PlayerHandlers))
            .MapWebSocket<DefaultProtocol>()
                .OnPort(webSocketPort)
                .WithPath(WebSocketPath)
                .Bind()
            .Build();
    }

    // Services are registered before the builder so [Inject] fields of the handlers resolve them.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "The player service lives as long as the process; its snapshot loop ends by itself when the last player leaves.")]
    private static void RegisterServices()
    {
        IConnectionGroupRegistry groups = new InMemoryGroupStore();
        InstanceManager.Instance.Register(groups);

        MapPresence presence = new(groups);
        TmxMapCatalog maps = new([new TmxMapSource(FarmMapId, Path.Combine(AppContext.BaseDirectory, "maps", "Farm.tmx"), FarmProps.FarmhouseDoorFront.X, FarmProps.FarmhouseDoorFront.Y, FarmProps.BuildWalkGrid)]);
        PlayerService players = new(presence, maps, TimeProvider.System, SnapshotInterval);

        InstanceManager.Instance.Register<IMapPresence>(presence);
        InstanceManager.Instance.Register<IMapCatalog>(maps);
        InstanceManager.Instance.Register<IPlayerService>(players);
    }
}
