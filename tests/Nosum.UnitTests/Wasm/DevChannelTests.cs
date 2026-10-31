// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using FluentAssertions;
using Nosum.Contracts.Dev.C2S;
using Nosum.Contracts.Dev.S2C;
using Nosum.Wasm.Socket.Calendar;
using Nosum.Wasm.Socket.Dev;
using Xunit;

namespace Nosum.UnitTests.Wasm;

/// <summary>The tester requests of the QA hook (docs/adr/ADR-079) carry their fields to the wire request, and the client asks only with them.</summary>
public sealed class DevChannelTests
{
    [Fact]
    public void Teleport_AsksWithTheMapAndTheTile()
    {
        FakeGameSocket socket = new();
        (string? map, int tileX, int tileY) = (null, -1, -1);
        socket.ReplyWith<DevTeleportResult>(request =>
        {
            DevTeleport ask = (DevTeleport)request;
            (map, tileX, tileY) = (ask.MapId, ask.TileX, ask.TileY);
            return new DevTeleportResult { Ok = true };
        });

        new DevChannel(socket, new CalendarChannel(socket)).Teleport("town", 4, 5);

        map.Should().Be("town");
        (tileX, tileY).Should().Be((4, 5));
    }

    [Fact]
    public void Season_AsksWithTheSeasonCount()
    {
        FakeGameSocket socket = new();
        int seasons = 0;
        socket.ReplyWith<DevSeasonResult>(request =>
        {
            seasons = ((DevSeason)request).Seasons;
            return new DevSeasonResult { Ok = true };
        });

        new DevChannel(socket, new CalendarChannel(socket)).Season(2);

        seasons.Should().Be(2);
    }

    [Fact]
    public void LevelUp_AsksWithTheSkillAndTheLevel()
    {
        FakeGameSocket socket = new();
        (int skill, int level) = (0, 0);
        socket.ReplyWith<DevLevelUpResult>(request =>
        {
            DevLevelUp ask = (DevLevelUp)request;
            (skill, level) = (ask.Skill, ask.Level);
            return new DevLevelUpResult { Ok = true };
        });

        new DevChannel(socket, new CalendarChannel(socket)).LevelUp(4, 6);

        (skill, level).Should().Be((4, 6));
    }

    [Fact]
    public void FishingBite_AsksWithTheCast()
    {
        FakeGameSocket socket = new();
        uint cast = 0;
        socket.ReplyWith<DevFishingBiteResult>(request =>
        {
            cast = ((DevFishingBite)request).CastId;
            return new DevFishingBiteResult { Ok = true };
        });

        new DevChannel(socket, new CalendarChannel(socket)).FishingBite(9);

        cast.Should().Be(9u);
    }

    [Fact]
    public void Teleport_WithNoAnswer_ThrowsNothingOut()
    {
        // A Production server never answers: the channel swallows the missing reply, as the grant does.
        FakeGameSocket socket = new();

        Action ask = () => new DevChannel(socket, new CalendarChannel(socket)).Teleport("town", 1, 1);

        ask.Should().NotThrow();
    }
}
