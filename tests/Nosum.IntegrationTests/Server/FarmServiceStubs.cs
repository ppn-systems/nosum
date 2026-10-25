// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;

namespace Nosum.IntegrationTests.Server;

/// <summary>A node layer with no nodes, for tests that build a <c>FarmService</c> by hand and test only the farm layer.</summary>
internal sealed class NoNodes : IResourceNodeService
{
    public ValueTask PrepareAsync(MapInstance instance, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public WalkGrid GridFor(MapInstance instance, MapDefinition map) => map.Grid;

    public Task<NodeSnapshotView> GetAsync(IConnection connection, CancellationToken cancellationToken = default)
        => Task.FromResult(new NodeSnapshotView(string.Empty, 40, 40, []));

    public Task<FarmUseOutcome?> TryUseAsync(NodeUseRequest request, CancellationToken cancellationToken = default) => Task.FromResult<FarmUseOutcome?>(null);

    public Task AdvanceDayAsync(long day, bool seasonChanged, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Tool state without the swing cooldown and with an inexhaustible can, so a test can swing as fast as it likes.</summary>
internal sealed class NoCooldownTools : IToolState
{
    public bool TryBeginSwing(Guid playerId) => true;

    public int WaterLeft(Guid playerId) => 40;

    public ValueTask<int> UseWaterAsync(Guid playerId, CancellationToken cancellationToken = default) => ValueTask.FromResult(39);

    public ValueTask<int> RefillAsync(Guid playerId, CancellationToken cancellationToken = default) => ValueTask.FromResult(40);
}
