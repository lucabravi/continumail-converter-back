// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later
#nullable enable

using System;

namespace Mail2Pst.Core.Parsing.Mbox;

/// <summary>One result from the mbox boundary engine: either a materialized message buffer
/// (or <c>null</c> in count/offset mode) OR an "oversized" marker for a message that exceeded the
/// max-message-size cap and was skipped without being fully buffered.</summary>
internal readonly struct MessageChunk
{
    public SpillableMessageBuffer? Buffer { get; }
    public bool IsOversized { get; }
    /// <summary>Approximate content bytes seen before the message was cut off (for the skip message).</summary>
    public long OversizedBytes { get; }
    public DateTimeOffset? EnvelopeDate { get; }

    private MessageChunk(
        SpillableMessageBuffer? buffer,
        bool oversized,
        long bytes,
        DateTimeOffset? envelopeDate)
    {
        Buffer = buffer;
        IsOversized = oversized;
        OversizedBytes = bytes;
        EnvelopeDate = envelopeDate;
    }

    public static MessageChunk Ok(SpillableMessageBuffer? buffer, DateTimeOffset? envelopeDate) =>
        new(buffer, false, 0, envelopeDate);

    public static MessageChunk Oversized(long bytes, DateTimeOffset? envelopeDate) =>
        new(null, true, bytes, envelopeDate);
}
