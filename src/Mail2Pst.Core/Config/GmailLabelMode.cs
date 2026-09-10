// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
namespace Mail2Pst.Core.Config;

/// <summary>Controls how per-message <c>X-Gmail-Labels</c> metadata is represented.</summary>
public enum GmailLabelMode
{
    /// <summary>Ignore Gmail labels and preserve the legacy source-folder mapping.</summary>
    Off,

    /// <summary>Write one physical message, preserve every label as an Outlook category, and use
    /// one deterministic label as the physical subfolder.</summary>
    Compact,

    /// <summary>Write one physical PST item in every label folder. This can multiply output size.</summary>
    ExactFolders,
}
