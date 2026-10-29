// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Wasm.ViewModels.Fishing;

/// <summary>Where the fishing banner is in a cast (docs/adr/ADR-071 D4). The server decides every outcome; these only say what is shown.</summary>
public enum FishingPhase : byte
{
    /// <summary>No line is out and no result is shown: the banner is hidden.</summary>
    Idle = 0,

    /// <summary>The cast went to the server and its answer has not come back yet.</summary>
    Casting = 1,

    /// <summary>The line is out; the fish has not bitten yet.</summary>
    Waiting = 2,

    /// <summary>The bite window is open: the press reels the line in.</summary>
    Biting = 3,

    /// <summary>The press went to the server; its answer has not come back yet.</summary>
    Reeling = 4,

    /// <summary>The cast is over: the result is shown for a while, then the banner clears.</summary>
    Result = 5,
}
