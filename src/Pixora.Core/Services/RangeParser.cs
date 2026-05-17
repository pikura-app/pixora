namespace Pixora.Core.Services;

/// <summary>
/// Parses compact range strings like <c>"1-4, 6-10, 13"</c> into a distinct
/// sorted set of 1-based indexes. Ranges are inclusive on both ends.
/// </summary>
public static class RangeParser
{
    public static IReadOnlyList<int> Parse(string input, int minInclusive = 1, int maxInclusive = int.MaxValue)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        var set = new SortedSet<int>();
        foreach (var rawPart in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Trim();
            if (part.Length == 0) continue;

            var dash = part.IndexOf('-');
            if (dash < 0)
            {
                if (int.TryParse(part, out var single) && single >= minInclusive && single <= maxInclusive)
                    set.Add(single);
                continue;
            }
            var lhs = part[..dash].Trim();
            var rhs = part[(dash + 1)..].Trim();
            if (!int.TryParse(lhs, out var from) || !int.TryParse(rhs, out var to)) continue;
            if (from > to) (from, to) = (to, from);
            for (var i = Math.Max(from, minInclusive); i <= Math.Min(to, maxInclusive); i++)
                set.Add(i);
        }
        return set.ToArray();
    }

    /// <summary>Validates the input and returns the resulting count plus any parse issues.</summary>
    public static (int Count, string? Error) Validate(string input, int minInclusive, int maxInclusive)
    {
        if (string.IsNullOrWhiteSpace(input)) return (0, "Enter a range, e.g. \"1-10\" or \"1,3,5-8\".");
        var indexes = Parse(input, minInclusive, maxInclusive);
        if (indexes.Count == 0) return (0, "Nothing matched the given range.");
        return (indexes.Count, null);
    }
}
