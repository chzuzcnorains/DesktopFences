using DesktopFences.Core.Models;
using DesktopFences.Core.Services;

namespace DesktopFences.Core.Tests;

public class PageManagerTests
{
    [Fact]
    public void Initialize_EmptyList_CreatesDefaultPage()
    {
        var pm = new PageManager();
        pm.Initialize([]);

        Assert.Equal(1, pm.PageCount);
        Assert.Equal(0, pm.CurrentPageIndex);
        Assert.Equal("Page 1", pm.Pages[0].Name);
    }

    [Fact]
    public void Initialize_WithPages_LoadsThem()
    {
        var pm = new PageManager();
        var pages = new List<DesktopPage>
        {
            new() { PageIndex = 0, Name = "Work" },
            new() { PageIndex = 1, Name = "Personal" }
        };
        pm.Initialize(pages);

        Assert.Equal(2, pm.PageCount);
        Assert.Equal("Work", pm.Pages[0].Name);
        Assert.Equal("Personal", pm.Pages[1].Name);
    }

    [Fact]
    public void NextPage_Wraps()
    {
        var pm = new PageManager();
        pm.Initialize([
            new() { PageIndex = 0, Name = "P1" },
            new() { PageIndex = 1, Name = "P2" }
        ]);

        Assert.True(pm.NextPage());
        Assert.Equal(1, pm.CurrentPageIndex);

        Assert.True(pm.NextPage());
        Assert.Equal(0, pm.CurrentPageIndex); // Wrapped
    }

    [Fact]
    public void PreviousPage_Wraps()
    {
        var pm = new PageManager();
        pm.Initialize([
            new() { PageIndex = 0, Name = "P1" },
            new() { PageIndex = 1, Name = "P2" }
        ]);

        Assert.True(pm.PreviousPage());
        Assert.Equal(1, pm.CurrentPageIndex); // Wrapped to last

        Assert.True(pm.PreviousPage());
        Assert.Equal(0, pm.CurrentPageIndex);
    }

    [Fact]
    public void NextPage_SinglePage_ReturnsFalse()
    {
        var pm = new PageManager();
        pm.Initialize([]);

        Assert.False(pm.NextPage());
        Assert.Equal(0, pm.CurrentPageIndex);
    }

    [Fact]
    public void GoToPage_ValidIndex_Switches()
    {
        var pm = new PageManager();
        pm.Initialize([
            new() { PageIndex = 0, Name = "P1" },
            new() { PageIndex = 1, Name = "P2" },
            new() { PageIndex = 2, Name = "P3" }
        ]);

        Assert.True(pm.GoToPage(2));
        Assert.Equal(2, pm.CurrentPageIndex);
    }

    [Fact]
    public void GoToPage_SameIndex_ReturnsFalse()
    {
        var pm = new PageManager();
        pm.Initialize([new() { PageIndex = 0, Name = "P1" }, new() { PageIndex = 1 }]);

        Assert.False(pm.GoToPage(0));
    }

    [Fact]
    public void GoToPage_InvalidIndex_ReturnsFalse()
    {
        var pm = new PageManager();
        pm.Initialize([new() { PageIndex = 0 }]);

        Assert.False(pm.GoToPage(5));
        Assert.False(pm.GoToPage(-1));
    }

    [Fact]
    public void AssignFenceToCurrentPage_AddsFenceId()
    {
        var pm = new PageManager();
        pm.Initialize([]);
        var fenceId = Guid.NewGuid();

        pm.AssignFenceToCurrentPage(fenceId);

        Assert.Contains(fenceId, pm.GetCurrentPageFenceIds());
    }

    [Fact]
    public void AssignFenceToCurrentPage_NoDuplicates()
    {
        var pm = new PageManager();
        pm.Initialize([]);
        var fenceId = Guid.NewGuid();

        pm.AssignFenceToCurrentPage(fenceId);
        pm.AssignFenceToCurrentPage(fenceId);

        Assert.Single(pm.GetCurrentPageFenceIds());
    }

    [Fact]
    public void RemoveFence_RemovesFromAllPages()
    {
        var pm = new PageManager();
        var fenceId = Guid.NewGuid();
        pm.Initialize([
            new() { PageIndex = 0, FenceIds = [fenceId] },
            new() { PageIndex = 1, FenceIds = [fenceId] }
        ]);

        pm.RemoveFence(fenceId);

        Assert.DoesNotContain(fenceId, pm.Pages[0].FenceIds);
        Assert.DoesNotContain(fenceId, pm.Pages[1].FenceIds);
    }

    [Fact]
    public void AddPage_CreatesNewPage()
    {
        var pm = new PageManager();
        pm.Initialize([]);

        var page = pm.AddPage("Custom");

        Assert.Equal(2, pm.PageCount);
        Assert.Equal("Custom", page.Name);
        Assert.Equal(1, page.PageIndex);
    }

    [Fact]
    public void RemovePage_MovesFencesToPrevious()
    {
        var pm = new PageManager();
        var fenceId = Guid.NewGuid();
        pm.Initialize([
            new() { PageIndex = 0, Name = "P1" },
            new() { PageIndex = 1, Name = "P2", FenceIds = [fenceId] }
        ]);

        Assert.True(pm.RemovePage(1));
        Assert.Equal(1, pm.PageCount);
        Assert.Contains(fenceId, pm.Pages[0].FenceIds);
    }

    [Fact]
    public void RemovePage_CannotRemoveLastPage()
    {
        var pm = new PageManager();
        pm.Initialize([]);

        Assert.False(pm.RemovePage(0));
        Assert.Equal(1, pm.PageCount);
    }

    [Fact]
    public void PageChanged_EventFires()
    {
        var pm = new PageManager();
        pm.Initialize([
            new() { PageIndex = 0 },
            new() { PageIndex = 1 }
        ]);

        int? firedOld = null, firedNew = null;
        pm.PageChanged += (oldIdx, newIdx) => { firedOld = oldIdx; firedNew = newIdx; };

        pm.NextPage();

        Assert.Equal(0, firedOld);
        Assert.Equal(1, firedNew);
    }
}
