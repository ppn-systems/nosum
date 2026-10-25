// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions;
using Nalix.Abstractions.Networking;
using Nalix.Abstractions.Security;

namespace Nosum.Handlers.Session;

/// <summary>
/// The "current player" of a connection. Handlers of any feature call
/// <see cref="GetPlayer"/> (or <see cref="TryGetPlayer"/>) instead of trusting an id in a packet.
/// </summary>
public static class PlayerConnectionExtensions
{
    private static readonly AttributeKey s_key = AttributeKey.FromName("nosum.player.identity");

    /// <summary>Permission level granted to a signed-in player; gate handlers with <c>[PacketPermission(PermissionLevel.USER)]</c>.</summary>
    public const PermissionLevel SignedInLevel = PermissionLevel.USER;

    /// <summary>Marks the connection as signed in: stores the identity and raises <see cref="IConnection.Level"/>.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="identity">The signed-in player.</param>
    public static void AttachPlayer(this IConnection connection, PlayerIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(connection);
        connection.Attributes[s_key] = identity;
        connection.Level = SignedInLevel;
    }

    /// <summary>Reads the signed-in player of a connection.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="identity">The player when this returns true.</param>
    /// <returns>True when a player is signed in on the connection.</returns>
    public static bool TryGetPlayer(this IConnection connection, out PlayerIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection.Attributes.TryGetValue(s_key, out object? value) && value is PlayerIdentity found)
        {
            identity = found;
            return true;
        }

        identity = default;
        return false;
    }

    /// <summary>Reads the signed-in player; for handlers already gated by <c>PermissionLevel.USER</c>.</summary>
    /// <param name="connection">The connection.</param>
    /// <returns>The signed-in player.</returns>
    /// <exception cref="InvalidOperationException">No player is signed in on the connection.</exception>
    public static PlayerIdentity GetPlayer(this IConnection connection)
        => connection.TryGetPlayer(out PlayerIdentity identity)
            ? identity
            : throw new InvalidOperationException("No player is signed in on this connection.");

    /// <summary>Signs the connection out: removes the identity and drops the permission level.</summary>
    /// <param name="connection">The connection.</param>
    public static void DetachPlayer(this IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _ = connection.Attributes.Remove(s_key);
        connection.Level = PermissionLevel.NONE;
    }
}
