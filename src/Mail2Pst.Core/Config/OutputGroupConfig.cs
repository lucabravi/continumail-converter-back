// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System.Collections.Generic;

namespace Mail2Pst.Core.Config;

public class OutputGroupConfig
{
    public string Name { get; set; } = string.Empty;
    public long MaxSizeMB { get; set; } = 20000;
    public FolderMappingMode FolderMapping { get; set; } = FolderMappingMode.Mirror;

    // Gmail Takeout stores folder/label membership per message in X-Gmail-Labels. Compact is the
    // safe default: one physical item plus categories. ExactFolders is explicit because it duplicates
    // message bodies and attachments for multi-label messages.
    public GmailLabelMode GmailLabelMode { get; set; } = GmailLabelMode.Compact;

    // Optional exact label names in preferred primary-folder order for Compact mode. The first
    // configured name present on a message wins; otherwise a nested label wins, then source order.
    public List<string> GmailPrimaryLabelPriority { get; set; } = new();

    // When true (default), empty source files still produce empty folders in the
    // PST. A GUI/CLI can surface this as a per-conversion choice.
    public bool IncludeEmptyFolders { get; set; } = true;

    public List<SourceConfig> Sources { get; set; } = new();
    public List<ContactSourceConfig> Contacts { get; set; } = new();
    public List<CalendarSourceConfig> Calendars { get; set; } = new();
}
