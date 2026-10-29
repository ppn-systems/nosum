// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Mail;

/// <summary>One item line attached to a <see cref="MailMessage"/>.</summary>
public sealed class MailAttachmentItem
{
    /// <summary>Gets or sets the mail.</summary>
    public Guid MailId { get; set; }

    /// <summary>Gets or sets the item id.</summary>
    public int ItemId { get; set; }

    /// <summary>Gets or sets how many.</summary>
    public int Quantity { get; set; }
}
