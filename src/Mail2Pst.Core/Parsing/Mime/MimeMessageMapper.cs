// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core.Models;
using Mail2Pst.Core.Msf;
using MimeKit;
using MimeKit.Utils;

namespace Mail2Pst.Core.Parsing.Mime;

/// <summary>
/// Maps a parsed MIME message into the converter's neutral <see cref="MailMessage"/> model
/// (subject, addresses, date, bodies, attachments, threading headers, read-state, importance).
/// Non-fatal fidelity issues are appended to the supplied warning list so callers can
/// report them without changing the conversion result.
/// Intended as a low-level building block for source parsers such as <c>MboxParser</c> and a
/// future <c>EmlParser</c> — it is public for reuse, NOT a stable end-user API; no backward-
/// compatibility is promised yet.
/// </summary>
public class MimeMessageMapper
{
    private readonly long _tempFileThresholdBytes;
    private readonly bool _measureOnly;

    private const uint MozillaStatusRead = 0x0001;
    private const uint MozillaStatusReplied = 0x0002;
    private const uint MozillaStatusMarked = 0x0004;
    private const uint MozillaStatusForwarded = 0x1000;
    private static readonly DateTimeOffset MinimumPstDate =
        new(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const int MaximumPstSubjectLength = 253;
    private const int MaximumDiagnosticHeaderLength = 200;
    private static readonly object TnefEncodingGate = new();
    private static bool _tnefEncodingProviderRegistered;

    public MimeMessageMapper(long tempFileThresholdBytes = 4L * 1024 * 1024, bool measureOnly = false)
    {
        if (tempFileThresholdBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(tempFileThresholdBytes), tempFileThresholdBytes, "Temp-file threshold must be non-negative.");
        _tempFileThresholdBytes = tempFileThresholdBytes;
        _measureOnly = measureOnly;
    }

    // A present-but-unparseable Date header makes MimeKit return
    // DateTimeOffset.MinValue (0001-01-01). The MBOX parser supplies the
    // envelope postmark as a source-specific fallback before we try Received.
    private static DateTimeOffset? ResolveDate(
        MimeMessage mime,
        DateTimeOffset? envelopeDate,
        List<string> warnings)
    {
        if (mime.Headers.Contains(HeaderId.Date))
        {
            DateTimeOffset headerDate = mime.Date;
            if (headerDate != DateTimeOffset.MinValue)
                return headerDate;
        }

        if (envelopeDate.HasValue)
            return envelopeDate;

        if (TryParseReceivedDate(mime, out DateTimeOffset receivedDate, out string receivedField))
        {
            AddIntegrityWarning(warnings, "date-received-fallback",
                $"Date is {(mime.Headers.Contains(HeaderId.Date) ? "present but invalid" : "absent")} " +
                $"({DescribeRawHeader(mime, "Date")}); PST submit and delivery timestamps use the " +
                $"{receivedField} fallback ({DescribeRawHeader(mime, receivedField)}).");
            return receivedDate;
        }

        if (HasNonBlankHeader(mime, "Date"))
        {
            AddIntegrityWarning(warnings, "date-missing",
                $"Date is present but invalid ({DescribeRawHeader(mime, "Date")}); " +
                "PST submit and delivery timestamps will use the writer default instead of the source date.");
        }

        return null;
    }

    public MailMessage Map(
        MimeMessage mime,
        SourceReference sourceRef,
        List<string> warnings,
        DateTimeOffset? envelopeDate = null)
    {
        // Read the ordinary MIME values before the optional TNEF expansion. Registering the
        // Windows code-page provider is required by MimeKit's TNEF reader and may affect lazy
        // decoding of an already-parsed header; the normal MIME mapping must retain its original
        // behavior even for messages that happen to carry winmail.dat.
        string? sourceSubject = mime.Subject;
        string? sourceTextBody = mime.TextBody;
        string? sourceHtmlBody = mime.HtmlBody;
        string? sourceMessageId = mime.MessageId;
        string? sourceInReplyTo = mime.InReplyTo;
        string? sourceReferencesHeader = mime.Headers["References"];
        List<string> sourceReferences = mime.References.ToList();

        uint? mozillaStatus = ParseMozillaStatus(mime);
        List<MailboxAddress> fromMailboxes = mime.From.Mailboxes.ToList();
        List<MailboxAddress> toMailboxes = mime.To.Mailboxes.ToList();
        List<MailboxAddress> ccMailboxes = mime.Cc.Mailboxes.ToList();
        List<MailboxAddress> bccMailboxes = mime.Bcc.Mailboxes.ToList();
        List<MailboxAddress> replyToMailboxes = mime.ReplyTo.Mailboxes.ToList();
        (bool hasGmailLabelsHeader, List<string> gmailLabels) = ParseGmailLabels(mime, warnings);
        List<MailboxAddress> deliveredToMailboxes = ParseDeliveredToMailboxes(mime);
        if (toMailboxes.Count == 0 && ccMailboxes.Count == 0 && bccMailboxes.Count == 0
            && deliveredToMailboxes.Count > 0)
        {
            // Delivered-To is an envelope-level fallback only. Use it when the visible
            // recipient lists are completely unusable, never append it to a valid To/Cc/Bcc
            // list where it could duplicate or change the original visible recipients.
            toMailboxes = deliveredToMailboxes;
        }
        DateTimeOffset? date = ResolveDate(mime, envelopeDate, warnings);
        List<MimeMessage> tnefMessages = ExpandTnefMessages(mime, warnings);

        string? textBody = sourceTextBody;
        string? htmlBody = sourceHtmlBody;
        if (string.IsNullOrWhiteSpace(textBody) && string.IsNullOrWhiteSpace(htmlBody))
        {
            textBody = FirstUsableBody(textBody, tnefMessages.Select(message => message.TextBody));
            htmlBody = FirstUsableBody(htmlBody, tnefMessages.Select(message => message.HtmlBody));
        }

        List<MailAttachment> attachments = ExtractAttachments(mime, warnings);
        foreach (MimeMessage tnefMessage in tnefMessages)
            attachments.AddRange(ExtractAttachments(tnefMessage, warnings));

        var message = new MailMessage
        {
            Subject = sourceSubject,
            From = ToMailAddress(fromMailboxes.FirstOrDefault()),
            Sender = ToMailAddress(mime.Sender),
            To = toMailboxes.Select(ToMailAddressNonNull).ToList(),
            Cc = ccMailboxes.Select(ToMailAddressNonNull).ToList(),
            Bcc = bccMailboxes.Select(ToMailAddressNonNull).ToList(),
            ReplyTo = replyToMailboxes.Select(ToMailAddressNonNull).ToList(),
            Date = date,
            TextBody = textBody,
            HtmlBody = htmlBody,
            Attachments = attachments,
            Source = sourceRef,
            MessageId = MessageIdNormalizer.NormalizeForJoin(sourceMessageId),
            InReplyTo = NormalizeInReplyTo(sourceInReplyTo, mime.Headers["In-Reply-To"]),
            References = NormalizeReferences(sourceReferences, sourceReferencesHeader),
            IsRead = ParseIsRead(mime, mozillaStatus),
            IsReplied = mozillaStatus is { } f1 && (f1 & MozillaStatusReplied) != 0,
            IsFlagged = mozillaStatus is { } f2 && (f2 & MozillaStatusMarked) != 0,
            IsForwarded = mozillaStatus is { } f3 && (f3 & MozillaStatusForwarded) != 0,
            GmailLabels = gmailLabels,
            HasGmailLabelsHeader = hasGmailLabelsHeader,
            Importance = ParseImportance(mime),
        };

        ValidateMappedFields(
            mime, message, fromMailboxes, toMailboxes, ccMailboxes, bccMailboxes,
            replyToMailboxes, deliveredToMailboxes, warnings);
        return message;
    }

    private static (bool HasHeader, List<string> Labels) ParseGmailLabels(
        MimeMessage mime, List<string> warnings)
    {
        List<Header> headers = mime.Headers
            .Where(header => header.Field.Equals("X-Gmail-Labels", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (headers.Count == 0)
            return (false, new List<string>());

        var labels = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasNonBlankSource = false;
        foreach (Header header in headers)
        {
            if (header.RawValue.Any(value =>
                    value != (byte)' ' && value != (byte)'\t'
                    && value != (byte)'\r' && value != (byte)'\n'))
            {
                hasNonBlankSource = true;
            }

            foreach (string token in (header.Value ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Length > 0 && seen.Add(token))
                    labels.Add(token);
            }
        }

        if (hasNonBlankSource && labels.Count == 0)
        {
            AddIntegrityWarning(warnings, "gmail-labels-unparsed",
                "X-Gmail-Labels is present and non-empty but yielded no usable label; " +
                $"{DescribeRawHeader(mime, "X-Gmail-Labels")}; no Gmail label category or folder can be written.");
        }

        return (true, labels);
    }

    private static string? FirstUsableBody(string? current, IEnumerable<string?> fallbacks)
    {
        if (!string.IsNullOrWhiteSpace(current))
            return current;

        return fallbacks.FirstOrDefault(body => !string.IsNullOrWhiteSpace(body));
    }

    private static string? NormalizeInReplyTo(string? parsedValue, string? decodedHeaderValue)
    {
        string? normalized = MessageIdNormalizer.NormalizeForJoin(parsedValue);
        if (normalized is not null)
            return normalized;

        return NormalizeSingleMessageId(decodedHeaderValue);
    }

    private static string? NormalizeReferences(IReadOnlyList<string> parsedValues, string? decodedHeaderValue)
    {
        if (parsedValues.Count > 0)
        {
            string normalized = string.Join(" ", parsedValues
                .Select(MessageIdNormalizer.NormalizeForJoin)
                .OfType<string>());
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        if (string.IsNullOrWhiteSpace(decodedHeaderValue))
            return null;

        List<string> decodedIds = MimeUtils.EnumerateReferences(decodedHeaderValue).ToList();
        if (decodedIds.Count > 0)
        {
            string normalized = string.Join(" ", decodedIds
                .Select(MessageIdNormalizer.NormalizeForJoin)
                .OfType<string>());
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        // Gmail exports occasionally serialize a single References value without the RFC
        // angle brackets. Recover it only when it is an email-shaped token; never wrap an
        // arbitrary string such as "11.111512" and present it as a threading identifier.
        return NormalizeSingleMessageId(decodedHeaderValue);
    }

    private static string? NormalizeSingleMessageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string candidate = value.Trim();
        if (candidate.Contains('<') || candidate.Contains('>') || candidate.Any(char.IsWhiteSpace))
        {
            List<string> ids = MimeUtils.EnumerateReferences(candidate).ToList();
            return ids.Count == 1 ? MessageIdNormalizer.NormalizeForJoin(ids[0]) : null;
        }

        if (!candidate.Contains('@', StringComparison.Ordinal))
            return null;

        List<string> wrappedIds = MimeUtils.EnumerateReferences($"<{candidate}>").ToList();
        return wrappedIds.Count == 1 ? MessageIdNormalizer.NormalizeForJoin(wrappedIds[0]) : null;
    }

    private static List<MimeMessage> ExpandTnefMessages(MimeMessage mime, List<string> warnings)
    {
        var converted = new List<MimeMessage>();
        foreach (MimeKit.Tnef.TnefPart tnef in mime.BodyParts.OfType<MimeKit.Tnef.TnefPart>())
        {
            try
            {
                EnsureTnefEncodingProvider();
                converted.Add(tnef.ConvertToMessage());
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                AddIntegrityWarning(warnings, "tnef-expansion-failed",
                    $"TNEF part '{tnef.FileName ?? "winmail.dat"}' could not be expanded ({ex.GetType().Name}: {CompactDiagnosticValue(ex.Message)}); " +
                    "the original TNEF bytes remain preserved as an attachment, but embedded body/attachment fields were not expanded.");
            }
        }

        return converted;
    }

    private static void EnsureTnefEncodingProvider()
    {
        lock (TnefEncodingGate)
        {
            if (_tnefEncodingProviderRegistered)
                return;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _tnefEncodingProviderRegistered = true;
        }
    }

    private static bool TryParseReceivedDate(
        MimeMessage mime,
        out DateTimeOffset date,
        out string fieldName)
    {
        date = default;
        fieldName = string.Empty;

        foreach (string candidateField in new[] { "Received", "X-Received" })
        {
            foreach (Header header in mime.Headers.Where(header =>
                         header.Field.Equals(candidateField, StringComparison.OrdinalIgnoreCase)))
            {
                string value = header.Value ?? string.Empty;
                int separator = value.LastIndexOf(';');
                if (separator >= 0)
                    value = value[(separator + 1)..];

                int commentStart = value.IndexOf(" (", StringComparison.Ordinal);
                if (commentStart >= 0)
                    value = value[..commentStart];

                if (DateUtils.TryParse(value.Trim(), out date))
                {
                    fieldName = candidateField;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasNonBlankHeader(MimeMessage mime, string fieldName)
    {
        if (!string.IsNullOrWhiteSpace(mime.Headers[fieldName]))
            return true;

        // Header.Value is decoded for display. If decoding collapses a malformed but non-empty
        // source header to an empty string, inspect RawValue so a real source-to-output loss is
        // still reported.
        return mime.Headers
            .Where(header => header.Field.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
            .Any(header => header.RawValue.Any(value =>
                value != (byte)' ' && value != (byte)'\t' && value != (byte)'\r' && value != (byte)'\n'));
    }

    private static List<MailboxAddress> ParseDeliveredToMailboxes(MimeMessage mime)
    {
        var mailboxes = new List<MailboxAddress>();
        foreach (Header header in mime.Headers.Where(header =>
                     header.Field.Equals("Delivered-To", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(header.Value)
                || !InternetAddressList.TryParse(header.Value, out InternetAddressList? parsed)
                || parsed is null)
            {
                continue;
            }

            mailboxes.AddRange(parsed.Mailboxes);
        }

        // Repeated Delivered-To headers can occur in a delivery chain. Keep every distinct
        // usable address, but do not create duplicate PST recipients for repeated trace values.
        return mailboxes
            .GroupBy(mailbox => $"{mailbox.Address}\u0000{mailbox.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static bool IsEmptyUndisclosedGroup(InternetAddressList source) =>
        source.Count > 0
        && source.All(address => address is GroupAddress group
            && group.Members.Count == 0
            && string.Equals(group.Name, "undisclosed-recipients", StringComparison.OrdinalIgnoreCase));

    private static bool HasMeaningfulAddressSource(
        string fieldName, InternetAddressList source, MimeMessage mime) =>
        HasNonBlankHeader(mime, fieldName) && !IsEmptyUndisclosedGroup(source);

    /// <summary>
    /// Records source fields that are absent, malformed, adjusted, or only partially
    /// representable by the PST mapping. This is deliberately non-fatal: it does not reject a
    /// message, and it reports the explicit Delivered-To fallback when that policy is used.
    /// </summary>
    private static void ValidateMappedFields(
        MimeMessage mime,
        MailMessage message,
        IReadOnlyList<MailboxAddress> fromMailboxes,
        IReadOnlyList<MailboxAddress> toMailboxes,
        IReadOnlyList<MailboxAddress> ccMailboxes,
        IReadOnlyList<MailboxAddress> bccMailboxes,
        IReadOnlyList<MailboxAddress> replyToMailboxes,
        IReadOnlyList<MailboxAddress> deliveredToMailboxes,
        List<string> warnings)
    {
        if (fromMailboxes.Count > 1)
        {
            AddIntegrityWarning(warnings, "from-multiple",
                $"From contains {fromMailboxes.Count} mailbox addresses ({DescribeAddressList("From", mime.From, fromMailboxes, new[] { message.From }.OfType<MailAddress>().ToList(), mime)}); " +
                "only the first one is written to the PST sender fields (the remaining addresses are not representable in those single-value MAPI properties).");
        }

        if (message.From is null && HasNonBlankHeader(mime, "From"))
        {
            AddIntegrityWarning(warnings, "from-missing",
                $"From has no usable mailbox address ({DescribeAddressList("From", mime.From, fromMailboxes, Array.Empty<MailAddress>(), mime)}; " +
                $"{DescribeRawHeader(mime, "From")}); PST sender fields will be empty.");
        }
        else if (message.From is not null
            && string.IsNullOrWhiteSpace(message.From.Email)
            && HasNonBlankHeader(mime, "From"))
        {
            AddIntegrityWarning(warnings, "from-address-empty",
                $"From has an empty mailbox address ({DescribeAddressList("From", mime.From, fromMailboxes, new[] { message.From }, mime)}; " +
                $"{DescribeRawHeader(mime, "From")}); PST sender address fields will be empty.");
        }

        if (HasNonBlankHeader(mime, "Sender"))
        {
            if (message.Sender is null)
            {
                AddIntegrityWarning(warnings, "sender-invalid",
                    $"Sender header is present but MimeKit produced no usable mailbox ({DescribeRawHeader(mime, "Sender")}); " +
                    "PST actual-sender address fields will be empty and no sender identity is fabricated.");
            }
            else if (string.IsNullOrWhiteSpace(message.Sender.Email))
            {
                AddIntegrityWarning(warnings, "sender-address-empty",
                    $"Sender contains a mailbox with an empty address ({DescribeRawHeader(mime, "Sender")}); " +
                    "PST actual-sender address fields will be empty.");
            }
        }

        if (HasMeaningfulAddressSource("Reply-To", mime.ReplyTo, mime))
        {
            if (replyToMailboxes.Count == 0)
            {
                AddIntegrityWarning(warnings, "reply-to-invalid",
                    $"Reply-To header is present but MimeKit produced no usable mailbox ({DescribeAddressList("Reply-To", mime.ReplyTo, replyToMailboxes, message.ReplyTo, mime)}; " +
                    $"{DescribeRawHeader(mime, "Reply-To")}); no PST reply-recipient list will be written.");
            }

            ValidateRecipientList("Reply-To", mime.ReplyTo, message.ReplyTo, replyToMailboxes, warnings, mime);
        }

        ValidateRecipientList("To", mime.To, message.To, toMailboxes, warnings, mime);
        ValidateRecipientList("Cc", mime.Cc, message.Cc, ccMailboxes, warnings, mime);
        ValidateRecipientList("Bcc", mime.Bcc, message.Bcc, bccMailboxes, warnings, mime);

        if (message.To.Count == 0 && message.Cc.Count == 0 && message.Bcc.Count == 0)
        {
            bool sourceHasRecipientValue =
                HasMeaningfulAddressSource("To", mime.To, mime)
                || HasMeaningfulAddressSource("Cc", mime.Cc, mime)
                || HasMeaningfulAddressSource("Bcc", mime.Bcc, mime)
                || HasNonBlankHeader(mime, "Delivered-To");
            if (sourceHasRecipientValue)
            {
                AddIntegrityWarning(warnings, "recipients-missing",
                    "To, Cc, and Bcc contain no usable mailbox recipient; " +
                    $"{DescribeAddressList("To", mime.To, toMailboxes, message.To, mime)}; " +
                    $"{DescribeAddressList("Cc", mime.Cc, ccMailboxes, message.Cc, mime)}; " +
                    $"{DescribeAddressList("Bcc", mime.Bcc, bccMailboxes, message.Bcc, mime)}; " +
                    $"source headers: {DescribeRawHeader(mime, "To")}; {DescribeRawHeader(mime, "Cc")}; {DescribeRawHeader(mime, "Bcc")}; " +
                    $"Delivered-To fallback: {DescribeDeliveredTo(mime, deliveredToMailboxes)}; " +
                    "the PST recipient table will be empty.");
            }
        }

        if (string.IsNullOrWhiteSpace(message.Subject) && HasNonBlankHeader(mime, "Subject"))
        {
            AddIntegrityWarning(warnings, "subject-missing",
                $"Subject is {(mime.Headers.Contains(HeaderId.Subject) ? "present but empty" : "absent")} ({DescribeRawHeader(mime, "Subject")}); " +
                "the PST subject will be empty.");
        }
        else if (message.Subject is not null)
        {
            int controlCharacters = message.Subject.Count(c => c < 0x20 || c == 0x7F);
            int usableCharacters = message.Subject.Count(c => c >= 0x20 && c != 0x7F);
            int sanitizedLength = Math.Min(MaximumPstSubjectLength, usableCharacters);
            if (usableCharacters > MaximumPstSubjectLength)
            {
                AddIntegrityWarning(warnings, "subject-truncated",
                    $"Subject exceeds the converter PST compatibility limit and will be truncated " +
                    $"(sourceLength={message.Subject.Length}, printableLength={usableCharacters}, " +
                    $"maximum={MaximumPstSubjectLength}, truncatedCharacters={usableCharacters - MaximumPstSubjectLength}, " +
                    $"resultingLength={sanitizedLength}); the first {MaximumPstSubjectLength} printable characters " +
                    "will be written to the PST.");
            }

            if (controlCharacters > 0)
            {
                AddIntegrityWarning(warnings, "subject-control-characters",
                    $"Subject contains {controlCharacters} control character(s) that cannot be written safely " +
                    $"to the PST (sourceLength={message.Subject.Length}, resultingLength={sanitizedLength}); " +
                    "those characters will be removed before writing the subject.");
            }
        }

        if (message.Date is { } messageDate && messageDate < MinimumPstDate)
        {
            AddIntegrityWarning(warnings, "date-adjusted",
                $"Date={messageDate:O} is earlier than the PST minimum {MinimumPstDate:O}; " +
                "it will be clamped to the earliest representable value.");
        }

        bool sourceHasTextBody = !string.IsNullOrWhiteSpace(mime.TextBody)
            || !string.IsNullOrWhiteSpace(mime.HtmlBody);
        if (sourceHasTextBody
            && string.IsNullOrWhiteSpace(message.TextBody)
            && string.IsNullOrWhiteSpace(message.HtmlBody))
        {
            AddIntegrityWarning(warnings, "body-missing",
                $"Neither body alternative contains usable text (textBody={(string.IsNullOrWhiteSpace(message.TextBody) ? "empty" : "present")}, " +
                $"htmlBody={(string.IsNullOrWhiteSpace(message.HtmlBody) ? "empty" : "present")}, bodyParts={mime.BodyParts.Count()}, " +
                $"attachments={message.Attachments.Count}); the PST body will be empty.");
        }

        if (string.IsNullOrWhiteSpace(message.MessageId) && HasNonBlankHeader(mime, "Message-ID"))
        {
            AddIntegrityWarning(warnings, "message-id-missing",
                $"Message-ID is {(mime.Headers.Contains(HeaderId.MessageId) ? "present but invalid/empty" : "absent")} ({DescribeRawHeader(mime, "Message-ID")}); " +
                "the PST internet message identifier will be empty.");
        }

        if (HasNonBlankHeader(mime, "In-Reply-To") && string.IsNullOrWhiteSpace(message.InReplyTo))
        {
            AddIntegrityWarning(warnings, "in-reply-to-invalid",
                $"In-Reply-To is present but MimeKit produced no normalized message ID ({DescribeRawHeader(mime, "In-Reply-To")}); " +
                "the PST In-Reply-To threading property will be left empty rather than being guessed.");
        }

        if (HasNonBlankHeader(mime, "References") && string.IsNullOrWhiteSpace(message.References))
        {
            AddIntegrityWarning(warnings, "references-invalid",
                $"References is present but MimeKit produced no normalized message IDs ({DescribeRawHeader(mime, "References")}); " +
                "the PST References threading property will be left empty rather than being guessed.");
        }
    }

    private static void ValidateRecipientList(
        string fieldName,
        InternetAddressList source,
        IReadOnlyList<MailAddress> mapped,
        IReadOnlyList<MailboxAddress> mailboxes,
        List<string> warnings,
        MimeMessage mime)
    {
        if (!HasMeaningfulAddressSource(fieldName, source, mime))
            return;

        if (source.Any(address => address is not MailboxAddress))
        {
            AddIntegrityWarning(warnings, "recipient-group",
                $"{fieldName} contains a group address ({DescribeAddressList(fieldName, source, mailboxes, mapped, mime)}; " +
                $"{DescribeGroups(source)}); the group container name is not represented in the PST recipient table, " +
                "although any parsed member mailboxes are preserved.");
        }

        if (mailboxes.Count > 0 && mapped.Count == 0)
        {
            AddIntegrityWarning(warnings, "recipient-unmapped",
                $"{fieldName} contains {mailboxes.Count} parsed mailbox address(es) but produced no PST recipients " +
                $"({DescribeAddressList(fieldName, source, mailboxes, mapped, mime)}; {DescribeRawHeader(mime, fieldName)}).");
        }

        int emptyAddresses = mapped.Count(address => string.IsNullOrWhiteSpace(address.Email));
        if (emptyAddresses > 0)
        {
            AddIntegrityWarning(warnings, "recipient-address-empty",
                $"{fieldName} contains {emptyAddresses} mapped recipient(s) with an empty mailbox address " +
                $"({DescribeAddressList(fieldName, source, mailboxes, mapped, mime)}); those recipients will have no PST address value.");
        }
    }

    private static void AddIntegrityWarning(List<string> warnings, string code, string message) =>
        warnings.Add($"[integrity:{code}] {message}");

    private static string DescribeAddressList(
        string fieldName,
        InternetAddressList source,
        IReadOnlyList<MailboxAddress> mailboxes,
        IReadOnlyList<MailAddress> mapped,
        MimeMessage mime)
    {
        int groups = source.OfType<GroupAddress>().Count();
        string headerState = mime.Headers.Contains(fieldName) ? "present" : "absent";
        return $"{fieldName}: header={headerState},parsedAddresses={source.Count},mailboxes={mailboxes.Count},groups={groups},mapped={mapped.Count}";
    }

    private static string DescribeGroups(InternetAddressList source)
    {
        List<GroupAddress> allGroups = source.OfType<GroupAddress>().ToList();
        if (allGroups.Count == 0)
            return "groups=none";

        const int maxGroups = 5;
        string details = string.Join(", ", allGroups.Take(maxGroups).Select(group =>
            $"'{CompactDiagnosticValue(group.Name)}' members={group.Members.Count}"));
        if (allGroups.Count > maxGroups)
            details += $", ... (+{allGroups.Count - maxGroups} more)";
        return $"groups={details}";
    }

    private static string DescribeRawHeader(MimeMessage mime, string fieldName) =>
        DescribeRawHeader(mime.Headers[fieldName], fieldName);

    private static string DescribeRawHeader(string? rawValue, string fieldName)
    {
        if (rawValue is null)
            return $"{fieldName}=absent";

        return $"{fieldName}=present,length={rawValue.Length},value=\"{CompactDiagnosticValue(rawValue)}\"";
    }

    private static string DescribeDeliveredTo(
        MimeMessage mime, IReadOnlyList<MailboxAddress> parsedMailboxes) =>
        $"{DescribeRawHeader(mime, "Delivered-To")},parsedMailboxes={parsedMailboxes.Count}";

    private static string CompactDiagnosticValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "<empty>";

        var builder = new StringBuilder(Math.Min(value.Length, MaximumDiagnosticHeaderLength));
        foreach (char c in value)
        {
            if (builder.Length >= MaximumDiagnosticHeaderLength)
                break;

            builder.Append(c switch
            {
                '\r' or '\n' or '\t' => ' ',
                _ when char.IsControl(c) => '?',
                _ => c,
            });
        }

        if (value.Length > MaximumDiagnosticHeaderLength)
            builder.Append("...");

        return builder.ToString().Trim();
    }

    private static MailImportance ParseImportance(MimeMessage mime)
    {
        if (mime.Importance == MessageImportance.High) return MailImportance.High;
        if (mime.Importance == MessageImportance.Low) return MailImportance.Low;

        // Importance == Normal is treated as "not explicitly set"; check X-Priority fallback.
        // mime.XPriority (distinct from mime.Priority) reads the X-Priority header and handles
        // decorated values like "1 (Highest)" that raw int-parse would miss.
        // XMessagePriority: Highest=1, High=2 → our High; Low=4, Lowest=5 → our Low; Normal=3.
        XMessagePriority xp = mime.XPriority;
        if (xp == XMessagePriority.Highest || xp == XMessagePriority.High) return MailImportance.High;
        if (xp == XMessagePriority.Low || xp == XMessagePriority.Lowest) return MailImportance.Low;

        return MailImportance.Normal;
    }

    private static uint? ParseMozillaStatus(MimeMessage mime)
    {
        string? value = mime.Headers["X-Mozilla-Status"];
        if (value is null) return null;
        return uint.TryParse(value.Trim(), System.Globalization.NumberStyles.HexNumber,
                             System.Globalization.CultureInfo.InvariantCulture, out uint flags)
            ? flags
            : null; // malformed -> null, same fall-through tolerance as before
    }

    private static bool ParseIsRead(MimeMessage mime, uint? mozillaStatus)
    {
        string? gmailLabels = mime.Headers["X-Gmail-Labels"];
        if (gmailLabels is not null)
        {
            // Gmail exports labels in the account's display language; match known locale variants.
            // English: "Unread", Danish: "Ulæste"
            return !gmailLabels.Split(',').Any(t =>
                t.Trim().Equals("Unread", StringComparison.OrdinalIgnoreCase) ||
                t.Trim().Equals("Ulæste", StringComparison.OrdinalIgnoreCase));
        }

        // Mozilla nsMsgMessageFlags.h: Read = 0x0001, New = 0x10000 (NOT 0x0001).
        // https://searchfox.org/comm-central/source/mailnews/base/public/nsMsgMessageFlags.h
        // Bit 0 set -> read; the NEW bit is 0x10000, unrelated. Do NOT "fix" this to == 0.
        if (mozillaStatus is { } flags)
            return (flags & MozillaStatusRead) != 0;

        string? status = mime.Headers["Status"];
        if (status is not null)
            return status.Contains('R', StringComparison.OrdinalIgnoreCase);

        return true;
    }

    /// <summary>
    /// Classifies each entity in <paramref name="mime"/>'s MIME tree as either
    /// body content (skipped) or an attachment/inline resource. Any
    /// unrecognized or non-body leaf MIME part is preserved as an attachment
    /// rather than dropped (a deliberate v1 fidelity choice). Per-attachment
    /// extraction failures are recorded in <paramref name="warnings"/> and the
    /// attachment is dropped without failing the whole message.
    /// </summary>
    internal List<MailAttachment> ExtractAttachments(MimeMessage mime, List<string> warnings)
    {
        var attachments = new List<MailAttachment>();
        int index = 0;

        foreach (MimeEntity entity in mime.BodyParts)
        {
            if (entity is MessagePart messagePart)
            {
                // An embedded message (message/rfc822) is a user attachment only when it
                // is explicitly marked as one. NDR/bounce mail wraps the original
                // undelivered message in a message/rfc822 with no attachment disposition
                // (commonly inside multipart/report, sometimes nested below the top level) —
                // surfacing it as attached-message.eml is the KB-001 phantom. Requiring an
                // explicit attachment disposition or a filename (which legitimate forwards
                // carry) suppresses it regardless of how deeply the report is nested.
                bool isExplicitAttachment =
                    (messagePart.ContentDisposition?.Disposition is { } messageDisposition
                        && messageDisposition.Equals(ContentDisposition.Attachment, StringComparison.OrdinalIgnoreCase))
                    || !string.IsNullOrEmpty(messagePart.ContentDisposition?.FileName)
                    || !string.IsNullOrEmpty(messagePart.ContentType.Name);
                if (!isExplicitAttachment)
                    continue;

                index++;
                const string embeddedFileName = "attached-message.eml";
                const string embeddedMimeType = "message/rfc822";
                try
                {
                    attachments.Add(new MailAttachment
                    {
                        FileName = embeddedFileName,
                        MimeType = embeddedMimeType,
                        Content = WriteEmbeddedMessageContent(messagePart),
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add(FormatDroppedAttachmentWarning(index, embeddedFileName, embeddedMimeType, ex));
                }

                continue;
            }

            if (entity is not MimePart part)
            {
                continue;
            }

            // message/delivery-status (and similar message/* parts) in an NDR are
            // machine-readable delivery metadata, not user-visible attachments. Skip them
            // unless explicitly marked as an attachment — independent of report nesting,
            // so a deeper-nested multipart/report doesn't leak a phantom (KB-001).
            if (part.ContentType.MediaType.Equals("message", StringComparison.OrdinalIgnoreCase)
                && !(part.ContentDisposition?.Disposition is { } messageMetaDisposition
                     && messageMetaDisposition.Equals(ContentDisposition.Attachment, StringComparison.OrdinalIgnoreCase)))
                continue;

            bool isAttachmentDisposition = part.ContentDisposition?.Disposition is { } disposition
                && disposition.Equals(ContentDisposition.Attachment, StringComparison.OrdinalIgnoreCase);
            bool isBodyMimeType = part.ContentType.IsMimeType("text", "plain") || part.ContentType.IsMimeType("text", "html");

            // text/plain and text/html parts are body content — only treat them as
            // attachments if they carry an explicit filename or attachment disposition.
            // A Content-ID alone on a body-typed part is a body-part label (e.g.
            // LinkedIn's "Content-ID: text-body"), not an inline resource reference.
            bool isAttachment = isAttachmentDisposition
                || !string.IsNullOrEmpty(part.FileName)
                || !isBodyMimeType;

            if (!isAttachment)
            {
                continue;
            }

            index++;

            // part.FileName is string? (MimeKit is nullable-annotated); IsNullOrEmpty
            // has [NotNullWhen(false)], so fileName is non-null after this block.
            string? fileName = part.FileName;
            if (string.IsNullOrEmpty(fileName))
            {
                // A missing source filename is not a conversion loss: the bytes remain
                // available under a deterministic generated name, so no warning is emitted.
                fileName = $"attachment-{index}{GuessExtension(part.ContentType)}";
            }

            string mimeType = part.ContentType.MimeType;

            if (part.Content is null)
            {
                warnings.Add(FormatDroppedAttachmentWarning(index, fileName, mimeType,
                    new InvalidOperationException("attachment part has no content data")));
                continue;
            }

            try
            {
                AttachmentContent content = DecodeContent(part);

                string? contentId = string.IsNullOrEmpty(part.ContentId) ? null : NormalizeContentId(part.ContentId);
                bool isInlineDisposition = part.ContentDisposition?.Disposition is { } partDisposition
                    && partDisposition.Equals(ContentDisposition.Inline, StringComparison.OrdinalIgnoreCase);
                bool isReferencedImageWithoutFilename = isAttachmentDisposition
                    && string.IsNullOrEmpty(part.FileName)
                    && part.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase)
                    && contentId is not null
                    && IsContentIdReferencedInHtml(mime, contentId);

                attachments.Add(new MailAttachment
                {
                    FileName = fileName,
                    MimeType = mimeType,
                    ContentId = contentId,
                    ContentLocation = part.ContentLocation?.ToString(),
                    // A Content-ID marks a part inline (body-embedded, hidden, no paperclip) ONLY when
                    // it is not also an explicit attachment. Many mailers attach genuine files (PDFs,
                    // ICS invites, signed parts) that carry BOTH Content-Disposition: attachment and a
                    // Content-ID — those must stay visible, so the explicit attachment disposition wins.
                    // The narrow exception below covers the common Gmail shape: unnamed image,
                    // explicit attachment disposition, CID referenced by the HTML.
                    IsInline = isInlineDisposition
                        || (contentId is not null && !isAttachmentDisposition)
                        || isReferencedImageWithoutFilename,
                    Content = content,
                });
            }
            catch (Exception ex)
            {
                warnings.Add(FormatDroppedAttachmentWarning(index, fileName, mimeType, ex));
            }
        }

        return attachments;
    }

    // Scan measure-only: decode the part to get its exact length, retaining nothing (no buffer, no temp
    // file). Materialize mode keeps the existing memory/temp-spill behavior. Both run the decoder, so a
    // decode failure still throws and is recorded as a dropped-attachment warning by the caller.
    private AttachmentContent DecodeContent(MimeKit.MimePart part)
    {
        if (_measureOnly)
        {
            var counter = new CountingStream();
            part.Content!.DecodeTo(counter);
            return AttachmentContent.FromLengthOnly(counter.BytesWritten);
        }
        using var buffer = new MemoryStream();
        part.Content!.DecodeTo(buffer);
        return ToAttachmentContent(buffer);
    }

    // Same measure-only contract as DecodeContent, for an embedded message/rfc822 attachment: in scan
    // measure-only mode, serialize the embedded message into a CountingStream to capture its exact length
    // while retaining nothing — otherwise a large forwarded .eml would be fully buffered (and temp-spilled
    // past the threshold) per scan worker, defeating the parallel-scan memory-safety guarantee.
    private AttachmentContent WriteEmbeddedMessageContent(MessagePart messagePart)
    {
        // A message/rfc822 part with no parsed inner message has nothing to attach; surface it as a
        // dropped-attachment warning (the caller catches this) rather than NRE-ing on WriteTo.
        MimeMessage message = messagePart.Message
            ?? throw new InvalidOperationException("embedded message/rfc822 part has no message content");

        if (_measureOnly)
        {
            var counter = new CountingStream();
            message.WriteTo(counter);
            return AttachmentContent.FromLengthOnly(counter.BytesWritten);
        }
        using var buffer = new MemoryStream();
        message.WriteTo(buffer);
        return ToAttachmentContent(buffer);
    }

    // Routes attachment bytes to memory (small) or a temp file (large) to bound the
    // SUSTAINED memory of the bounded parse/write queue — a queued temp-backed attachment
    // holds only a path, not its bytes. This does NOT reduce PEAK memory: the part is
    // already fully decoded into `buffer` (a MemoryStream) before this point, and the temp
    // file is read back in full at write time (AttachmentContent.ReadAllBytes -> PstWriter).
    // It is queue-memory hygiene, not end-to-end streaming.
    private AttachmentContent ToAttachmentContent(MemoryStream buffer)
    {
        if (buffer.Length < _tempFileThresholdBytes)
            return AttachmentContent.FromBytes(buffer.ToArray());

        string tempPath = Path.Combine(Path.GetTempPath(), $"mail2pst-{Guid.NewGuid()}");
        buffer.Position = 0;
        try
        {
            using (var tempFile = File.Create(tempPath))
                buffer.CopyTo(tempFile);
        }
        catch (Exception)
        {
            try { File.Delete(tempPath); } catch (Exception) { }
            throw;
        }
        return AttachmentContent.FromTempFile(tempPath, buffer.Length);
    }

    /// <summary>
    /// Formats the warning recorded when an attachment is dropped due to an
    /// extraction failure.
    /// </summary>
    private static string FormatDroppedAttachmentWarning(int index, string fileName, string mimeType, Exception ex) =>
        $"Dropped attachment #{index} '{fileName}' ({mimeType}): cause={ex.Message}; " +
        "action=the attachment was not written to the PST, while the rest of the message continued.";

    /// <summary>
    /// Returns a best-effort file extension (including the leading '.') for a
    /// part with no filename, derived from its MIME subtype (e.g. "image/png"
    /// -> ".png"). Returns "" for "octet-stream" or for subtypes that aren't a
    /// single alphanumeric token (e.g. "svg+xml"), since those wouldn't make a
    /// sensible bare extension.
    /// </summary>
    private static string GuessExtension(ContentType contentType)
    {
        string subtype = contentType.MediaSubtype;
        if (string.IsNullOrEmpty(subtype) || !subtype.All(char.IsLetterOrDigit))
        {
            return string.Empty;
        }

        if (subtype.Equals("octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return "." + subtype.ToLowerInvariant();
    }

    /// <summary>
    /// Strips the optional "cid:" prefix and surrounding angle brackets from a
    /// Content-ID/cid: reference, so stored ContentId values can be compared
    /// directly against each other regardless of which form they came from.
    /// Note: <c>part.ContentId</c> (from MIME headers) never has a "cid:"
    /// prefix in practice — that prefix only appears in HTML
    /// <c>src="cid:..."</c> references. The "cid:"-stripping branch here is
    /// defensive/future-proofing, for if such an HTML reference is ever
    /// normalized through this same helper.
    /// </summary>
    private static string NormalizeContentId(string contentId)
    {
        contentId = contentId.Trim();

        if (contentId.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
        {
            contentId = contentId.Substring(4);
        }

        if (contentId.StartsWith("<") && contentId.EndsWith(">"))
        {
            contentId = contentId.Substring(1, contentId.Length - 2);
        }

        return contentId;
    }

    private static bool IsContentIdReferencedInHtml(MimeMessage mime, string contentId) =>
        !string.IsNullOrEmpty(mime.HtmlBody)
        && mime.HtmlBody.Contains(contentId, StringComparison.OrdinalIgnoreCase);

    private static MailAddress? ToMailAddress(MailboxAddress? mailbox) =>
        mailbox is null ? null : ToMailAddressNonNull(mailbox);

    private static MailAddress ToMailAddressNonNull(MailboxAddress mailbox) =>
        new() { Name = mailbox.Name, Email = mailbox.Address };
}
