// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Models;

namespace Mail2Pst.Core.Mapping;

/// <summary>Maps Gmail Takeout labels to Outlook categories and physical PST folder paths.</summary>
public static class GmailLabelMapper
{
    // PidNameKeywords: every string in the PT_MV_UNICODE value must be shorter than 256 chars.
    public const int MaximumOutlookCategoryLength = 255;

    public static GmailLabelMappingResult Map(
        MailMessage message,
        IReadOnlyList<string> baseTargetFolderPath,
        GmailLabelMode mode,
        IReadOnlyList<string>? primaryLabelPriority = null,
        bool preserveBaseFolder = false)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(baseTargetFolderPath);

        if (mode == GmailLabelMode.Off || message.GmailLabels.Count == 0)
            return new GmailLabelMappingResult(
                new[] { baseTargetFolderPath }, Array.Empty<string>(), message.GmailLabels.Count);

        var warnings = new List<string>();
        AddCategories(message, warnings);

        // Existing explicit routing (currently JunkHandling.Folder) remains authoritative. Labels
        // are still preserved as categories, but must not silently undo a route the caller requested.
        if (preserveBaseFolder)
            return new GmailLabelMappingResult(
                new[] { baseTargetFolderPath }, warnings, message.GmailLabels.Count);

        IEnumerable<string> folderLabels = mode == GmailLabelMode.ExactFolders
            ? message.GmailLabels
            : new[] { SelectPrimaryLabel(message.GmailLabels, primaryLabelPriority) };

        var paths = new List<IReadOnlyList<string>>();
        var pathLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string label in folderLabels)
        {
            IReadOnlyList<string>? path = BuildFolderPath(baseTargetFolderPath, label, warnings);
            if (path is null)
                continue;

            string key = FolderPathKey.Join(path);
            if (pathLabels.TryGetValue(key, out string? previousLabel))
            {
                warnings.Add(
                    $"[integrity:gmail-label-folder-collision] Gmail labels '{previousLabel}' and '{label}' " +
                    $"map to the same PST folder '{FolderPathDisplay.Join(path)}'; only one physical copy will be written there, " +
                    "while both original labels remain preserved as categories.");
                continue;
            }

            pathLabels[key] = label;
            paths.Add(path);
        }

        if (paths.Count == 0)
        {
            warnings.Add(
                "[integrity:gmail-label-folder-unrepresentable] Gmail labels are present but none can be " +
                $"represented below '{FolderPathDisplay.Join(baseTargetFolderPath)}'; the message remains in the source folder " +
                "and its usable labels remain preserved as categories.");
            paths.Add(baseTargetFolderPath);
        }

        return new GmailLabelMappingResult(paths, warnings, message.GmailLabels.Count);
    }

    private static string SelectPrimaryLabel(
        IReadOnlyList<string> labels, IReadOnlyList<string>? primaryLabelPriority)
    {
        foreach (string preferred in primaryLabelPriority ?? Array.Empty<string>())
        {
            string? match = labels.FirstOrDefault(label =>
                string.Equals(label, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        // A source path explicitly encoded with '/' carries more hierarchy than a flat label.
        // Prefer it, then preserve Gmail's original label order as the deterministic fallback.
        return labels.FirstOrDefault(label => label.Contains('/', StringComparison.Ordinal)) ?? labels[0];
    }

    private static IReadOnlyList<string>? BuildFolderPath(
        IReadOnlyList<string> basePath, string label, List<string> warnings)
    {
        string[] rawSegments = label.Split('/', StringSplitOptions.None);
        if (basePath.Count + rawSegments.Length > FolderNameValidator.MaxDepth)
        {
            warnings.Add(
                $"[integrity:gmail-label-folder-depth] Gmail label '{label}' requires folder depth " +
                $"{basePath.Count + rawSegments.Length}, above the supported maximum {FolderNameValidator.MaxDepth}; " +
                "it will remain a category but cannot be used as a PST folder path.");
            return null;
        }

        var result = new List<string>(basePath.Count + rawSegments.Length);
        result.AddRange(basePath);
        for (int i = 0; i < rawSegments.Length; i++)
        {
            string raw = rawSegments[i];
            string sanitized = FolderNameValidator.Sanitize(raw, $"Label {i + 1}");
            if (!string.Equals(raw, sanitized, StringComparison.Ordinal))
            {
                warnings.Add(
                    $"[integrity:gmail-label-folder-adjusted] Gmail label '{label}' segment {i + 1} " +
                    $"('{raw}') is not a valid PST folder name; using '{sanitized}'. The original full label remains a category.");
            }
            result.Add(sanitized);
        }

        return result;
    }

    private static void AddCategories(MailMessage message, List<string> warnings)
    {
        var seen = new HashSet<string>(message.Categories, StringComparer.OrdinalIgnoreCase);
        foreach (string label in message.GmailLabels)
        {
            string adjusted = AdjustCategory(label);
            if (adjusted.Length == 0)
            {
                warnings.Add(
                    $"[integrity:gmail-label-category-unrepresentable] Gmail label '{label}' contains no " +
                    "Outlook-compatible category characters and cannot be written as a category.");
                continue;
            }

            if (!string.Equals(label, adjusted, StringComparison.Ordinal))
            {
                warnings.Add(
                    $"[integrity:gmail-label-category-adjusted] Gmail label '{label}' cannot be written verbatim " +
                    $"as an Outlook category (sourceLength={label.Length}, maximum={MaximumOutlookCategoryLength}); " +
                    $"using '{adjusted}'.");
            }

            if (seen.Add(adjusted))
                message.Categories.Add(adjusted);
        }
    }

    private static string AdjustCategory(string label)
    {
        string printable = string.Concat(label.Select(character =>
            character < 0x20 || character == 0x7F ? ' ' : character)).Trim();
        if (printable.Length <= MaximumOutlookCategoryLength)
            return printable;

        string suffix = "~" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(label)))[..8];
        return printable[..(MaximumOutlookCategoryLength - suffix.Length)] + suffix;
    }
}

public sealed record GmailLabelMappingResult(
    IReadOnlyList<IReadOnlyList<string>> TargetFolderPaths,
    IReadOnlyList<string> Warnings,
    int LabelAssignments);
