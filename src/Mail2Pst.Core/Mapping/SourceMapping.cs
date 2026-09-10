// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Config;

namespace Mail2Pst.Core.Mapping;

public class SourceMapping
{
    public SourceConfig Source { get; set; } = new();
    public IReadOnlyList<string> TargetFolderPath { get; set; } = Array.Empty<string>();
    public GmailLabelMode GmailLabelMode { get; set; } = GmailLabelMode.Compact;
    public IReadOnlyList<string> GmailPrimaryLabelPriority { get; set; } = Array.Empty<string>();
}
