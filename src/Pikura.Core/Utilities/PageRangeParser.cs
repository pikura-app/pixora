using System.Globalization;
using System.Text.RegularExpressions;

namespace Pikura.Core.Utilities;

/// <summary>
/// Represents a parsed page range.
/// </summary>
public sealed class ParsedPageRange
{
    /// <summary>Original input string.</summary>
    public string RawInput { get; set; } = string.Empty;

    /// <summary>When true, represents "all pages".</summary>
    public bool IsAll { get; set; }

    /// <summary>Individual page numbers (1-indexed).</summary>
    public List<int> Pages { get; set; } = new();

    /// <summary>Minimum page number.</summary>
    public int MinPage => Pages.Count > 0 ? Pages.Min() : 0;

    /// <summary>Maximum page number.</summary>
    public int MaxPage => Pages.Count > 0 ? Pages.Max() : 0;

    /// <summary>Whether the range is empty (no valid pages).</summary>
    public bool IsEmpty => !IsAll && Pages.Count == 0;

    /// <summary>
    /// Checks if a specific page number is included in this range.
    /// </summary>
    public bool Contains(int page)
    {
        if (IsAll) return true;
        return Pages.Contains(page);
    }

    /// <summary>
    /// Converts to a display-friendly string (e.g., "1-5" instead of "1,2,3,4,5").
    /// </summary>
    public string ToDisplayString()
    {
        if (IsAll) return "All";
        if (Pages.Count == 0) return "None";
        if (Pages.Count == 1) return Pages[0].ToString();

        var sorted = Pages.OrderBy(p => p).ToList();
        var ranges = new List<string>();
        var start = sorted[0];
        var end = sorted[0];

        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] == end + 1)
            {
                end = sorted[i];
            }
            else
            {
                ranges.Add(start == end ? start.ToString() : $"{start}-{end}");
                start = sorted[i];
                end = sorted[i];
            }
        }
        ranges.Add(start == end ? start.ToString() : $"{start}-{end}");

        return string.Join(",", ranges);
    }

    /// <summary>
    /// Converts page numbers to 0-based indices for array access.
    /// </summary>
    public List<int> ToZeroBasedIndices()
    {
        if (IsAll) return new List<int>(); // Special case: caller should handle
        return Pages.Select(p => p - 1).Where(i => i >= 0).ToList();
    }
}

/// <summary>
/// Parses page range strings like "0", "2", "1-5", "2,4,6-10" into structured data.
/// </summary>
public static class PageRangeParser
{
    // Matches: single number, range (start-end), or comma-separated combinations
    private static readonly Regex PageRangeRegex = new(
        @"^\s*(?:(?:all|0)\s*$|\d+(?:\s*-\s*\d+)?(?:\s*,\s*\d+(?:\s*-\s*\d+)?)*)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberRangeRegex = new(
        @"(\d+)(?:\s*-\s*(\d+))?",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses a page range string.
    /// </summary>
    /// <param name="input">Input string like "0", "2", "1-5", "2,4,6-10"</param>
    /// <param name="maxPages">Maximum valid page number (default: no limit)</param>
    /// <returns>ParsedPageRange with validation results</returns>
    public static ParsedPageRange Parse(string? input, int maxPages = int.MaxValue)
    {
        var result = new ParsedPageRange { RawInput = input ?? string.Empty };

        // Handle null/empty/all/0 as "all pages"
        if (string.IsNullOrWhiteSpace(input) ||
            input.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ||
            input.Trim() == "0")
        {
            result.IsAll = true;
            return result;
        }

        // Validate format
        if (!PageRangeRegex.IsMatch(input))
        {
            // Invalid format - return empty
            return result;
        }

        // Parse individual segments
        var matches = NumberRangeRegex.Matches(input);
        var pages = new HashSet<int>();

        foreach (Match match in matches)
        {
            var startStr = match.Groups[1].Value;
            var endStr = match.Groups[2].Value;

            if (!int.TryParse(startStr, out var start))
                continue;

            if (string.IsNullOrEmpty(endStr))
            {
                // Single number
                if (start > 0 && start <= maxPages)
                    pages.Add(start);
            }
            else
            {
                // Range
                if (int.TryParse(endStr, out var end))
                {
                    // Normalize: ensure start <= end
                    var rangeStart = Math.Min(start, end);
                    var rangeEnd = Math.Max(start, end);

                    // Clamp to valid range
                    rangeStart = Math.Max(1, rangeStart);
                    rangeEnd = Math.Min(maxPages, rangeEnd);

                    for (int i = rangeStart; i <= rangeEnd; i++)
                    {
                        pages.Add(i);
                    }
                }
            }
        }

        result.Pages = pages.OrderBy(p => p).ToList();
        return result;
    }

    /// <summary>
    /// Validates if a string is a valid page range format.
    /// </summary>
    public static bool IsValid(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return true; // Empty = valid (means "all")
        if (input.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return true;
        if (input.Trim() == "0") return true;
        return PageRangeRegex.IsMatch(input);
    }

    /// <summary>
    /// Gets a user-friendly error message for invalid input.
    /// </summary>
    public static string? GetValidationError(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        if (IsValid(input)) return null;

        return "Invalid format. Use: 'all' or '0' (all pages), '2' (page 2), '1-5' (pages 1-5), or '2,4,6-10' (specific pages)";
    }

    /// <summary>
    /// Normalizes a page range string to a standard format.
    /// </summary>
    public static string? Normalize(string? input)
    {
        var parsed = Parse(input);
        if (parsed.IsAll) return "0";
        if (parsed.IsEmpty) return null;
        return parsed.ToDisplayString();
    }

    /// <summary>
    /// Combines multiple page ranges into a single normalized range.
    /// </summary>
    public static ParsedPageRange Combine(params string?[] ranges)
    {
        var allPages = new HashSet<int>();
        var isAll = false;

        foreach (var range in ranges)
        {
            var parsed = Parse(range);
            if (parsed.IsAll)
            {
                isAll = true;
                break;
            }
            foreach (var page in parsed.Pages)
            {
                allPages.Add(page);
            }
        }

        if (isAll)
        {
            return new ParsedPageRange { IsAll = true, RawInput = string.Join(",", ranges.Where(r => !string.IsNullOrEmpty(r))) };
        }

        return new ParsedPageRange
        {
            RawInput = string.Join(",", ranges.Where(r => !string.IsNullOrEmpty(r))),
            Pages = allPages.OrderBy(p => p).ToList()
        };
    }
}
