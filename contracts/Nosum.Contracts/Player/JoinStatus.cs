// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Player;

/// <summary>Outcome code of a <see cref="C2S.JoinMap"/> request. Clients map it to text; no text travels on the wire.</summary>
public enum JoinStatus : byte
{
    /// <summary>The player entered the map.</summary>
    Ok = 0,

    /// <summary>The server has no map with that id.</summary>
    UnknownMap = 1,
}
