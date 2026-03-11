namespace DesktopFences.Core.Models;

/// <summary>
/// A rule that automatically assigns files to fences based on matching criteria.
/// </summary>
public class ClassificationRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Priority { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Guid TargetFenceId { get; set; }
    public RuleCondition Condition { get; set; } = new();
}

public class RuleCondition
{
    public RuleMatchType MatchType { get; set; } = RuleMatchType.Extension;

    /// <summary>
    /// Pattern to match against. Interpretation depends on MatchType:
    /// - Extension: ".txt", ".pdf" (comma-separated)
    /// - NameGlob: "report*", "*.tmp"
    /// - DateRange: not used (see DateFrom/DateTo)
    /// - Regex: raw regex pattern
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public long? MinSizeBytes { get; set; }
    public long? MaxSizeBytes { get; set; }
}

public enum RuleMatchType
{
    Extension,
    NameGlob,
    DateRange,
    SizeRange,
    Regex,
    IsDirectory
}
