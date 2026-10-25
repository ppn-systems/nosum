// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Mail;

/// <summary>Outcome code of a mail request. Values are append-only.</summary>
public enum MailStatus : byte
{
    /// <summary>Applied.</summary>
    Ok = 0,
    /// <summary>No such mail in the player's mailbox.</summary>
    NotFound = 1,
    /// <summary>The mail carries no attachment.</summary>
    NothingToCollect = 2,
    /// <summary>The attachment was collected already.</summary>
    AlreadyCollected = 3,
    /// <summary>The mail expired.</summary>
    Expired = 4,
    /// <summary>The inventory has no room for the items.</summary>
    NoSpace = 5,
    /// <summary>The coins would exceed the balance limit.</summary>
    TooRich = 6,
    /// <summary>The mail still carries an attachment; collect it first.</summary>
    HasAttachments = 7,
    /// <summary>Another change got there first; try again.</summary>
    Conflict = 8,
    /// <summary>The transfer failed.</summary>
    Failed = 9,
}
