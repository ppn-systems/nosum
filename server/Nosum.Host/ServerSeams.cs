// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Presence;

namespace Nosum.Host;

/// <summary>
/// Test seams of the composition root: each function wraps a service the snapshot loop or the leave path uses, so an integration test can slow a save or
/// stall a send without changing the production wiring. Production passes none.
/// </summary>
/// <param name="WrapPresence">Wraps the presence the player service sends snapshots through.</param>
/// <param name="WrapStates">Wraps the saved-place service the player service writes through.</param>
internal sealed record ServerSeams(Func<IMapPresence, IMapPresence>? WrapPresence = null, Func<IPlayerStateService, IPlayerStateService>? WrapStates = null);
