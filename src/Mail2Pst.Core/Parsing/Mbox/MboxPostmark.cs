// SPDX-FileCopyrightText: 2026 Aksel Visby (ContinuMail)
// SPDX-License-Identifier: GPL-3.0-or-later

#nullable enable
using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Mail2Pst.Core.Parsing.Mbox;

/// <summary>
/// Shared mbox "From " envelope-postmark detection. Extracted verbatim from MboxParser so the
/// parser and discovery's content-sniff use ONE implementation — "discovery says mbox" can never
/// disagree with "parser says mbox". Matches the asctime form, e.g.
/// "From sender@host Mon Jan  1 00:00:00 2020" (optional timezone token before the year), with
/// day-of-week and month validated against the English abbreviations (exact case).
/// </summary>
internal static class MboxPostmark
{
    private static readonly Regex EnvelopePostmark = new Regex(
        @"^From \S+ (?<weekday>Mon|Tue|Wed|Thu|Fri|Sat|Sun) (?<month>Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+(?<day>\d{1,2}) (?<hour>\d{2}):(?<minute>\d{2}):(?<second>\d{2})(?:\s+(?<zone>\S+))?\s+(?<year>\d{4})\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool IsEnvelopePostmark(ReadOnlySpan<byte> line)
    {
        // From lines are ASCII; decode and drop the trailing newline before matching.
        string text = Encoding.ASCII.GetString(line).TrimEnd('\r', '\n');
        return EnvelopePostmark.IsMatch(text);
    }

    internal static bool TryParseDate(ReadOnlySpan<byte> line, out DateTimeOffset date)
    {
        date = default;
        string text = Encoding.ASCII.GetString(line).TrimEnd('\r', '\n');
        Match match = EnvelopePostmark.Match(text);
        if (!match.Success
            || !int.TryParse(match.Groups["day"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int day)
            || !int.TryParse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int year)
            || !int.TryParse(match.Groups["hour"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int hour)
            || !int.TryParse(match.Groups["minute"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minute)
            || !int.TryParse(match.Groups["second"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int second)
            || !TryParseMonth(match.Groups["month"].Value, out int month)
            || !TryParseOffset(match.Groups["zone"].Value, out TimeSpan offset))
        {
            return false;
        }

        try
        {
            date = new DateTimeOffset(year, month, day, hour, minute, second, offset);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool TryParseMonth(string value, out int month)
    {
        month = value switch
        {
            "Jan" => 1,
            "Feb" => 2,
            "Mar" => 3,
            "Apr" => 4,
            "May" => 5,
            "Jun" => 6,
            "Jul" => 7,
            "Aug" => 8,
            "Sep" => 9,
            "Oct" => 10,
            "Nov" => 11,
            "Dec" => 12,
            _ => 0,
        };
        return month > 0;
    }

    private static bool TryParseOffset(string value, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (string.IsNullOrEmpty(value))
            return true;

        if ((value.Length == 5 || value.Length == 6)
            && (value[0] == '+' || value[0] == '-'))
        {
            int separator = value.Length == 6 ? 3 : -1;
            if ((separator < 0 || value[separator] == ':')
                && int.TryParse(value.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int hours)
                && int.TryParse(value.AsSpan(separator < 0 ? 3 : 4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
                && hours <= 14
                && minutes < 60
                && (hours < 14 || minutes == 0))
            {
                offset = new TimeSpan(hours, minutes, 0);
                if (value[0] == '-')
                    offset = -offset;
                return true;
            }
        }

        offset = value.ToUpperInvariant() switch
        {
            "UT" or "UTC" or "GMT" or "Z" => TimeSpan.Zero,
            "EST" => TimeSpan.FromHours(-5),
            "EDT" => TimeSpan.FromHours(-4),
            "CST" => TimeSpan.FromHours(-6),
            "CDT" => TimeSpan.FromHours(-5),
            "MST" => TimeSpan.FromHours(-7),
            "MDT" => TimeSpan.FromHours(-6),
            "PST" => TimeSpan.FromHours(-8),
            "PDT" => TimeSpan.FromHours(-7),
            _ => TimeSpan.MinValue,
        };
        return offset != TimeSpan.MinValue;
    }
}
