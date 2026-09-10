// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;

namespace Mail2Pst.Core.Models;

/// <summary>
/// Normalized representation of a single email message, produced by every
/// <see cref="Mail2Pst.Core.Parsing.IMailSourceParser"/> implementation.
/// </summary>
public class MailMessage
{
    public string? Subject { get; set; }
    // MIME From identifies the author represented by the message. Sender, when present,
    // identifies the actual transport sender and must not overwrite that distinction.
    public MailAddress? From { get; set; }
    public MailAddress? Sender { get; set; }
    public List<MailAddress> To { get; set; } = new();
    public List<MailAddress> Cc { get; set; } = new();
    public List<MailAddress> Bcc { get; set; } = new();
    // Reply-To is a list in MIME and may contain more than one mailbox.
    public List<MailAddress> ReplyTo { get; set; } = new();
    public DateTimeOffset? Date { get; set; }
    public string? TextBody { get; set; }
    public string? HtmlBody { get; set; }
    public List<MailAttachment> Attachments { get; set; } = new();
    public SourceReference Source { get; set; } = new();
    public string? MessageId { get; set; }
    public string? InReplyTo { get; set; }
    public string? References { get; set; }
    public bool IsRead { get; set; } = true;
    public bool IsReplied { get; set; } = false;
    public bool IsForwarded { get; set; } = false;
    public bool IsFlagged { get; set; } = false;
    public bool IsJunk { get; set; } = false;
    public List<string> Categories { get; set; } = new();
    /// <summary>Decoded, ordered, case-insensitively de-duplicated values from all non-empty
    /// X-Gmail-Labels headers. Empty when the source carries no usable Gmail label metadata.</summary>
    public List<string> GmailLabels { get; set; } = new();
    /// <summary>True when an X-Gmail-Labels header exists, including an empty/malformed one.</summary>
    public bool HasGmailLabelsHeader { get; set; }
    public MailImportance Importance { get; set; } = MailImportance.Normal;
}
