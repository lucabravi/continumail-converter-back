// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Writing;

public class PlannedMessage
{
    public MailMessage Message { get; set; } = new();
    public IReadOnlyList<string> TargetFolderPath { get; set; } = System.Array.Empty<string>();
    /// <summary>Additional physical destinations for an intentional multi-folder copy (currently
    /// Gmail ExactFolders mode). The writer writes every destination before disposing attachments.</summary>
    public IReadOnlyList<IReadOnlyList<string>> AdditionalTargetFolderPaths { get; set; } =
        System.Array.Empty<IReadOnlyList<string>>();
}
