using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Graspea.Flint.Validation.Rules;

/// <summary>
/// <c>parseTemporalByType</c> of flint-chart 1.1.1 (src/core/temporal-parse.ts:113-131): the spellings a <c>YearMonth</c>,
/// <c>Date</c> or <c>DateTime</c> semantic type reads into <c>YYYY-MM</c> or <c>YYYY-MM-DD</c>; <c>readIsoTimestamp</c>
/// (:87-106, since flint-chart 1.1.0), the ISO timestamps <c>convertTemporalData</c> reads first; and the table that
/// <c>convertTemporalData</c> writes (<see cref="Converted"/>). Regular expressions and arithmetic only, so it needs no
/// JavaScript engine. The patterns are JavaScript's: <c>\d</c> is ASCII, <c>\s</c> is ECMA-262 WhiteSpace and
/// LineTerminator, the <c>i</c> flag folds ASCII letters only, and <c>$</c> is the end of the text.
/// </summary>
internal static partial class TemporalParse
{
    private const string S = @"[\t\n\v\f\r    -     　﻿]";
    private const string Month = "(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\\.?";
    private const string Sep = "(?:" + S + @"|[\-/._,])*";
    private const string Year = "([0-9]{4})";
    private const string ShortYear = "'?([0-9]{2})";
    private const string Ordinal = "(?:st|nd|rd|th)?";

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static Regex Anchored(string source) => new($"^{source}\\z", RegexOptions.CultureInvariant);

    private static readonly (Regex Pattern, Func<Match, string?> Read)[] YearMonthParsers =
    [
        (Anchored("([0-9]{4})([0-9]{2})"), m => YearMonth(N(m, 1), N(m, 2))),
        (Anchored($"{Year}[\\-/. ]([0-9]{{1,2}})"), m => YearMonth(N(m, 1), N(m, 2))),
        (Anchored($"{Year}{S}*m([0-9]{{1,2}})"), m => YearMonth(N(m, 1), N(m, 2))),
        (Anchored($"([0-9]{{1,2}})[\\-/. ]{Year}"), m => YearMonth(N(m, 2), N(m, 1))),
        (Anchored($"{Year}{Sep}{Month}"), m => YearMonth(N(m, 1), MonthOf(m, 2))),
        (Anchored($"{Month}{Sep}{Year}"), m => YearMonth(N(m, 2), MonthOf(m, 1))),
        (Anchored($"{Month}(?:{S}|[\\-/.])*{ShortYear}"), m => YearMonth(FullYear(N(m, 2)), MonthOf(m, 1))),
    ];

    private static readonly (Regex Pattern, Func<Match, string?> Read)[] DateParsers =
    [
        (Anchored("([0-9]{4})([0-9]{2})([0-9]{2})"), m => Date(N(m, 1), N(m, 2), N(m, 3))),
        (Anchored($"{Year}[/.]([0-9]{{1,2}})[/.]([0-9]{{1,2}})"), m => Date(N(m, 1), N(m, 2), N(m, 3))),
        (Anchored($"{Year}{Sep}{Month}{Sep}([0-9]{{1,2}}){Ordinal}"), m => Date(N(m, 1), MonthOf(m, 2), N(m, 3))),
        (Anchored($"{Month}{Sep}([0-9]{{1,2}}){Ordinal}{Sep}{Year}"), m => Date(N(m, 3), MonthOf(m, 1), N(m, 2))),
        (Anchored($"([0-9]{{1,2}}){Ordinal}{Sep}{Month}{Sep}{Year}"), m => Date(N(m, 3), MonthOf(m, 2), N(m, 1))),
    ];

    /// <summary><c>hasTemporalParsers</c> for a registered type (a name of Object.prototype is <c>reserved_semantic_type</c>).</summary>
    internal static bool HasParsers(string semanticType) => semanticType is "YearMonth" or "Date" or "DateTime";

    /// <summary>The canonical string of a value of <paramref name="semanticType"/>, or null when it is not one of its spellings.</summary>
    internal static string? Parse(JsonNode? value, string semanticType)
    {
        if (!HasParsers(semanticType))
        {
            return null;
        }

        string text;
        switch (Js.Kind(value))
        {
            case JsonValueKind.Number:
                var n = Js.NumberOf(value!);
                if (n % 1 != 0 || double.IsInfinity(n))
                {
                    return null;
                }

                text = Js.ToJsString(value);
                break;
            case JsonValueKind.String:
                text = value!.GetValue<string>();
                break;
            default:
                return null;
        }

        text = Js.Trim(text);
        return text.Length == 0 ? null : Read(text, semanticType == "YearMonth" ? YearMonthParsers : DateParsers);
    }

    /// <summary><c>parseTemporalByType(text, 'Date') !== null</c>.</summary>
    internal static bool IsDate(string text) => Js.Trim(text) is { Length: > 0 } t && Read(t, DateParsers) is not null;

    /// <summary>The upper bound of a likely timestamp in seconds (<c>MAX_TIMESTAMP_SEC</c>, src/core/resolve-semantics.ts:232 of
    /// flint-chart 1.1.1).</summary>
    private const double MaxTimestampSeconds = 4102444800;

    /// <summary>
    /// Whether <paramref name="semanticType"/> is a calendar grain: <c>SEMANTIC_LEVEL[st] &gt;= SEMANTIC_LEVEL.Date</c>
    /// (src/core/resolve-semantics.ts:360-366, :466 of flint-chart 1.1.1). A name of <c>Object.prototype</c> compares false
    /// there, so the list is explicit. <c>Month</c>, <c>Quarter</c> and <c>Day</c> are ordinal in the catalog: their key is
    /// temporal only when its values read as dates, so a caller tests the temporal key first.
    /// </summary>
    internal static bool IsCalendarGrain(string semanticType) =>
        semanticType is "Year" or "Decade" or "YearMonth" or "Month" or "YearQuarter" or "Quarter" or "Date" or "Day";

    /// <summary>
    /// <c>readIsoTimestamp</c> (src/core/temporal-parse.ts:87-106 of flint-chart 1.1.1, since 1.1.0): for a string that is
    /// an ISO timestamp once trimmed, its date when <paramref name="calendarGrain"/> holds and its time is midnight (no
    /// digit past zero in the fraction) whatever its zone; else null when it has a zone (it is kept as written); else the
    /// zoneless timestamp in ISO form, <c>T</c> for a space or a <c>t</c> and seconds added (<c>"2024-01-15 10:00"</c> is
    /// <c>"2024-01-15T10:00:00"</c>). Null for any other value. No range check: <c>"2024-02-30T00:00:00"</c> of a
    /// <c>Date</c> is <c>"2024-02-30"</c>.
    /// </summary>
    internal static string? ReadIsoTimestamp(JsonNode? value, bool calendarGrain)
    {
        if (Js.Kind(value) != JsonValueKind.String || IsoTimestamp().Match(Js.Trim(value!.GetValue<string>())) is not { Success: true } m)
        {
            return null;
        }

        var (day, hours, minutes) = (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
        var seconds = m.Groups[4].Success ? m.Groups[4].Value : "00";
        var fraction = m.Groups[5].Value;
        var midnight = hours == "00" && minutes == "00" && seconds == "00" && !fraction.Any(c => c is >= '1' and <= '9');
        if (calendarGrain && midnight)
        {
            return day;
        }

        return m.Groups[6].Success ? null : $"{day}T{hours}:{minutes}:{seconds}{fraction}";
    }

    /// <summary><c>ISO_TIMESTAMP</c> (src/core/temporal-parse.ts:87 of flint-chart 1.1.1): its <c>i</c> flag reaches only
    /// <c>T</c> and <c>Z</c>, and its <c>$</c> is the end of the text.</summary>
    [GeneratedRegex(@"^([0-9]{4}-[0-9]{2}-[0-9]{2})[Tt ]([0-9]{2}):([0-9]{2})(?::([0-9]{2})(\.[0-9]+)?)?([Zz]|[+-][0-9]{2}(?::?[0-9]{2})?)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex IsoTimestamp();

    /// <summary>
    /// A value of a temporal key as <c>convertTemporalData</c> writes it into the table (src/core/resolve-semantics.ts:462-497
    /// of flint-chart 1.1.1): an ISO timestamp as <see cref="ReadIsoTimestamp"/> reads it, a midnight being its date under a
    /// calendar grain (<see cref="IsCalendarGrain"/>; since flint-chart 1.1.0); else the canonical string of a spelling of
    /// <paramref name="semanticType"/> (<see cref="Parse"/>), or without a semantic type of a full date that does not open as an
    /// ISO date; else a number of a <c>Year</c> or <c>Decade</c> as its floor, a likely timestamp (<c>isLikelyTimestamp</c>,
    /// :235-243: seconds or milliseconds up to 2100) as <c>toISOString()</c>, any other number as <c>String(n)</c>; a
    /// <c>Year</c> or <c>Decade</c> string of two digits as its four-digit year (<c>expandToFullYear</c>, :424-431); anything
    /// else as <c>String(v)</c>, so <c>null</c> is <c>"null"</c> and a missing key <c>"undefined"</c>. JSON holds no
    /// <c>Date</c> object, so the branch for one (:482-487) never applies.
    /// </summary>
    internal static string Converted(JsonNode? value, bool present, string semanticType)
    {
        var parsed = ReadIsoTimestamp(value, IsCalendarGrain(semanticType))
            ?? Parse(value, semanticType)
            ?? (semanticType.Length == 0 && Js.Kind(value) == JsonValueKind.String && !IsoPrefix().IsMatch(Js.Trim(value!.GetValue<string>())) ? Parse(value, "Date") : null);
        if (parsed is not null)
        {
            return parsed;
        }

        var year = semanticType is "Year" or "Decade";
        switch (Js.Kind(value))
        {
            case JsonValueKind.Number:
                var n = Js.NumberOf(value!);
                if (year)
                {
                    return Js.NumberToString(Math.Floor(n));
                }

                if (n is >= 1e9 and <= MaxTimestampSeconds * 1000)
                {
                    var ms = Math.Truncate(n <= MaxTimestampSeconds ? n * 1000 : n);
                    return DateTimeOffset.FromUnixTimeMilliseconds((long)ms).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                }

                return Js.ToJsString(value);
            case JsonValueKind.String when year:
                var text = value!.GetValue<string>();
                return Js.Trim(text) is { Length: 2 } t && t.All(char.IsAsciiDigit) && int.Parse(t, CultureInfo.InvariantCulture) is var y
                    ? (y <= 49 ? 2000 + y : 1900 + y).ToString(CultureInfo.InvariantCulture)
                    : text;
            default:
                return Js.ToJsString(value, present);
        }
    }

    /// <summary><c>ISO_PREFIX</c> (src/core/resolve-semantics.ts:263 of flint-chart 1.1.1).</summary>
    [GeneratedRegex("^[0-9]{4}-[0-9]{2}", RegexOptions.CultureInvariant)]
    private static partial Regex IsoPrefix();

    private static string? Read(string text, (Regex Pattern, Func<Match, string?> Read)[] parsers)
    {
        // The i flag without u folds ASCII letters only.
        var folded = string.Create(text.Length, text, (span, t) =>
        {
            for (var i = 0; i < t.Length; i++)
            {
                span[i] = t[i] is >= 'A' and <= 'Z' ? (char)(t[i] + 32) : t[i];
            }
        });
        foreach (var (pattern, read) in parsers)
        {
            if (pattern.Match(folded) is { Success: true } m && read(m) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    private static int N(Match m, int group) => int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    private static int MonthOf(Match m, int group) => Array.IndexOf(Months, m.Groups[group].Value[..3]) + 1;

    private static int FullYear(int twoDigits) => twoDigits <= 49 ? 2000 + twoDigits : 1900 + twoDigits;

    private static string? YearMonth(int year, int month) =>
        year is < 1000 or > 2999 || month is < 1 or > 12 ? null : $"{year:D4}-{month:D2}";

    private static string? Date(int year, int month, int day) =>
        YearMonth(year, month) is { } ym && day >= 1 && day <= DateTime.DaysInMonth(year, month) ? $"{ym}-{day:D2}" : null;
}

/// <summary>
/// <c>readsAsSnapshots</c> (src/core/decisions.ts:216-223 of flint-chart 1.1.1): whether dates on a banded axis read as a few
/// snapshots: two to eight distinct dates, or nine to fifty that fill under half the periods between the first and the last
/// at their cadence (<see cref="Cadence"/>, since flint-chart 1.1.0). A value's time (<c>snapshotTime</c>, :79-90) is decided
/// for numbers (an integer from 1000 to 2999 is a year), four digits, ISO dates and ISO date-times with <c>Z</c> or an
/// offset; a date-time without one is local time and other strings need V8's legacy parser, so their time is undecided.
/// The answer is null when the undecided values could change it.
/// </summary>
internal static class Snapshots
{
    private const int MaxDates = 8;
    private const double MaxCoverage = 0.5;
    private const int MaxBands = 50;
    private const double DayMs = 86_400_000;

    /// <summary><c>MONTH_DAYS</c>, <c>MONTH_SLACK_DAYS</c> and <c>CADENCE_SLACK</c> (src/core/decisions.ts:122-125 of
    /// flint-chart 1.1.1).</summary>
    private const double MonthDays = 365.2425 / 12;
    private const double MonthSlackDays = 4;
    private const double CadenceSlack = 0.25;

    internal static bool? ReadAs(IEnumerable<JsonNode?> values)
    {
        var decided = new HashSet<double>();
        var undecided = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            switch (Time(value, out var text))
            {
                case { } time:
                    decided.Add(time);
                    break;
                case null when text is not null:
                    undecided.Add(text);
                    break;
            }
        }

        if (undecided.Count > 0)
        {
            // The distinct times number between the decided ones and all of them.
            return decided.Count + undecided.Count < 2 ? false
                : decided.Count >= 2 && decided.Count + undecided.Count <= MaxDates ? true
                : decided.Count > MaxBands ? false
                : null;
        }

        var times = decided.Order().ToArray();
        if (times.Length < 2)
        {
            return false;
        }

        if (times.Length <= MaxDates)
        {
            return true;
        }

        if (times.Length > MaxBands)
        {
            return false;
        }

        if (times.Any(t => t < MinTime || t > MaxTime))
        {
            // The month of a time .NET cannot represent is unknown here (no calendar date of a chart is one).
            return null;
        }

        return Cadence(times) is { } cadence && cadence.Count / cadence.Periods < MaxCoverage;
    }

    /// <summary>
    /// <c>dateCadence</c> (src/core/decisions.ts:143-164 of flint-chart 1.1.1, since 1.1.0), as <c>calendarCoverage</c>
    /// (:198-205) reads it: the dates counted and the periods spanned at their step, or null where they keep none. Whole
    /// months when every gap is a whole number of months within four days whatever the day or time (quarter ends, the 15th);
    /// else the least gap when it divides every gap (whole days, weeks, hours); else the usual gap (the median) when every gap
    /// is within a quarter of a whole number of it (the 1st and the 15th; 13 or 14 days); else, for dates all at midnight UTC,
    /// their greatest common step; times of day that keep no step have none. <paramref name="times"/> are distinct, ascending
    /// and within the range of <see cref="DateTimeOffset"/>.
    /// </summary>
    private static (double Count, double Periods)? Cadence(double[] times)
    {
        var count = times.Length;
        var span = times[^1] - times[0];
        var gaps = times.Skip(1).Select((t, i) => t - times[i]).ToArray();
        var month = times.Select(MonthIndex).ToArray();
        var monthGaps = month.Skip(1).Select((m, i) => m - month[i]).ToArray();
        if (Enumerable.Range(0, gaps.Length).All(i => monthGaps[i] > 0 && Math.Abs((gaps[i] / DayMs) - (monthGaps[i] * MonthDays)) <= MonthSlackDays))
        {
            var step = monthGaps.Aggregate(0d, Gcd);
            return (count, ((month[^1] - month[0]) / step) + 1);
        }

        var least = gaps.Min();
        var common = gaps.Aggregate(0d, Gcd);
        if (!(least > 0) || !(common > 0))
        {
            return null;
        }

        if (common == least)
        {
            return (count, Js.Round(span / least) + 1);
        }

        var usual = gaps.Order().ElementAt(gaps.Length / 2);
        if (gaps.All(g => Math.Abs(g - (Math.Max(1, Js.Round(g / usual)) * usual)) <= CadenceSlack * usual))
        {
            return (count, Js.Round(span / usual) + 1);
        }

        return times.All(t => t % DayMs == 0) ? (count, (span / common) + 1) : null;
    }

    /// <summary><c>getUTCFullYear() * 12 + getUTCMonth()</c> of <c>new Date(time)</c>, which truncates to whole
    /// milliseconds (TimeClip).</summary>
    private static double MonthIndex(double time)
    {
        var date = DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Truncate(time));
        return (date.Year * 12.0) + date.Month - 1;
    }

    private static readonly double MinTime = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
    private static readonly double MaxTime = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    /// <summary>The <c>gcd</c> of src/core/decisions.ts:92 of flint-chart 1.1.1, over doubles with JavaScript's <c>%</c> (the same
    /// as .NET's).</summary>
    private static double Gcd(double a, double b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    /// <summary><c>snapshotTime</c>: the time, or null with <paramref name="text"/> set when only V8 could tell it, or null
    /// with <paramref name="text"/> null when the value has none.</summary>
    private static double? Time(JsonNode? value, out string? text)
    {
        text = null;
        switch (Js.Kind(value))
        {
            case JsonValueKind.Number:
                var n = Js.NumberOf(value!);
                return n % 1 == 0 && n is >= 1000 and <= 2999 ? YearStart(n) : double.IsFinite(n) ? n : null;
            case JsonValueKind.String:
                var t = Js.Trim(value!.GetValue<string>());
                if (t.Length == 4 && t.All(char.IsAsciiDigit))
                {
                    return YearStart(int.Parse(t, CultureInfo.InvariantCulture));
                }

                switch (Js.DateParses(t))
                {
                    case false:
                        return null;
                    case true when Js.IsoDateOnlyOrWithOffset(t):
                        return Js.IsoTime(t);
                    default:
                        text = t;
                        return null;
                }

            default:
                return null;
        }
    }

    /// <summary><c>Date.UTC(year, 0, 1)</c>; a year of 0 to 99 is 1900 plus it.</summary>
    private static double YearStart(double year) =>
        new DateTimeOffset(year is >= 0 and <= 99 ? 1900 + (int)year : (int)year, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
}
