using System.Text.RegularExpressions;
using DesktopFences.Core.Models;

namespace DesktopFences.Core.Services;

/// <summary>
/// Evaluates classification rules against file paths.
/// Rules are evaluated in priority order (lower number = higher priority).
/// </summary>
public class RuleEngine : IRuleEngine
{
    /// <summary>
    /// Finds the first matching enabled rule for the given file path.
    /// Rules are sorted by Priority (ascending) before evaluation.
    /// </summary>
    public ClassificationRule? Match(string filePath, IReadOnlyList<ClassificationRule> rules)
    {
        var sorted = rules
            .Where(r => r.IsEnabled)
            .OrderBy(r => r.Priority);

        foreach (var rule in sorted)
        {
            if (Evaluate(filePath, rule.Condition))
                return rule;
        }

        return null;
    }

    /// <summary>
    /// Evaluates a single rule condition against a file.
    /// </summary>
    public bool Evaluate(string filePath, RuleCondition condition)
    {
        return condition.MatchType switch
        {
            RuleMatchType.Extension => EvaluateExtension(filePath, condition.Pattern),
            RuleMatchType.NameGlob => EvaluateGlob(filePath, condition.Pattern),
            RuleMatchType.DateRange => EvaluateDateRange(filePath, condition.DateFrom, condition.DateTo),
            RuleMatchType.SizeRange => EvaluateSizeRange(filePath, condition.MinSizeBytes, condition.MaxSizeBytes),
            RuleMatchType.Regex => EvaluateRegex(filePath, condition.Pattern),
            RuleMatchType.IsDirectory => EvaluateIsDirectory(filePath),
            _ => false
        };
    }

    private static bool EvaluateExtension(string filePath, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        var fileExt = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(fileExt)) return false;

        var extensions = pattern
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var ext in extensions)
        {
            // Normalize: ensure leading dot
            var normalized = ext.StartsWith('.') ? ext : "." + ext;
            if (string.Equals(fileExt, normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool EvaluateGlob(string filePath, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        var fileName = Path.GetFileName(filePath);
        var regexPattern = GlobToRegex(pattern);

        return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
    }

    private static bool EvaluateDateRange(string filePath, DateTime? from, DateTime? to)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists) return false;

            var lastWrite = info.LastWriteTime;
            if (from.HasValue && lastWrite < from.Value) return false;
            if (to.HasValue && lastWrite > to.Value) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool EvaluateSizeRange(string filePath, long? minBytes, long? maxBytes)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists) return false;

            if (minBytes.HasValue && info.Length < minBytes.Value) return false;
            if (maxBytes.HasValue && info.Length > maxBytes.Value) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool EvaluateRegex(string filePath, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;

        try
        {
            var fileName = Path.GetFileName(filePath);
            return Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase);
        }
        catch (RegexParseException)
        {
            return false;
        }
    }

    private static bool EvaluateIsDirectory(string filePath)
    {
        try { return Directory.Exists(filePath); }
        catch { return false; }
    }

    /// <summary>
    /// Converts a simple glob pattern to a regex pattern.
    /// Supports: * (any chars), ? (single char).
    /// </summary>
    internal static string GlobToRegex(string glob)
    {
        var escaped = Regex.Escape(glob);
        // Unescape the glob wildcards
        escaped = escaped.Replace(@"\*", ".*");
        escaped = escaped.Replace(@"\?", ".");
        return "^" + escaped + "$";
    }
}
