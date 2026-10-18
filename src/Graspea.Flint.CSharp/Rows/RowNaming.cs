using System.Globalization;
using System.Text;

namespace Graspea.Flint.CSharp;

/// <summary>How a row type's member names become keys (design §12.6.5).</summary>
internal enum RowNamingPolicy
{
    /// <summary>The member name as written (no naming policy).</summary>
    Verbatim,

    /// <summary><c>JsonNamingPolicy.CamelCase</c>, the default of <c>RowOptions</c>.</summary>
    CamelCase,

    SnakeCaseLower,

    SnakeCaseUpper,

    KebabCaseLower,

    KebabCaseUpper,
}

/// <summary>
/// System.Text.Json's naming policies, which the compiler host may not have: the camelCase rule of
/// <c>JsonCamelCaseNamingPolicy</c> and the separator rule of <c>JsonSeparatorNamingPolicy</c>. Test vectors compare
/// them with <c>JsonNamingPolicy</c> on net8.0 and net10.0.
/// </summary>
internal static class RowNaming
{
    public static string Convert(RowNamingPolicy policy, string name) => policy switch
    {
        RowNamingPolicy.CamelCase => CamelCase(name),
        RowNamingPolicy.SnakeCaseLower => Separated(name, '_', lowercase: true),
        RowNamingPolicy.SnakeCaseUpper => Separated(name, '_', lowercase: false),
        RowNamingPolicy.KebabCaseLower => Separated(name, '-', lowercase: true),
        RowNamingPolicy.KebabCaseUpper => Separated(name, '-', lowercase: false),
        _ => name,
    };

    private static string CamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
        {
            return name;
        }

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
            {
                break;
            }

            var hasNext = i + 1 < chars.Length;
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
            {
                if (chars[i + 1] == ' ')
                {
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }

                break;
            }

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    private enum State
    {
        NotStarted,
        UppercaseLetter,
        LowercaseLetterOrDigit,
        SpaceSeparator,
    }

    private static string Separated(string name, char separator, bool lowercase)
    {
        var builder = new StringBuilder(name.Length + (name.Length / 5));
        var state = State.NotStarted;
        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            var category = char.GetUnicodeCategory(current);
            switch (category)
            {
                case UnicodeCategory.UppercaseLetter:
                    if (state is State.LowercaseLetterOrDigit or State.SpaceSeparator
                        || (state == State.UppercaseLetter && i + 1 < name.Length && char.IsLower(name[i + 1])))
                    {
                        builder.Append(separator);
                    }

                    builder.Append(lowercase ? char.ToLowerInvariant(current) : current);
                    state = State.UppercaseLetter;
                    break;
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.DecimalDigitNumber:
                    if (state == State.SpaceSeparator)
                    {
                        builder.Append(separator);
                    }

                    builder.Append(!lowercase && category == UnicodeCategory.LowercaseLetter ? char.ToUpperInvariant(current) : current);
                    state = State.LowercaseLetterOrDigit;
                    break;
                case UnicodeCategory.SpaceSeparator:
                    if (state != State.NotStarted)
                    {
                        state = State.SpaceSeparator;
                    }

                    break;
                default:
                    builder.Append(current);
                    state = State.NotStarted;
                    break;
            }
        }

        return builder.ToString();
    }
}
