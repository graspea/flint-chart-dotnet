namespace Graspea.Flint.Delivery;

/// <summary>The name alphabet shared by chart ids and group names.</summary>
internal static class DeliveryNames
{
    /// <summary>True when <paramref name="s"/> is non-empty and made of A-Z, a-z, 0-9, '_', '.' and '-'.</summary>
    internal static bool IsValid(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        foreach (var c in s)
        {
            var ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '.' or '-';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Throws when <paramref name="value"/> is null or outside the alphabet; <paramref name="what"/> names it in the message.</summary>
    internal static void Check(string? value, string paramName, string what)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        if (!IsValid(value))
        {
            throw new ArgumentException($"A {what} must match [A-Za-z0-9_.-]+.", paramName);
        }
    }
}
