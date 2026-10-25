// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Handlers.Session;

/// <summary>Who is signed in on a connection. Set by the server at sign-in, never read from a packet.</summary>
/// <param name="PlayerId">The signed-in player's id.</param>
/// <param name="Username">The player's username.</param>
public readonly record struct PlayerIdentity(Guid PlayerId, string Username);
