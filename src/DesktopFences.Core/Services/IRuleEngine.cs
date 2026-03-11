using DesktopFences.Core.Models;

namespace DesktopFences.Core.Services;

/// <summary>
/// Evaluates classification rules against files to determine fence assignments.
/// </summary>
public interface IRuleEngine
{
    /// <summary>
    /// Finds the first matching rule for the given file path.
    /// Returns null if no rule matches.
    /// </summary>
    ClassificationRule? Match(string filePath, IReadOnlyList<ClassificationRule> rules);

    /// <summary>
    /// Evaluates a single rule condition against a file.
    /// </summary>
    bool Evaluate(string filePath, RuleCondition condition);
}
