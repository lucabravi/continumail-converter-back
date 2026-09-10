// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Scanning;
using Xunit;

namespace Mail2Pst.Core.Tests.Scanning;

public class ScanRunnerTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Scan_SingleSource_PopulatesTotalsAndOneSourceRow()
    {
        var runner = new ScanRunner();
        ScanReport report = runner.Scan(Fixture("sample.mbox"), "mbox");

        Assert.Equal(2, report.Totals.Messages);
        Assert.Equal(1, report.Totals.Sources);
        Assert.True(report.Totals.Bytes > 0);
        Assert.True(report.Totals.SourceBytes > 0);

        SourceScanResult src = Assert.Single(report.Sources);
        Assert.Equal("sample", src.Id);
        Assert.Equal("sample", src.DisplayName);
        Assert.Equal(2, src.Messages);
        Assert.True(src.EstimatedBytes > 0);
        Assert.Equal(new FileInfo(Fixture("sample.mbox")).Length, src.SourceBytes);
        Assert.Equal(0, src.Warnings);
        Assert.Equal(0, src.Skipped);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero), src.DateFrom);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 11, 30, 0, TimeSpan.Zero), src.DateTo);

        Assert.Empty(report.Skipped);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Scan_MultipleSources_AggregatesTotals()
    {
        var runner = new ScanRunner();
        ScanReport report = runner.Scan(
            new[] { Fixture("sample.mbox"), Fixture("mbox-with-broken-attachment.mbox") }, "mbox");

        Assert.Equal(2, report.Totals.Sources);
        Assert.Equal(3, report.Totals.Messages); // 2 + 1
        Assert.Equal(2, report.Sources.Count);

        SourceScanResult broken = report.Sources.Single(s => s.Id == "mbox-with-broken-attachment");
        Assert.Equal(1, broken.Messages);
        Assert.Equal(1, broken.Warnings);

        // Detailed top-level warning is preserved and tagged with its source path.
        Assert.Single(report.Warnings);
        Assert.Contains("broken.bin", report.Warnings[0].Reason);
    }

    [Fact]
    public void Scan_SourceWithoutDateHeader_UsesEnvelopeDate()
    {
        var runner = new ScanRunner();
        ScanReport report = runner.Scan(
            new[] { Fixture("mbox-no-dates.mbox"), Fixture("sample.mbox") }, "mbox");

        SourceScanResult noDates = report.Sources.Single(s => s.Id == "mbox-no-dates");
        Assert.Equal(1, noDates.Messages);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), noDates.DateFrom);
        Assert.Equal(noDates.DateFrom, noDates.DateTo);
        Assert.Equal(0, noDates.Warnings);

        // The dated source in the same run is unaffected.
        SourceScanResult dated = report.Sources.Single(s => s.Id == "sample");
        Assert.NotNull(dated.DateFrom);
        Assert.NotNull(dated.DateTo);
    }

    [Fact]
    public void Scan_EmptySource_StillAppearsAsZeroCountRow()
    {
        var runner = new ScanRunner();
        ScanReport report = runner.Scan(new[] { Fixture("empty.mbox") }, "mbox");

        SourceScanResult src = Assert.Single(report.Sources);
        Assert.Equal("empty", src.Id);
        Assert.Equal(0, src.Messages);
        Assert.Equal(0, src.EstimatedBytes);
        Assert.Null(src.DateFrom);
        Assert.Null(src.DateTo);
        Assert.Equal(0, report.Totals.Messages);
        Assert.Equal(1, report.Totals.Sources);
    }

    [Fact]
    public void Scan_DuplicateFileNames_SuffixesCollidingIds()
    {
        string dirA = Path.Combine(Path.GetTempPath(), "mail2pst-scan-" + Guid.NewGuid());
        string dirB = Path.Combine(Path.GetTempPath(), "mail2pst-scan-" + Guid.NewGuid());
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        try
        {
            string a = Path.Combine(dirA, "Inbox.mbox");
            string b = Path.Combine(dirB, "Inbox.mbox");
            File.Copy(Fixture("sample.mbox"), a);
            File.Copy(Fixture("sample.mbox"), b);

            var runner = new ScanRunner();
            ScanReport report = runner.Scan(new[] { a, b }, "mbox");

            Assert.Equal(new[] { "inbox", "inbox-2" }, report.Sources.Select(s => s.Id).ToArray());
        }
        finally
        {
            Directory.Delete(dirA, true);
            Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public void Scan_EofBugFixture_StillCountsBothMessages()
    {
        var runner = new ScanRunner();
        ScanReport report = runner.Scan(Fixture("mbox-eof-bug.mbox"), "mbox");
        Assert.Equal(2, Assert.Single(report.Sources).Messages);
    }

    [Fact]
    public void Scan_MissingFile_ThrowsFileNotFoundException()
    {
        var runner = new ScanRunner();
        Assert.Throws<FileNotFoundException>(() => runner.Scan("does-not-exist.mbox", "mbox"));
    }

    [Fact]
    public void Scan_UnsupportedType_ThrowsNotSupportedException()
    {
        var runner = new ScanRunner();
        Assert.Throws<NotSupportedException>(() => runner.Scan("any.eml", "eml"));
    }
}
