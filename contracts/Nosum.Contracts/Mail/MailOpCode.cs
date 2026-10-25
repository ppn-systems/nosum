// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Mail;

/// <summary>
/// Opcodes of the Mail domain.
/// <para>Mailbox (docs/adr/ADR-029 D5). Sub-range <c>0x9300-0x93FF</c> of the Trade band is reserved for this domain (docs/adr/ADR-029 D6). C2S requests use <c>0x9300-0x933F</c>, S2C replies and pushes <c>0x9340-0x937F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class MailOpCode
{
    // -- C2S --

    /// <summary>Asks for the mailbox. Reply: <see cref="S2C.MailList"/>.</summary>
    public const ushort GetMail = 0x9300;

    /// <summary>Marks a mail read. Reply: <see cref="S2C.MailResult"/>.</summary>
    public const ushort ReadMail = 0x9301;

    /// <summary>Collects the attachment of a mail in one atomic transfer. Reply: <see cref="S2C.MailResult"/>.</summary>
    public const ushort CollectMail = 0x9302;

    /// <summary>Deletes a mail. Reply: <see cref="S2C.MailResult"/>.</summary>
    public const ushort DeleteMail = 0x9303;

    // -- S2C --

    /// <summary>Reply to read, collect and delete.</summary>
    public const ushort MailResult = 0x9340;

    /// <summary>The mailbox; reply to <c>GetMail</c>.</summary>
    public const ushort MailList = 0x9341;

    /// <summary>Push to one player: the number of unread mails changed.</summary>
    public const ushort MailUnread = 0x9342;
}
