// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Presence;
using Nalix.Runtime.Groups;
using Nosum.Core;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Movement;
using Nosum.Infrastructure.Accounts;
using Nosum.Infrastructure.Persistence;
using Nosum.Infrastructure.Presence;
using Nosum.Infrastructure.Progress;
using Xunit;
using DomainPlayerState = Nosum.Domain.Accounts.PlayerState;

namespace Nosum.IntegrationTests.Server;

/// <summary>
/// The order of a session end (docs/adr/ADR-035 D4, ADR-087 D1): the leave saves the place while the player's copy is held, the end that lets the copy go
/// runs after the save, and a sign-in that comes while an end is still running waits for it. A late end never drops the copy of a session that already signed in.
/// </summary>
public sealed class SessionEndOrderTests : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset s_start = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _database;
    private readonly EfUnitOfWorkFactory _units;
    private readonly PlayerStateCache _cache = new();
    private readonly PlayerStateService _places;

    public SessionEndOrderTests(PostgresFixture database)
    {
        _database = database;
        _units = new EfUnitOfWorkFactory(database.Options);
        _places = new PlayerStateService(_units, new FakeTimeProvider(s_start), _cache);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Connection doubles hold nothing to dispose.")]
    public async Task SignIn_WhileAnEndIsStillRunning_WaitsForTheEndAndKeepsItsCopy()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _places.LoadAsync(player, CancellationToken.None);

        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IMapPresence presence = DispatchProxy.Create<IMapPresence, ScriptedPresence>();
        ((ScriptedPresence)(object)presence).OnLeave = _ => _places.LeaveAsync(player, new PlayerStateRow("Farm", 10, 20, Facing.Down), CancellationToken.None);
        SessionRegistry registry = new(presence);

        // A slow session-end handler (it stands for any handler that takes its time), then the copy's eviction as Program.Services wires it.
        registry.SessionEnded += _id =>
        {
            _ = entered.TrySetResult();
            return new ValueTask(release.Task);
        };
        registry.SessionEnded += id =>
        {
            _cache.Forget(id);
            return ValueTask.CompletedTask;
        };

        IConnection first = Connection(1);
        IConnection second = Connection(2);
        IConnection third = Connection(3);

        await registry.ActivateAsync(player, first);
        registry.Release(first);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Two sign-ins arrive while the end waits. Each one must wait for it; the second must not see the end as already gone.
        Task secondSignIn = registry.ActivateAsync(player, second).AsTask();
        Task thirdSignIn = registry.ActivateAsync(player, third).AsTask();
        thirdSignIn.IsCompleted.Should().BeFalse("a sign-in that comes while the end is still running waits for it");

        release.SetResult();
        await Task.WhenAll(secondSignIn, thirdSignIn).WaitAsync(TimeSpan.FromSeconds(10));

        // The new session reads its place after the end. Its copy must survive: the late end has already run.
        PlayerStateRow? place = await _places.LoadAsync(player, CancellationToken.None);
        place.Should().NotBeNull();
        _cache.DescribeHeld(player).Should().NotBeNull("the copy the new session read is not dropped by the earlier end");
        (await RowAsync(player)).MapName.Should().Be("Farm", "the leave's save reached the row");
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Connection doubles hold nothing to dispose.")]
    public async Task SessionEnd_WhileTheLeaveOfTheSameSocketIsSaving_WaitsForTheSave()
    {
        Guid player = await CreatePlayerAsync();
        _ = await _places.LoadAsync(player, CancellationToken.None);

        // The socket's close event and the session's release both leave the same connection. The first leave is held inside its save.
        MapPresence presence = new(new InMemoryGroupStore());
        TaskCompletionSource saving = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        presence.PlayerLeft += async _leaving =>
        {
            await _places.LeaveAsync(player, new PlayerStateRow("Farm", 10, 20, Facing.Down), CancellationToken.None);
            _ = saving.TrySetResult();
            await release.Task;
        };

        SessionRegistry registry = new(presence);
        TaskCompletionSource sessionEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.SessionEnded += id =>
        {
            _cache.Forget(id);
            _ = sessionEnded.TrySetResult();
            return ValueTask.CompletedTask;
        };

        IConnection connection = Connection(7);
        _ = await presence.JoinAsync(connection, player, MapInstance.Shared("Farm"), new MotionState(10, 20, 0));
        await registry.ActivateAsync(player, connection);

        Task firstLeave = presence.LeaveAsync(connection).AsTask();
        await saving.Task.WaitAsync(TimeSpan.FromSeconds(10));
        registry.Release(connection);

        // While the save is held, the session end must not run: it would drop the copy the save is still using.
        await FluentActions.Awaiting(() => sessionEnded.Task.WaitAsync(TimeSpan.FromMilliseconds(300)))
            .Should().ThrowAsync<TimeoutException>("the session end waits for the save of the same socket's leave");

        release.SetResult();
        await firstLeave.WaitAsync(TimeSpan.FromSeconds(10));
        await sessionEnded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await RowAsync(player)).MapName.Should().Be("Farm", "the save reached the row before the end");
    }

    private static IConnection Connection(ulong id)
    {
        NumberedConnection proxy = (NumberedConnection)(object)DispatchProxy.Create<IConnection, NumberedConnection>();
        proxy.Id = id;
        return (IConnection)(object)proxy;
    }

    private async Task<DomainPlayerState> RowAsync(Guid player)
    {
        await using NosumDbContext db = new(_database.Options);
        return await db.PlayerStates.AsNoTracking().SingleAsync(s => s.PlayerId == player);
    }

    private async Task<Guid> CreatePlayerAsync()
    {
        IUnitOfWork unit = _units.Create();
        await using (unit)
        {
            Player player = TestNicknames.Name(new() { IsGuest = true, CreatedAt = s_start, LastActiveAt = s_start });
            unit.Repository<Player>().Add(player);
            unit.Repository<DomainPlayerState>().Add(new DomainPlayerState { PlayerId = player.Id, MapName = "Farm", Energy = 40, EnergyRefilledDay = 5, FarmingXp = 0, UpdatedAt = s_start });
            _ = await unit.SaveChangesAsync(CancellationToken.None);
            return player.Id;
        }
    }

    /// <summary>A connection with its own id; every other member does nothing.</summary>
    internal class NumberedConnection : DispatchProxy
    {
        public ulong Id { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_ConnectionId")
            {
                return this.Id;
            }

            return targetMethod is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }

    /// <summary>Presence whose leave runs a scripted action; every other member does nothing.</summary>
    internal class ScriptedPresence : DispatchProxy
    {
        public Func<IConnection, Task>? OnLeave { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IMapPresence.LeaveAsync))
            {
                if (this.OnLeave is { } leave)
                {
                    return new ValueTask(leave((IConnection)args![0]!));
                }

                return ValueTask.CompletedTask;
            }

            return targetMethod is { ReturnType: { IsValueType: true } type } && type != typeof(void) ? Activator.CreateInstance(type) : null;
        }
    }
}
