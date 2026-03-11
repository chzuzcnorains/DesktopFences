using DesktopFences.Core.Models;
using DesktopFences.Core.Services;

namespace DesktopFences.Core.Tests;

public class RuleEngineTests
{
    private readonly RuleEngine _engine = new();
    private readonly Guid _targetFenceId = Guid.NewGuid();

    // ── Extension matching ───────────────────────────────────

    [Theory]
    [InlineData(@"C:\Desktop\photo.jpg", ".jpg,.png,.gif", true)]
    [InlineData(@"C:\Desktop\photo.PNG", ".jpg,.png,.gif", true)]
    [InlineData(@"C:\Desktop\document.pdf", ".jpg,.png,.gif", false)]
    [InlineData(@"C:\Desktop\photo.jpg", "jpg, png", true)]       // without dots
    [InlineData(@"C:\Desktop\noext", ".jpg,.png", false)]
    public void ExtensionMatch(string filePath, string pattern, bool expected)
    {
        var condition = new RuleCondition
        {
            MatchType = RuleMatchType.Extension,
            Pattern = pattern
        };
        Assert.Equal(expected, _engine.Evaluate(filePath, condition));
    }

    // ── NameGlob matching ────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Desktop\report_2024.xlsx", "report*", true)]
    [InlineData(@"C:\Desktop\report_2024.xlsx", "report_????.xlsx", true)]
    [InlineData(@"C:\Desktop\data.csv", "report*", false)]
    [InlineData(@"C:\Desktop\temp.tmp", "*.tmp", true)]
    public void GlobMatch(string filePath, string pattern, bool expected)
    {
        var condition = new RuleCondition
        {
            MatchType = RuleMatchType.NameGlob,
            Pattern = pattern
        };
        Assert.Equal(expected, _engine.Evaluate(filePath, condition));
    }

    // ── Regex matching ───────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Desktop\IMG_001.jpg", @"^IMG_\d+\.jpg$", true)]
    [InlineData(@"C:\Desktop\DOC_001.pdf", @"^IMG_\d+\.jpg$", false)]
    [InlineData(@"C:\Desktop\test.txt", "[invalid regex", false)] // invalid regex → no match
    public void RegexMatch(string filePath, string pattern, bool expected)
    {
        var condition = new RuleCondition
        {
            MatchType = RuleMatchType.Regex,
            Pattern = pattern
        };
        Assert.Equal(expected, _engine.Evaluate(filePath, condition));
    }

    // ── Priority ordering ────────────────────────────────────

    [Fact]
    public void MatchReturnsHighestPriorityRule()
    {
        var lowPriority = MakeRule(RuleMatchType.Extension, ".txt", priority: 10);
        var highPriority = MakeRule(RuleMatchType.Extension, ".txt", priority: 1);

        var rules = new List<ClassificationRule> { lowPriority, highPriority };

        var result = _engine.Match(@"C:\Desktop\readme.txt", rules);
        Assert.NotNull(result);
        Assert.Equal(highPriority.Id, result.Id);
    }

    [Fact]
    public void MatchSkipsDisabledRules()
    {
        var disabled = MakeRule(RuleMatchType.Extension, ".txt", priority: 1);
        disabled.IsEnabled = false;
        var enabled = MakeRule(RuleMatchType.Extension, ".txt", priority: 10);

        var rules = new List<ClassificationRule> { disabled, enabled };

        var result = _engine.Match(@"C:\Desktop\readme.txt", rules);
        Assert.NotNull(result);
        Assert.Equal(enabled.Id, result.Id);
    }

    [Fact]
    public void MatchReturnsNullWhenNoRulesMatch()
    {
        var rule = MakeRule(RuleMatchType.Extension, ".jpg");

        var result = _engine.Match(@"C:\Desktop\readme.txt", [rule]);
        Assert.Null(result);
    }

    // ── Empty/null pattern edge cases ────────────────────────

    [Theory]
    [InlineData(RuleMatchType.Extension, "")]
    [InlineData(RuleMatchType.NameGlob, "")]
    [InlineData(RuleMatchType.Regex, "")]
    [InlineData(RuleMatchType.Extension, null)]
    public void EmptyPatternDoesNotMatch(RuleMatchType matchType, string? pattern)
    {
        var condition = new RuleCondition
        {
            MatchType = matchType,
            Pattern = pattern ?? string.Empty
        };
        Assert.False(_engine.Evaluate(@"C:\Desktop\test.txt", condition));
    }

    // ── GlobToRegex conversion ───────────────────────────────

    [Theory]
    [InlineData("*.txt", @"^.*\.txt$")]
    [InlineData("report_????", @"^report_....$")]
    [InlineData("file.name", @"^file\.name$")]
    public void GlobToRegexConversion(string glob, string expectedRegex)
    {
        Assert.Equal(expectedRegex, RuleEngine.GlobToRegex(glob));
    }

    // ── Helpers ──────────────────────────────────────────────

    private ClassificationRule MakeRule(RuleMatchType matchType, string pattern, int priority = 0)
    {
        return new ClassificationRule
        {
            Name = $"Test {matchType}",
            Priority = priority,
            IsEnabled = true,
            TargetFenceId = _targetFenceId,
            Condition = new RuleCondition
            {
                MatchType = matchType,
                Pattern = pattern
            }
        };
    }
}
