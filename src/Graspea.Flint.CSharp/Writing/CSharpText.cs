using System.Globalization;
using System.Text;

namespace Graspea.Flint.CSharp;

/// <summary>C# identifiers, names and literals (design §12.5.3).</summary>
internal static class CSharpText
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>
    /// The names the class of the tool's output must not have (design §12.5.1): every type and namespace the output writes
    /// by its simple name, which the class's own name would hide inside it (a class <c>Chart</c> turns
    /// <c>Chart.Bar(Rows)</c> into a call on itself). The catalog's enums and option classes come from the name tables.
    /// </summary>
    public static readonly HashSet<string> ReservedClassNames = Reserved();

    /// <summary>
    /// Rule 1 of design §7.6, as the generator's <c>pascal</c>: split on runs of characters outside <c>[A-Za-z0-9]</c> and
    /// on case changes, capitalise each token (an all-upper token of two or more letters keeps only its first letter
    /// upper), and put <c>_</c> before a leading digit. Empty when the text has no ASCII letter or digit.
    /// </summary>
    public static string Pascal(string text)
    {
        var result = new StringBuilder();
        foreach (var part in AsciiParts(text))
        {
            var start = 0;
            for (var i = 1; i <= part.Length; i++)
            {
                if (i == part.Length || SplitsBefore(part, i))
                {
                    var token = part.Substring(start, i - start);
                    result.Append(char.ToUpperInvariant(token[0]));
                    var rest = token.Substring(1);
                    result.Append(token.Length >= 2 && IsAllUpper(token) ? rest.ToLowerInvariant() : rest);
                    start = i;
                }
            }
        }

        return result.Length > 0 && IsAsciiDigit(result[0]) ? "_" + result : result.ToString();
    }

    /// <summary>The default class name for an input file (design §12.5.1), with <c>Flint</c> before a reserved name
    /// (<c>chart.json</c> gives <c>FlintChart</c>); <c>FlintChart</c> for standard input.</summary>
    public static string ClassNameFor(string? file)
    {
        if (file is null || file == "-")
        {
            return "FlintChart";
        }

        var name = file.Substring(Math.Max(file.LastIndexOf('/'), file.LastIndexOf('\\')) + 1);
        var stem = name.EndsWith(".flint.json", StringComparison.Ordinal) ? name.Substring(0, name.Length - ".flint.json".Length)
            : name.EndsWith(".json", StringComparison.Ordinal) ? name.Substring(0, name.Length - ".json".Length)
            : name;
        var pascal = Pascal(stem);
        var className = pascal.Length == 0 ? "FlintChart" : pascal.EndsWith("Chart", StringComparison.Ordinal) ? pascal : pascal + "Chart";
        return ReservedClassNames.Contains(className) ? "Flint" + className : className;
    }

    /// <summary>Whether <paramref name="name"/> is a C# identifier that is not a keyword.</summary>
    public static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || Keywords.Contains(name))
        {
            return false;
        }

        for (var i = 0; i < name.Length; i += char.IsSurrogatePair(name, i) ? 2 : 1)
        {
            var category = char.GetUnicodeCategory(name, i);
            var letter = name[i] == '_' || category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber;
            var part = category is UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format;
            if (!(letter || (i > 0 && part)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A member name as written in C#: a keyword gets <c>@</c> (<c>@class</c>).</summary>
    public static string Escape(string name) => Keywords.Contains(name) ? "@" + name : name;

    /// <summary>Whether <paramref name="name"/> is a dotted sequence of identifiers.</summary>
    public static bool IsNamespace(string name) => name.Split('.').All(IsIdentifier);

    /// <summary>
    /// A regular C# string literal. Quotes, backslashes and the named escapes are escaped, and so is every character that
    /// could make the source read differently from what compiles: other controls, the Unicode categories Cf, Zl, Zp, Co
    /// and Cn, and U+FFFD (design §12.5.3).
    /// </summary>
    public static string StringLiteral(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"': builder.Append("\\\""); continue;
                case '\\': builder.Append("\\\\"); continue;
                case '\0': builder.Append("\\0"); continue;
                case '\a': builder.Append("\\a"); continue;
                case '\b': builder.Append("\\b"); continue;
                case '\f': builder.Append("\\f"); continue;
                case '\n': builder.Append("\\n"); continue;
                case '\r': builder.Append("\\r"); continue;
                case '\t': builder.Append("\\t"); continue;
                case '\v': builder.Append("\\v"); continue;
            }

            var pair = char.IsSurrogatePair(value, i);
            if (MustEscape(value, i))
            {
                builder.Append(pair ? $"\\U{char.ConvertToUtf32(value, i):X8}" : $"\\u{(int)c:X4}");
            }
            else
            {
                builder.Append(c);
                if (pair)
                {
                    builder.Append(value[i + 1]);
                }
            }

            if (pair)
            {
                i++;
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>Whether the character (or surrogate pair) at <paramref name="index"/> is written as an escape.</summary>
    public static bool MustEscape(string value, int index)
    {
        var c = value[index];
        if (c == '\uFFFD' || (char.IsSurrogate(c) && !char.IsSurrogatePair(value, index)))
        {
            return true;
        }

        return char.GetUnicodeCategory(value, index) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate;
    }

    /// <summary>A single-line raw string literal with one more quote than the longest quote run in the content, at least
    /// three. The content must not start or end with a quote and must not contain a line break.</summary>
    public static string RawStringLiteral(string content)
    {
        var longest = 0;
        var run = 0;
        foreach (var c in content)
        {
            run = c == '"' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var quotes = new string('"', Math.Max(3, longest + 1));
        return quotes + content + quotes;
    }

    /// <summary>The raw text of a lexically integral JSON number as a <c>long</c> literal: <c>L</c> outside the int range.</summary>
    public static string LongLiteral(string raw) =>
        int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? raw : raw + "L";

    /// <summary>The raw text of a JSON number as a <c>double</c> literal: <c>d</c> when it has no point and no exponent.</summary>
    public static string DoubleLiteral(string raw) => IsIntegralText(raw) ? raw + "d" : raw;

    /// <summary>The raw text of a JSON number as a <c>decimal</c> literal.</summary>
    public static string DecimalLiteral(string raw) => raw + "m";

    /// <summary>
    /// The raw text of a JSON number where any numeric type converts (a <c>double</c> or <c>JsonNode</c> parameter): as
    /// written when it is an <c>int</c> literal or has a point or an exponent, <c>L</c> within the <c>long</c> range,
    /// otherwise <c>d</c>.
    /// </summary>
    public static string NumberLiteral(string raw)
    {
        if (!IsIntegralText(raw) || int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            return raw;
        }

        return long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _) ? raw + "L" : raw + "d";
    }

    /// <summary>Whether the text has neither a decimal point nor an exponent (design §12.4.3: integral is lexical).</summary>
    public static bool IsIntegralText(string raw) => raw.IndexOf('.') < 0 && raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0;

    /// <summary>Whether <c>double</c> represents the number without overflowing to infinity.</summary>
    public static bool FitsDouble(string raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !double.IsInfinity(value);

    private static HashSet<string> Reserved()
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            "Graspea", "System",
            "Aggregate", "Channel", "Chart", "ChartAssemblyInput", "ChartData", "ChartEncoding", "ChartSpec", "ChartTemplate", "ChartType",
            "EncodingType", "EncodingValue", "FlintBackend", "FlintJson", "RowOptions", "SemanticAnnotation", "SemanticType",
            "SeriesCountAxis", "SortOrder", "ThemePreset", "ValidationResult",
            "JsonArray", "JsonDocument", "JsonElement", "JsonNode", "JsonPropertyName", "JsonTypeInfo",
            "RequiresDynamicCode", "RequiresUnreferencedCode", "LazyInitializer",
            "CancellationToken", "DateOnly", "DateTime", "DateTimeOffset", "IAsyncEnumerable", "IEnumerable", "Stream", "Task", "ValueTask",
        };
        foreach (var template in NameTables.Templates)
        {
            foreach (var property in template.Properties)
            {
                if (property.EnumType is { } type)
                {
                    names.Add(type);
                }
            }
        }

        foreach (var type in NameTables.Classes)
        {
            names.Add(NameTables.SimpleName(type.Name));
            foreach (var member in type.Members)
            {
                // A member type such as Graspea.Flint.ChartSize or IList<Graspea.Flint.Theming.ThemeInk>?: the simple names.
                foreach (var part in member.Type.Split('<', '>', ',', ' ', '?', '[', ']'))
                {
                    var simple = NameTables.SimpleName(part);
                    if (simple.Length > 0 && IsAsciiUpper(simple[0]))
                    {
                        names.Add(simple);
                    }
                }
            }
        }

        return names;
    }

    private static IEnumerable<string> AsciiParts(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text)
        {
            if (IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    /// <summary>The case-change split points of the generator's rule: <c>(?&lt;=[a-z0-9])(?=[A-Z])</c> and
    /// <c>(?&lt;=[A-Z])(?=[A-Z][a-z])</c>.</summary>
    private static bool SplitsBefore(string part, int i) =>
        (IsAsciiLower(part[i - 1]) || IsAsciiDigit(part[i - 1])) && IsAsciiUpper(part[i])
        || IsAsciiUpper(part[i - 1]) && IsAsciiUpper(part[i]) && i + 1 < part.Length && IsAsciiLower(part[i + 1]);

    private static bool IsAllUpper(string token) => token.All(IsAsciiUpper);

    private static bool IsAsciiLetterOrDigit(char c) => IsAsciiLower(c) || IsAsciiUpper(c) || IsAsciiDigit(c);

    private static bool IsAsciiLower(char c) => c >= 'a' && c <= 'z';

    private static bool IsAsciiUpper(char c) => c >= 'A' && c <= 'Z';

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
}
