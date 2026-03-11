using DesktopFences.Core.Models;

namespace DesktopFences.Core.Services;

/// <summary>
/// Manages desktop pages — virtual groups of fences that can be switched.
/// </summary>
public class PageManager
{
    private readonly List<DesktopPage> _pages = [];
    private int _currentPageIndex;

    public int CurrentPageIndex => _currentPageIndex;
    public int PageCount => _pages.Count;
    public IReadOnlyList<DesktopPage> Pages => _pages;

    /// <summary>
    /// Fired when the active page changes. Provides (oldPageIndex, newPageIndex).
    /// </summary>
    public event Action<int, int>? PageChanged;

    /// <summary>
    /// Initialize with loaded pages. Creates a default page if none exist.
    /// </summary>
    public void Initialize(List<DesktopPage> pages)
    {
        _pages.Clear();
        if (pages.Count == 0)
        {
            _pages.Add(new DesktopPage { PageIndex = 0, Name = "Page 1" });
        }
        else
        {
            _pages.AddRange(pages.OrderBy(p => p.PageIndex));
        }
        _currentPageIndex = 0;
    }

    /// <summary>
    /// Assign a fence to the current page.
    /// </summary>
    public void AssignFenceToCurrentPage(Guid fenceId)
    {
        var page = GetCurrentPage();
        if (page is not null && !page.FenceIds.Contains(fenceId))
            page.FenceIds.Add(fenceId);
    }

    /// <summary>
    /// Remove a fence from all pages.
    /// </summary>
    public void RemoveFence(Guid fenceId)
    {
        foreach (var page in _pages)
            page.FenceIds.Remove(fenceId);
    }

    /// <summary>
    /// Get fence IDs for the current page.
    /// </summary>
    public List<Guid> GetCurrentPageFenceIds()
    {
        return GetCurrentPage()?.FenceIds ?? [];
    }

    /// <summary>
    /// Switch to the next page. Wraps around.
    /// </summary>
    public bool NextPage()
    {
        if (_pages.Count <= 1) return false;
        var oldIndex = _currentPageIndex;
        _currentPageIndex = (_currentPageIndex + 1) % _pages.Count;
        PageChanged?.Invoke(oldIndex, _currentPageIndex);
        return true;
    }

    /// <summary>
    /// Switch to the previous page. Wraps around.
    /// </summary>
    public bool PreviousPage()
    {
        if (_pages.Count <= 1) return false;
        var oldIndex = _currentPageIndex;
        _currentPageIndex = (_currentPageIndex - 1 + _pages.Count) % _pages.Count;
        PageChanged?.Invoke(oldIndex, _currentPageIndex);
        return true;
    }

    /// <summary>
    /// Switch to a specific page by index.
    /// </summary>
    public bool GoToPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _pages.Count || pageIndex == _currentPageIndex)
            return false;
        var oldIndex = _currentPageIndex;
        _currentPageIndex = pageIndex;
        PageChanged?.Invoke(oldIndex, _currentPageIndex);
        return true;
    }

    /// <summary>
    /// Add a new page and return it.
    /// </summary>
    public DesktopPage AddPage(string? name = null)
    {
        var page = new DesktopPage
        {
            PageIndex = _pages.Count,
            Name = name ?? $"Page {_pages.Count + 1}"
        };
        _pages.Add(page);
        return page;
    }

    /// <summary>
    /// Remove a page. Cannot remove the last page.
    /// Fences on removed page get moved to previous page.
    /// </summary>
    public bool RemovePage(int pageIndex)
    {
        if (_pages.Count <= 1 || pageIndex < 0 || pageIndex >= _pages.Count)
            return false;

        var removedPage = _pages[pageIndex];
        var targetPage = _pages[Math.Max(0, pageIndex - 1)];

        // Move fences to target page
        foreach (var fenceId in removedPage.FenceIds)
        {
            if (!targetPage.FenceIds.Contains(fenceId))
                targetPage.FenceIds.Add(fenceId);
        }

        _pages.RemoveAt(pageIndex);

        // Reindex
        for (int i = 0; i < _pages.Count; i++)
            _pages[i].PageIndex = i;

        if (_currentPageIndex >= _pages.Count)
            _currentPageIndex = _pages.Count - 1;

        return true;
    }

    private DesktopPage? GetCurrentPage()
    {
        return _currentPageIndex >= 0 && _currentPageIndex < _pages.Count
            ? _pages[_currentPageIndex]
            : null;
    }
}
