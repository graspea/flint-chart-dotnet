using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Graspea.Flint.Validation.Rules;

/// <summary>
/// The JavaScript semantics the upstream rules apply to row values: <c>Number(v)</c>, <c>String(v)</c>, set membership
/// (SameValueZero) and what V8's <c>Date.parse</c> accepts, as far as it can be decided without a JavaScript engine.
/// A row value is a <see cref="JsonNode"/>; a missing key is <c>undefined</c>.
/// </summary>
internal static partial class Js
{
    /// <summary>The own property names of <c>Object.prototype</c>. Upstream looks some ids up in plain objects
    /// (<c>THEME_PRESETS[id]</c>, <c>semanticType in TYPE_REGISTRY</c>), where these names are always found.</summary>
    internal static readonly FrozenSet<string> ObjectPrototypeKeys = new[]
    {
        "constructor", "__proto__", "__defineGetter__", "__defineSetter__", "__lookupGetter__", "__lookupSetter__",
        "hasOwnProperty", "isPrototypeOf", "propertyIsEnumerable", "toLocaleString", "toString", "valueOf",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The value of <c>row[field]</c>: <paramref name="present"/> is false for <c>undefined</c>.</summary>
    internal static JsonNode? Get(JsonNode? row, string field, out bool present)
    {
        if (row is JsonObject o && o.TryGetPropertyValue(field, out var value))
        {
            present = true;
            return value;
        }

        present = false;
        return null;
    }

    internal static JsonValueKind Kind(JsonNode? node) => node?.GetValueKind() ?? JsonValueKind.Null;

    /// <summary><c>v != null</c>: neither <c>undefined</c> nor <c>null</c>.</summary>
    internal static bool IsNullish(JsonNode? node, bool present) => !present || node is null;

    /// <summary><c>Number(v)</c> (also <c>+v</c>); <c>undefined</c> is NaN, <c>null</c> is 0.</summary>
    internal static double ToNumber(JsonNode? node, bool present = true)
    {
        if (!present)
        {
            return double.NaN;
        }

        switch (Kind(node))
        {
            case JsonValueKind.Null:
                return 0;
            case JsonValueKind.Number:
                return NumberOf(node!);
            case JsonValueKind.String:
                return StringToNumber(node!.GetValue<string>());
            case JsonValueKind.True:
                return 1;
            case JsonValueKind.False:
                return 0;
            case JsonValueKind.Array:
                // +[] is 0, +[x] is +String(x), anything longer is NaN.
                var array = node!.AsArray();
                return array.Count switch
                {
                    0 => 0,
                    1 => StringToNumber(ToJsString(array[0])),
                    _ => double.NaN,
                };
            default:
                return double.NaN;
        }
    }

    /// <summary>The value of a JSON number, whether parsed (a <see cref="JsonElement"/>) or created from a .NET number.</summary>
    internal static double NumberOf(JsonNode node) =>
        node is JsonValue v && v.TryGetValue<double>(out var d) ? d : double.Parse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>The StringToNumber of ECMA-262 §7.1.4.1.1: trimmed, empty is 0, decimal, Infinity, 0x/0o/0b.</summary>
    internal static double StringToNumber(string s)
    {
        var t = Trim(s);
        if (t.Length == 0)
        {
            return 0;
        }

        if (t is "Infinity" or "+Infinity")
        {
            return double.PositiveInfinity;
        }

        if (t == "-Infinity")
        {
            return double.NegativeInfinity;
        }

        if (t.Length > 2 && t[0] == '0' && t[1] is 'x' or 'X' or 'o' or 'O' or 'b' or 'B')
        {
            var radix = t[1] switch
            {
                'x' or 'X' => 16,
                'o' or 'O' => 8,
                _ => 2,
            };
            double value = 0;
            foreach (var c in t.AsSpan(2))
            {
                var digit = c switch
                {
                    >= '0' and <= '9' => c - '0',
                    >= 'a' and <= 'f' => c - 'a' + 10,
                    >= 'A' and <= 'F' => c - 'A' + 10,
                    _ => 99,
                };
                if (digit >= radix)
                {
                    return double.NaN;
                }

                value = (value * radix) + digit;
            }

            return value;
        }

        return DecimalLiteral().IsMatch(t) ? double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture) : double.NaN;
    }

    /// <summary><c>Number.isFinite(Number(v))</c>.</summary>
    internal static bool IsFiniteNumber(JsonNode? node, bool present = true) => double.IsFinite(ToNumber(node, present));

    /// <summary><c>String(v)</c>; <c>undefined</c> is <c>"undefined"</c>.</summary>
    internal static string ToJsString(JsonNode? node, bool present = true)
    {
        if (!present)
        {
            return "undefined";
        }

        return Kind(node) switch
        {
            JsonValueKind.Null => "null",
            JsonValueKind.String => node!.GetValue<string>(),
            JsonValueKind.Number => NumberToString(NumberOf(node!)),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Array => string.Join(",", node!.AsArray().Select(e => e is null ? string.Empty : ToJsString(e))),
            _ => "[object Object]",
        };
    }

    /// <summary>Number::toString of ECMA-262 §6.1.6.1.20 (radix 10).</summary>
    internal static string NumberToString(double d)
    {
        if (double.IsNaN(d))
        {
            return "NaN";
        }

        if (d == 0)
        {
            return "0";
        }

        if (double.IsInfinity(d))
        {
            return d > 0 ? "Infinity" : "-Infinity";
        }

        var sign = d < 0 ? "-" : string.Empty;
        var shortest = Math.Abs(d).ToString("R", CultureInfo.InvariantCulture);
        var mantissa = shortest;
        var exponent = 0;
        var e = shortest.IndexOfAny(['E', 'e']);
        if (e >= 0)
        {
            mantissa = shortest[..e];
            exponent = int.Parse(shortest[(e + 1)..], CultureInfo.InvariantCulture);
        }

        var dot = mantissa.IndexOf('.');
        var digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
        var pointPosition = (dot < 0 ? mantissa.Length : dot) + exponent;
        var lead = digits.TakeWhile(c => c == '0').Count();
        digits = digits[lead..].TrimEnd('0');
        pointPosition -= lead;
        var k = digits.Length;
        var n = pointPosition;

        string body;
        if (k <= n && n <= 21)
        {
            body = digits + new string('0', n - k);
        }
        else if (0 < n && n <= 21)
        {
            body = digits[..n] + "." + digits[n..];
        }
        else if (-6 < n && n <= 0)
        {
            body = "0." + new string('0', -n) + digits;
        }
        else
        {
            var exp = n - 1;
            var expText = (exp >= 0 ? "+" : "-") + Math.Abs(exp).ToString(CultureInfo.InvariantCulture);
            body = (k == 1 ? digits : digits[..1] + "." + digits[1..]) + "e" + expText;
        }

        return sign + body;
    }

    /// <summary>A key with SameValueZero equality, for <c>new Set(values)</c>: objects and arrays are distinct by identity.</summary>
    internal static object SetKey(JsonNode? node, bool present)
    {
        if (!present)
        {
            return Undefined;
        }

        return Kind(node) switch
        {
            JsonValueKind.Null => Null,
            JsonValueKind.Number => NumberOf(node!) is var d && d == 0 ? 0d : d,
            JsonValueKind.String => node!.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => node!,
        };
    }

    private static readonly object Undefined = new();
    private static readonly object Null = new();

    /// <summary>
    /// Whether V8's <c>Date.parse</c> returns a number: true for the ISO 8601 forms of ECMA-262 §21.4.1.32 with a valid
    /// month, a day of at most 31 and a valid time of day, false for a string without an ASCII digit (V8 needs a number for a date), null
    /// otherwise (V8's legacy parser accepts more, such as <c>"Stage 1"</c>; recorded in generator/verify/probes.json).
    /// </summary>
    internal static bool? DateParses(string s)
    {
        var t = Trim(s);
        if (IsoDate().Match(t) is { Success: true } m)
        {
            int Part(string name, int fallback) => m.Groups[name].Success ? int.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture) : fallback;
            var valid = Part("month", 1) is >= 1 and <= 12 && Part("day", 1) is >= 1 and <= 31
                && Part("hour", 0) <= 23 && Part("minute", 0) <= 59 && Part("second", 0) <= 59;
            return valid ? true : null;
        }

        return t.Any(char.IsAsciiDigit) ? null : false;
    }

    /// <summary>The time value of an ISO 8601 string accepted by <see cref="DateParses"/>, read as UTC when it has no
    /// offset (V8 reads a date-time without one as local time; the order of two such values is the same).</summary>
    internal static double? IsoTime(string s)
    {
        var t = Trim(s);
        if (IsoDate().Match(t) is not { Success: true } m || DateParses(t) != true)
        {
            return null;
        }

        var g = (string name, int fallback) => m.Groups[name].Success ? int.Parse(m.Groups[name].Value, CultureInfo.InvariantCulture) : fallback;
        var year = g("year", 1970);
        var month = g("month", 1);
        var day = g("day", 1);
        if (day > DateTime.DaysInMonth(year, month))
        {
            // V8 rolls an out-of-range day into the next month ("2024-02-30" is 1 March).
            var rolled = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day - 1);
            (year, month, day) = (rolled.Year, rolled.Month, rolled.Day);
        }

        var fraction = m.Groups["fraction"].Success ? double.Parse("0." + m.Groups["fraction"].Value, CultureInfo.InvariantCulture) * 1000 : 0;
        var time = new DateTimeOffset(year, month, day, g("hour", 0), g("minute", 0), g("second", 0), TimeSpan.Zero).ToUnixTimeMilliseconds() + fraction;
        if (m.Groups["offset"].Success && m.Groups["offset"].Value is not ("Z" or "z"))
        {
            var o = m.Groups["offset"].Value;
            var minutes = (int.Parse(o.AsSpan(1, 2), CultureInfo.InvariantCulture) * 60) + int.Parse(o.AsSpan(4, 2), CultureInfo.InvariantCulture);
            time -= (o[0] == '-' ? -minutes : minutes) * 60_000;
        }

        return time;
    }

    /// <summary><c>String.prototype.trim</c>: WhiteSpace and LineTerminator of ECMA-262 §12.2, §12.3.</summary>
    internal static string Trim(string s) => s.Trim(WhiteSpace);

    private static readonly char[] WhiteSpace =
    [
        '\t', '\v', '\f', ' ', '\u00A0', '\uFEFF', '\n', '\r', '\u2028', '\u2029',
        '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006', '\u2007', '\u2008', '\u2009', '\u200A', '\u202F', '\u205F', '\u3000',
    ];

    [GeneratedRegex(@"^[+-]?([0-9]+\.?[0-9]*([eE][+-]?[0-9]+)?|\.[0-9]+([eE][+-]?[0-9]+)?)$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalLiteral();

    [GeneratedRegex(@"^(?<year>[0-9]{4})(-(?<month>[0-9]{2})(-(?<day>[0-9]{2})(T(?<hour>[0-9]{2}):(?<minute>[0-9]{2})(:(?<second>[0-9]{2})(\.(?<fraction>[0-9]{1,3}))?)?(?<offset>Z|[+-][0-9]{2}:[0-9]{2})?)?)?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();
}
