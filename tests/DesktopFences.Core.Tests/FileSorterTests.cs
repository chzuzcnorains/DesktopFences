using DesktopFences.Core.Models;
using DesktopFences.Core.Services;

namespace DesktopFences.Core.Tests;

public class FileSorterTests
{
    // Identity selector: keys ARE the items, so the comparison logic is tested
    // directly without touching disk.
    private static List<FileSortKey> Sort(
        IEnumerable<FileSortKey> keys, SortField field, SortDirection dir)
        => FileSorter.Sort(keys, field, dir, k => k);

    private static FileSortKey Key(
        string name, string ext = "", long size = 0, long mod = 0, long created = 0)
        => new(name, ext, size, mod, created);

    [Fact]
    public void Name_Ascending_OrdersCaseInsensitive()
    {
        var keys = new[] { Key("banana"), Key("Apple"), Key("cherry") };
        var sorted = Sort(keys, SortField.Name, SortDirection.Ascending);
        Assert.Equal(["Apple", "banana", "cherry"], sorted.Select(k => k.Name));
    }

    [Fact]
    public void Name_Descending_ReversesOrder()
    {
        var keys = new[] { Key("banana"), Key("Apple"), Key("cherry") };
        var sorted = Sort(keys, SortField.Name, SortDirection.Descending);
        Assert.Equal(["cherry", "banana", "Apple"], sorted.Select(k => k.Name));
    }

    [Fact]
    public void Extension_TieBreaksByName()
    {
        var keys = new[]
        {
            Key("zebra", ".txt"),
            Key("alpha", ".txt"),
            Key("doc1",  ".docx"),
        };
        var sorted = Sort(keys, SortField.Extension, SortDirection.Ascending);
        // .docx before .txt; within .txt, name ascending
        Assert.Equal(["doc1", "alpha", "zebra"], sorted.Select(k => k.Name));
    }

    [Fact]
    public void Size_Ascending_OrdersNumeric()
    {
        var keys = new[]
        {
            Key("big",   size: 5000),
            Key("small", size: 10),
            Key("mid",   size: 800),
        };
        var sorted = Sort(keys, SortField.Size, SortDirection.Ascending);
        Assert.Equal(["small", "mid", "big"], sorted.Select(k => k.Name));
    }

    [Fact]
    public void Size_FolderOrFailure_MinusOne_SortsFirstAscending()
    {
        var keys = new[]
        {
            Key("file",   size: 100),
            Key("folder", size: -1),
        };
        var sorted = Sort(keys, SortField.Size, SortDirection.Ascending);
        Assert.Equal(["folder", "file"], sorted.Select(k => k.Name));
    }

    [Fact]
    public void DateModified_Descending_NewestFirst()
    {
        var keys = new[]
        {
            Key("old", mod: 100),
            Key("new", mod: 300),
            Key("mid", mod: 200),
        };
        var sorted = Sort(keys, SortField.DateModified, SortDirection.Descending);
        Assert.Equal(["new", "mid", "old"], sorted.Select(k => k.Name));
    }

    [Fact]
    public void DateCreated_Ascending_OldestFirst()
    {
        var keys = new[]
        {
            Key("c", created: 300),
            Key("a", created: 100),
            Key("b", created: 200),
        };
        var sorted = Sort(keys, SortField.DateCreated, SortDirection.Ascending);
        Assert.Equal(["a", "b", "c"], sorted.Select(k => k.Name));
    }

    [Fact]
    public void Manual_PreservesInputOrder()
    {
        var keys = new[] { Key("z"), Key("a"), Key("m") };
        var sorted = Sort(keys, SortField.Manual, SortDirection.Ascending);
        Assert.Equal(["z", "a", "m"], sorted.Select(k => k.Name));
    }

    [Fact]
    public void Manual_IgnoresDirection()
    {
        var keys = new[] { Key("z"), Key("a"), Key("m") };
        var sorted = Sort(keys, SortField.Manual, SortDirection.Descending);
        Assert.Equal(["z", "a", "m"], sorted.Select(k => k.Name));
    }

    [Fact]
    public void Sort_StableForEqualKeys()
    {
        // Equal sizes keep their original relative order (LINQ OrderBy is stable).
        var keys = new[]
        {
            Key("first",  size: 100),
            Key("second", size: 100),
            Key("third",  size: 100),
        };
        var sorted = Sort(keys, SortField.Size, SortDirection.Ascending);
        Assert.Equal(["first", "second", "third"], sorted.Select(k => k.Name));
    }

    // ── AdjustMoveIndex ──────────────────────────────────────────

    [Theory]
    [InlineData(0, 3, 5, 2)]   // forward move: target shifts down by one
    [InlineData(4, 1, 5, 1)]   // backward move: target unchanged
    [InlineData(2, 2, 5, 2)]   // drop on self (before): no shift, clamps to self
    [InlineData(0, 5, 5, 4)]   // drop past the end clamps to count-1
    [InlineData(3, 0, 5, 0)]   // drop at the very front
    public void AdjustMoveIndex_ComputesTarget(int oldIndex, int insertIndex, int count, int expected)
    {
        Assert.Equal(expected, FileSorter.AdjustMoveIndex(oldIndex, insertIndex, count));
    }

    [Fact]
    public void AdjustMoveIndex_EmptyCollection_ReturnsZero()
    {
        Assert.Equal(0, FileSorter.AdjustMoveIndex(0, 0, 0));
    }
}
