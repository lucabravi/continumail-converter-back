// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Mapping;
using Mail2Pst.Core.Models;
using Xunit;

namespace Mail2Pst.Core.Tests.Mapping;

public class GmailLabelMapperTests
{
    private static MailMessage Message(params string[] labels) => new()
    {
        GmailLabels = labels.ToList(),
        HasGmailLabelsHeader = true,
    };

    [Fact]
    public void Compact_DefaultsToNestedPrimaryAndPreservesEveryLabelAsCategory()
    {
        MailMessage message = Message("Posta in arrivo", "Importanti", "INBOX/AMMINISTRAZIONE");

        GmailLabelMappingResult result = GmailLabelMapper.Map(
            message, new[] { "Takeout" }, GmailLabelMode.Compact);

        Assert.Equal(new[] { "Takeout", "INBOX", "AMMINISTRAZIONE" }, Assert.Single(result.TargetFolderPaths));
        Assert.Equal(message.GmailLabels, message.Categories);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Compact_ConfiguredPriorityWinsBeforeNestedFallback()
    {
        MailMessage message = Message("Posta in arrivo", "INBOX/AMMINISTRAZIONE");

        GmailLabelMappingResult result = GmailLabelMapper.Map(
            message, new[] { "Takeout" }, GmailLabelMode.Compact,
            new[] { "Posta inviata", "Posta in arrivo" });

        Assert.Equal(new[] { "Takeout", "Posta in arrivo" }, Assert.Single(result.TargetFolderPaths));
    }

    [Fact]
    public void ExactFolders_ReturnsOneDestinationPerDistinctLabelAndKeepsCategories()
    {
        MailMessage message = Message("Posta in arrivo", "Importanti", "INBOX/AMMINISTRAZIONE");

        GmailLabelMappingResult result = GmailLabelMapper.Map(
            message, new[] { "Takeout" }, GmailLabelMode.ExactFolders);

        Assert.Equal(3, result.TargetFolderPaths.Count);
        Assert.Contains(result.TargetFolderPaths,
            path => path.SequenceEqual(new[] { "Takeout", "Posta in arrivo" }));
        Assert.Contains(result.TargetFolderPaths,
            path => path.SequenceEqual(new[] { "Takeout", "INBOX", "AMMINISTRAZIONE" }));
        Assert.Equal(message.GmailLabels, message.Categories);
    }

    [Fact]
    public void Off_PreservesLegacyFolderAndDoesNotAddCategories()
    {
        MailMessage message = Message("Inbox", "Important");

        GmailLabelMappingResult result = GmailLabelMapper.Map(
            message, new[] { "Legacy" }, GmailLabelMode.Off);

        Assert.Equal(new[] { "Legacy" }, Assert.Single(result.TargetFolderPaths));
        Assert.Empty(message.Categories);
    }

    [Fact]
    public void PreserveBaseFolder_KeepsExplicitRoutingButStillAddsCategories()
    {
        MailMessage message = Message("Inbox", "Important");

        GmailLabelMappingResult result = GmailLabelMapper.Map(
            message, new[] { "Junk Email" }, GmailLabelMode.ExactFolders,
            preserveBaseFolder: true);

        Assert.Equal(new[] { "Junk Email" }, Assert.Single(result.TargetFolderPaths));
        Assert.Equal(new[] { "Inbox", "Important" }, message.Categories);
    }

    [Fact]
    public void OversizedCategory_IsDeterministicallyShortenedAndWarned()
    {
        string label = new('x', 300);
        MailMessage message = Message(label);

        GmailLabelMappingResult result = GmailLabelMapper.Map(
            message, new[] { "Takeout" }, GmailLabelMode.Compact);

        string category = Assert.Single(message.Categories);
        Assert.Equal(GmailLabelMapper.MaximumOutlookCategoryLength, category.Length);
        Assert.EndsWith("~0D4E2CA9", category, StringComparison.Ordinal);
        Assert.Contains(result.Warnings,
            warning => warning.StartsWith("[integrity:gmail-label-category-adjusted]", StringComparison.Ordinal));
    }

    [Fact]
    public void ExactFolders_SanitizedPathCollisionWritesOneCopyAndWarns()
    {
        MailMessage message = Message("A//B", "A/Label 2/B");

        GmailLabelMappingResult result = GmailLabelMapper.Map(
            message, new[] { "Takeout" }, GmailLabelMode.ExactFolders);

        Assert.Single(result.TargetFolderPaths);
        Assert.Contains(result.Warnings,
            warning => warning.StartsWith("[integrity:gmail-label-folder-collision]", StringComparison.Ordinal));
    }
}
