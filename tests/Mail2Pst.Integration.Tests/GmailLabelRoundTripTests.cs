// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mail2Pst.Core;
using Mail2Pst.Core.Config;
using Mail2Pst.Core.Reporting;
using MimeKit;
using Xunit;

namespace Mail2Pst.Integration.Tests;

public class GmailLabelRoundTripTests
{
    private static string WriteMbox(string dir, string labels, bool withAttachment = false)
    {
        string path = Path.Combine(dir, "gmail.mbox");
        var message = new MimeMessage
        {
            Subject = "Gmail labels",
            Date = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            MessageId = "gmail-labels@example.com",
        };
        message.From.Add(new MailboxAddress("Sender", "sender@example.com"));
        message.To.Add(new MailboxAddress("Recipient", "recipient@example.com"));
        message.Headers.Add("X-Gmail-Labels", labels);
        var body = new BodyBuilder { TextBody = "body" };
        if (withAttachment)
            body.Attachments.Add("proof.bin", new byte[] { 1, 2, 3, 4 },
                ContentType.Parse("application/octet-stream"));
        message.Body = body.ToMessageBody();

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        byte[] postmark = Encoding.ASCII.GetBytes("From sender@example.com Fri Jan  2 03:04:05 2026\r\n");
        stream.Write(postmark);
        message.WriteTo(stream);
        stream.WriteByte((byte)'\r');
        stream.WriteByte((byte)'\n');
        return path;
    }

    private static (IReadOnlyList<ReadFolder> Folders, ConversionReport Report) Convert(
        string dir, string mbox, GmailLabelMode mode, params string[] priority)
    {
        string output = Path.Combine(dir, "output");
        Directory.CreateDirectory(output);
        var config = new ConversionConfig
        {
            Outputs =
            {
                new OutputGroupConfig
                {
                    Name = "Out",
                    MaxSizeMB = 100,
                    GmailLabelMode = mode,
                    GmailPrimaryLabelPriority = priority.ToList(),
                    Sources =
                    {
                        new SourceConfig
                        {
                            Path = mbox,
                            Type = "mbox",
                            TargetFolderPath = new List<string> { "Takeout" },
                        },
                    },
                },
            },
        };

        ConversionReport report = new ConversionRunner().Run(config, output);
        return (PstReader.Read(report.OutputFiles), report);
    }

    private static string NewDir()
    {
        string path = Path.Combine(Path.GetTempPath(), "mail2pst-gmail-labels-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Compact_Default_WritesOnePhysicalCopyInNestedPrimaryAndAllCategories()
    {
        string dir = NewDir();
        try
        {
            string mbox = WriteMbox(dir, "Posta in arrivo,Importanti,INBOX/AMMINISTRAZIONE");

            var (folders, report) = Convert(dir, mbox, GmailLabelMode.Compact);

            ReadBackMessage read = Assert.Single(folders
                .Single(folder => folder.DisplayPath == "Takeout / INBOX / AMMINISTRAZIONE").Messages);
            Assert.Equal(
                new[] { "Posta in arrivo", "Importanti", "INBOX/AMMINISTRAZIONE" },
                read.Categories);
            Assert.Equal(1, report.ConvertedCount);
            Assert.Equal(new GmailLabelSummary(1, 1, 1, 0, 3, 1, 0), report.GmailLabels);
            Assert.Empty(report.Warnings.Where(warning =>
                warning.Reason.Contains("gmail-label-exact-mode", StringComparison.Ordinal)));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Compact_ConfiguredPrioritySelectsRequestedPhysicalFolder()
    {
        string dir = NewDir();
        try
        {
            string mbox = WriteMbox(dir, "Posta in arrivo,INBOX/AMMINISTRAZIONE");

            var (folders, _) = Convert(dir, mbox, GmailLabelMode.Compact, "Posta in arrivo");

            Assert.Single(folders.Single(folder => folder.DisplayPath == "Takeout / Posta in arrivo").Messages);
            Assert.DoesNotContain(folders,
                folder => folder.DisplayPath == "Takeout / INBOX / AMMINISTRAZIONE" && folder.Messages.Count > 0);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ExactFolders_WritesEveryCopyBeforeAttachmentDisposalAndReportsSpaceWarnings()
    {
        string dir = NewDir();
        try
        {
            string mbox = WriteMbox(dir, "Posta in arrivo,Importanti,INBOX/AMMINISTRAZIONE",
                withAttachment: true);

            var (folders, report) = Convert(dir, mbox, GmailLabelMode.ExactFolders);

            string[] expectedPaths =
            {
                "Takeout / Posta in arrivo",
                "Takeout / Importanti",
                "Takeout / INBOX / AMMINISTRAZIONE",
            };
            foreach (string path in expectedPaths)
            {
                ReadBackMessage copy = Assert.Single(folders.Single(folder => folder.DisplayPath == path).Messages);
                Assert.Equal(new[] { "proof.bin" }, copy.AttachmentNames);
                Assert.Equal(
                    new[] { "Posta in arrivo", "Importanti", "INBOX/AMMINISTRAZIONE" },
                    copy.Categories);
            }

            Assert.Equal(3, report.ConvertedCount);
            Assert.Equal(new GmailLabelSummary(1, 1, 1, 0, 3, 3, 2), report.GmailLabels);
            Assert.Contains(report.Warnings, warning => warning.Reason.StartsWith(
                "[integrity:gmail-label-exact-mode-space]", StringComparison.Ordinal));
            Assert.Contains(report.Warnings, warning => warning.Reason.StartsWith(
                "[integrity:gmail-label-exact-mode-copy-count]", StringComparison.Ordinal));
        }
        finally { Directory.Delete(dir, true); }
    }
}
