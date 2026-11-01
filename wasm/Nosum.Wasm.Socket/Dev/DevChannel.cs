// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;

namespace Nosum.Wasm.Socket.Dev;

/// <summary>A registered test player the Development server befriended with the sender (docs/adr/ADR-105).</summary>
/// <param name="Nickname">The test player's nickname.</param>
/// <param name="SessionToken">The test player's session token; a second client resumes as that player with it.</param>
public sealed record TestFriend(string Nickname, string SessionToken);

/// <summary>
/// Tester tool: asks a Development server to grant items or coins. A server running in Production does not know the opcode and never answers; the
/// request is only sent from a client built with <c>-p:NosumDevTools=true</c> (<c>NOSUM_DEV_TOOLS</c>), so a normal build contains no way to ask.
/// </summary>
public sealed class DevChannel
{
    private readonly IGameSocket _socket;
    private readonly CalendarChannel _calendar;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    /// <param name="calendar">The client's calendar, read again after every request that moves the server clock (docs/adr/ADR-079 D5).</param>
    public DevChannel(IGameSocket socket, CalendarChannel calendar)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(calendar);
        _socket = socket;
        _calendar = calendar;
    }

    /// <summary>Asks the server for items and/or coins.</summary>
    /// <param name="itemId">The item, or zero for coins only.</param>
    /// <param name="quantity">How many items.</param>
    /// <param name="coins">Coins to add.</param>
    public void Grant(int itemId, int quantity, long coins) => _ = this.GrantAsync(itemId, quantity, coins);

    /// <summary>Asks the server to move its game clock forward by whole game days (crops grow, bare soil may revert). The change reaches the screen as pushed tiles.</summary>
    /// <param name="days">Game days, 1 to 60.</param>
    public void AdvanceClock(int days) => _ = this.AdvanceClockAsync(days);

    /// <summary>Asks the server to move the sender to a tile of a map (docs/adr/ADR-079).</summary>
    /// <param name="mapId">The map id.</param>
    /// <param name="tileX">The tile column.</param>
    /// <param name="tileY">The tile row.</param>
    public void Teleport(string mapId, int tileX, int tileY) => _ = this.AskAsync(async () =>
    {
        using PacketScope<DevTeleport> request = PacketFactory<DevTeleport>.Acquire();
        request.Value.MapId = mapId;
        request.Value.TileX = tileX;
        request.Value.TileY = tileY;
        _ = await _socket.RequestAsync<DevTeleportResult>(request.Value).ConfigureAwait(false);
    });

    /// <summary>Asks the server to pass the given number of season starts (docs/adr/ADR-079); zero passes to the next Spring (the QA run start).</summary>
    /// <param name="seasons">Season starts to pass, 1 to 4, or 0 for the next Spring.</param>
    public void Season(int seasons) => _ = this.AskAsync(async () =>
    {
        using PacketScope<DevSeason> request = PacketFactory<DevSeason>.Acquire();
        request.Value.Seasons = seasons;
        _ = await _socket.RequestAsync<DevSeasonResult>(request.Value).ConfigureAwait(false);
        _calendar.Load();
    });

    /// <summary>Asks the server to push a level-up banner to the sender; the skill does not change (docs/adr/ADR-079).</summary>
    /// <param name="skill">The skill, a <c>SkillKind</c> value: 1 farming, 2 foraging, 3 mining, 4 combat, 5 fishing.</param>
    /// <param name="level">The level to show, 1 to 10.</param>
    public void LevelUp(int skill, int level) => _ = this.AskAsync(async () =>
    {
        using PacketScope<DevLevelUp> request = PacketFactory<DevLevelUp>.Acquire();
        request.Value.Skill = skill;
        request.Value.Level = level;
        _ = await _socket.RequestAsync<DevLevelUpResult>(request.Value).ConfigureAwait(false);
    });

    /// <summary>Asks the server to make the sender's open cast bite at once (docs/adr/ADR-079).</summary>
    /// <param name="castId">The open cast.</param>
    public void FishingBite(uint castId) => _ = this.AskAsync(async () =>
    {
        using PacketScope<DevFishingBite> request = PacketFactory<DevFishingBite>.Acquire();
        request.Value.CastId = castId;
        _ = await _socket.RequestAsync<DevFishingBiteResult>(request.Value).ConfigureAwait(false);
    });

    /// <summary>Asks the server for a new registered test friend of the sender (docs/adr/ADR-105). Null when the server made none (or does not answer).</summary>
    /// <returns>The test player's nickname and session token, or null.</returns>
    [SuppressMessage("Design", "CA1031", Justification = "A tester tool: a missing answer (Production) is not an error.")]
    public async Task<TestFriend?> BefriendAsync()
    {
        try
        {
            using PacketScope<DevBefriend> request = PacketFactory<DevBefriend>.Acquire();
            DevBefriendResult reply = await _socket.RequestAsync<DevBefriendResult>(request.Value).ConfigureAwait(false);
            return reply.Ok ? new TestFriend(reply.Nickname, reply.SessionToken) : null;
        }
        catch (Exception)
        {
            // No answer: not a Development server.
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A tester tool: a missing answer (Production) is not an error.")]
    private async Task AdvanceClockAsync(int days)
    {
        try
        {
            using PacketScope<DevAdvanceClock> request = PacketFactory<DevAdvanceClock>.Acquire();
            request.Value.Days = days;
            _ = await _socket.RequestAsync<DevAdvanceClockResult>(request.Value).ConfigureAwait(false);
            _calendar.Load();
        }
        catch (Exception)
        {
            // No answer: not a Development server.
        }
    }

    // A tester request: a missing answer (Production) is not an error, so nothing is thrown out of it.
    [SuppressMessage("Design", "CA1031", Justification = "A tester tool: a missing answer (Production) is not an error.")]
    private async Task AskAsync(Func<Task> ask)
    {
        try
        {
            await ask().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No answer: not a Development server.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "A tester tool: a missing answer (Production) is not an error.")]
    private async Task GrantAsync(int itemId, int quantity, long coins)
    {
        try
        {
            using PacketScope<DevGrant> request = PacketFactory<DevGrant>.Acquire();
            request.Value.ItemId = itemId;
            request.Value.Quantity = quantity;
            request.Value.Coins = coins;
            _ = await _socket.RequestAsync<DevGrantResult>(request.Value).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // No answer: not a Development server.
        }
    }
}
