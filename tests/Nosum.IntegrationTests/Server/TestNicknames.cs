// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Domain.Accounts;
using Nosum.Gameplay.Players;

namespace Nosum.IntegrationTests.Server;

/// <summary>Unique valid nicknames for tests that create players (the database requires one per player).</summary>
internal static class TestNicknames
{
    /// <summary>Returns a valid, unique nickname such as <c>Test3fa9c1d07b</c> (14 characters).</summary>
    /// <returns>The nickname.</returns>
    public static string Next() => "Test" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Gives a hand-built player a unique nickname.</summary>
    /// <param name="player">The player to name.</param>
    /// <returns>The same player.</returns>
    public static Player Name(Player player)
    {
        player.Nickname = Next();
        player.NormalizedNickname = NicknameRules.Key(player.Nickname);
        return player;
    }
}
