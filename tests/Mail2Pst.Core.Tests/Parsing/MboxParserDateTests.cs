// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using Mail2Pst.Core.Parsing;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing;

public class MboxParserDateTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Parse_MessageWithoutDateHeader_UsesEnvelopeDateWithoutWarning()
    {
        IMailSourceParser parser = ParserRegistry.Get("mbox");
        ParseResult result = parser.Parse(Fixture("mbox-no-dates.mbox")).Single();

        Assert.True(result.Success);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), result.Message!.Date);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_MessageWithDateHeader_PopulatesDate()
    {
        IMailSourceParser parser = ParserRegistry.Get("mbox");
        ParseResult result = parser.Parse(Fixture("sample.mbox")).First();

        Assert.True(result.Success);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 10, 0, 0, TimeSpan.Zero), result.Message!.Date);
    }

    [Fact]
    public void Parse_ValidDateHeaderWinsOverEnvelopeFallback()
    {
        const string content =
            "From sender@example.com Mon Jan  1 00:00:00 2020\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: Tue, 2 Jan 2024 03:04:05 +0200\r\n" +
            "Subject: valid date\r\n\r\nbody\r\n";
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            ParseResult result = new MboxParser().Parse(path).Single();

            Assert.True(result.Success);
            Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), result.Message!.Date);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A present-but-unparseable Date header makes MimeKit return MinValue
    // (0001-01-01). The MBOX envelope postmark is a safe fallback for this
    // source and must not generate a warning.
    [Fact]
    public void Parse_MessageWithUnparseableDateHeader_UsesEnvelopeDateWithoutWarning()
    {
        IMailSourceParser parser = ParserRegistry.Get("mbox");
        ParseResult result = parser.Parse(Fixture("mbox-bad-date.mbox")).Single();

        Assert.True(result.Success);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), result.Message!.Date);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Parse_LocalizedDateWithoutYear_UsesEnvelopeDateWithoutWarning()
    {
        const string content =
            "From sender@example.com Tue May 16 10:39:08 +0200 2024\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: mar, 16 mag 10:39:08 +0200\r\n" +
            "Subject: localized date\r\n\r\nbody\r\n";
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            ParseResult result = new MboxParser().Parse(path).Single();

            Assert.True(result.Success);
            Assert.Equal(new DateTimeOffset(2024, 5, 16, 10, 39, 8, TimeSpan.FromHours(2)), result.Message!.Date);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_InvalidDateWithoutEnvelope_UsesReceivedAndWarns()
    {
        string content =
            "From sender@example.com not-a-postmark\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: not-a-valid-date\r\n" +
            "Received: by mx.example.com; Tue, 16 May 2023 10:39:08 +0200\r\n" +
            "Subject: received fallback\r\n\r\nbody\r\n";
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            ParseResult result = new MboxParser().Parse(path).Single();

            Assert.True(result.Success);
            Assert.Equal(new DateTimeOffset(2023, 5, 16, 10, 39, 8, TimeSpan.FromHours(2)), result.Message!.Date);
            string warning = Assert.Single(result.Warnings,
                item => item.StartsWith("[integrity:date-received-fallback]", StringComparison.Ordinal));
            Assert.Contains("Received", warning);
            Assert.DoesNotContain(result.Warnings, item => item.StartsWith("[integrity:date-missing]", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_InvalidDateWithoutAnyFallback_WarnsDateMissing()
    {
        string content =
            "From sender@example.com not-a-postmark\r\n" +
            "From: sender@example.com\r\n" +
            "To: recipient@example.com\r\n" +
            "Date: not-a-valid-date\r\n" +
            "Subject: no fallback\r\n\r\nbody\r\n";
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            ParseResult result = new MboxParser().Parse(path).Single();

            Assert.True(result.Success);
            Assert.Null(result.Message!.Date);
            Assert.Contains(result.Warnings,
                item => item.StartsWith("[integrity:date-missing]", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
