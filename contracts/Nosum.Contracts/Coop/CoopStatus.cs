// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Coop;

/// <summary>Outcome code of a co-op request. Values are append-only.</summary>
public enum CoopStatus : byte
{
    /// <summary>Applied.</summary>
    Ok = 0,

    /// <summary>The name is not valid.</summary>
    InvalidName = 1,

    /// <summary>The player already belongs to a co-op.</summary>
    AlreadyInCoop = 2,

    /// <summary>The player belongs to no co-op.</summary>
    NotInCoop = 3,

    /// <summary>Only the founder may do this.</summary>
    NotFounder = 4,

    /// <summary>The other player is not on the same map.</summary>
    TargetNotFound = 5,

    /// <summary>The other player already belongs to a co-op.</summary>
    TargetInCoop = 6,

    /// <summary>The invitation does not exist or expired.</summary>
    InviteNotFound = 7,

    /// <summary>The founder cannot leave an empty co-op while its warehouse holds items.</summary>
    CannotLeave = 8,

    /// <summary>The member may not take that much.</summary>
    NotAllowed = 9,

    /// <summary>Not enough items.</summary>
    NotEnough = 10,

    /// <summary>The inventory has no room.</summary>
    NoSpace = 11,

    /// <summary>The quantity is not valid.</summary>
    InvalidQuantity = 12,

    /// <summary>The item cannot be put in a warehouse.</summary>
    NotTradable = 13,

    /// <summary>The item id is not in the catalogue.</summary>
    UnknownItem = 14,

    /// <summary>There is no such member.</summary>
    MemberNotFound = 15,

    /// <summary>The founder cannot kick themselves; leave instead.</summary>
    SelfKick = 16,

    /// <summary>Another change happened at the same time; ask again.</summary>
    Conflict = 17,

    /// <summary>The player already has an invitation from this co-op.</summary>
    AlreadyInvited = 18,
}
