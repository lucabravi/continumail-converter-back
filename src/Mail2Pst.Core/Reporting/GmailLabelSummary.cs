// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
namespace Mail2Pst.Core.Reporting;

/// <summary>Aggregate source-label and planned-copy counts for Gmail-labelled sources.</summary>
public sealed record GmailLabelSummary(
    int SourcesDetected,
    long SourceMessages,
    long LabeledMessages,
    long MessagesWithoutLabels,
    long LabelAssignments,
    long PstCopiesPlanned,
    long DuplicateCopiesPlanned);
