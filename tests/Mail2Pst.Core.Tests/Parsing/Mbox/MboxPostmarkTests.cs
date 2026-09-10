// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Text;
using Mail2Pst.Core.Parsing.Mbox;
using Xunit;

namespace Mail2Pst.Core.Tests.Parsing.Mbox;

public class MboxPostmarkTests
{
    private static bool Sniff(string line) =>
        MboxPostmark.IsEnvelopePostmark(Encoding.ASCII.GetBytes(line));

    [Fact]
    public void ValidAsctimePostmark_Matches()
        => Assert.True(Sniff("From sender@host Mon Jan  1 00:00:00 2020"));

    [Fact]
    public void ValidPostmarkWithTimezone_Matches()
        => Assert.True(Sniff("From a@b Tue Feb 10 09:30:15 PST 2021"));

    [Fact]
    public void BodyLineThatStartsWithFrom_DoesNotMatch()
        => Assert.False(Sniff("From x foo bar 12 10:00:00 2020"));

    [Fact]
    public void PlainText_DoesNotMatch()
        => Assert.False(Sniff("Hello, this is not an mbox line"));

    [Fact]
    public void NumericTimezonePostmark_ParsesDate()
    {
        bool parsed = MboxPostmark.TryParseDate(
            Encoding.ASCII.GetBytes("From sender@host Tue Sep 01 13:10:38 +0000 2026"),
            out DateTimeOffset date);

        Assert.True(parsed);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 13, 10, 38, TimeSpan.Zero), date);
    }

    [Fact]
    public void PostmarkWithoutTimezone_ParsesDateAsUtcWallClock()
    {
        bool parsed = MboxPostmark.TryParseDate(
            Encoding.ASCII.GetBytes("From sender@host Thu Jan  1 00:00:00 2020"),
            out DateTimeOffset date);

        Assert.True(parsed);
        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), date);
    }
}
