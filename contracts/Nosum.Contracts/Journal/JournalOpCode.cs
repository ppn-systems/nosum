// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Journal;

/// <summary>
/// Opcodes of the Journal domain: the collection journal, the record of every item a player has obtained (how many, and when first).
/// <para>Band <c>0xD100-0xD1FF</c> is reserved for this domain, next to the Season band (<c>0xD000-0xD0FF</c>). C2S requests use <c>0xD100-0xD13F</c>, S2C replies use <c>0xD140-0xD17F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class JournalOpCode
{
    // -- C2S (0xD100-0xD13F) --------------------------------------------------

    /// <summary>Asks for the player's collection journal: every item obtained so far, with its count and first-obtained time. Reply: <see cref="S2C.JournalReply"/>.</summary>
    public const ushort GetJournal = 0xD100;

    // -- S2C (0xD140-0xD17F) --------------------------------------------------

    /// <summary>Reply to <see cref="GetJournal"/>: the discovered entries only; undiscovered items are not sent.</summary>
    public const ushort JournalReply = 0xD140;
}
