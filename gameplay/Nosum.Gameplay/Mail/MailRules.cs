// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Mail;

/// <summary>Limits of the mailbox and the codes of the system mail the server sends (docs/adr/ADR-029 D5). Pure data shared by the server and the client.</summary>
public static class MailRules
{
    /// <summary>Days a mail stays before it expires.</summary>
    public const int ExpiryDays = 30;

    /// <summary>The most mails a mailbox lists (the newest ones).</summary>
    public const int MaxListed = 100;

    /// <summary>The most arguments a mail code takes.</summary>
    public const int MaxArgs = 6;

    /// <summary>The longest argument, in characters.</summary>
    public const int MaxArgLength = 256;

    /// <summary>The most item lines one mail carries.</summary>
    public const int MaxItemLines = 8;

    /// <summary>How long a mail stays.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(ExpiryDays);

    /// <summary>The system mail codes. The client renders a code and its arguments from its resources; a code it does not know is shown as a generic notice.</summary>
    public static class Codes
    {
        /// <summary>A market listing sold. Arguments: item id, quantity, coins paid out after the fee.</summary>
        public const string MarketSold = "market.sold";

        /// <summary>A co-op invited the player. Arguments: co-op name, inviter nickname.</summary>
        public const string CoopInvite = "coop.invite";

        /// <summary>A reward from a community goal (docs/adr/ADR-030). Arguments: defined by that feature.</summary>
        public const string GoalReward = "goal.reward";
    }
}
