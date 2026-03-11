using System.Collections.ObjectModel;
using System.IO;
using DesktopFences.Core.Models;

namespace DesktopFences.UI.ViewModels;

public class FencePanelViewModel : ViewModelBase
{
    private readonly FenceDefinition _model;

    private string _title = "New Fence";
    private double _x;
    private double _y;
    private double _width = 300;
    private double _height = 200;
    private bool _isRolledUp;
    private bool _isVisible = true;
    private int _pageIndex;
    private int _monitorIndex;
    private string? _portalPath;
    private string? _backgroundColor;
    private string? _titleBarColor;
    private string? _textColor;
    private double _opacity = 1.0;
    private ViewMode _viewMode = ViewMode.Icon;
    private SortField _sortBy = SortField.Name;
    private SortDirection _sortDirection = SortDirection.Ascending;

    public ObservableCollection<FileItemViewModel> Files { get; } = [];

    public FencePanelViewModel() : this(new FenceDefinition()) { }

    public FencePanelViewModel(FenceDefinition model)
    {
        _model = model;
        _title = model.Title;
        _x = model.Bounds.X;
        _y = model.Bounds.Y;
        _width = model.Bounds.Width;
        _height = model.Bounds.Height;
        _isRolledUp = model.IsRolledUp;
        _isVisible = model.IsVisible;
        _pageIndex = model.PageIndex;
        _monitorIndex = model.MonitorIndex;
        _portalPath = model.PortalPath;
        _backgroundColor = model.BackgroundColor;
        _titleBarColor = model.TitleBarColor;
        _textColor = model.TextColor;
        _opacity = model.Opacity;
        _viewMode = model.ViewMode;
        _sortBy = model.SortBy;
        _sortDirection = model.SortDirection;

        // Load existing file paths from model
        foreach (var path in model.FilePaths)
            Files.Add(new FileItemViewModel(path));
    }

    public Guid Id => _model.Id;
    public FenceDefinition Model => _model;

    /// <summary>
    /// Add a file to this fence. Updates both ViewModel and Model.
    /// </summary>
    public void AddFile(string filePath)
    {
        if (_model.FilePaths.Contains(filePath)) return;
        _model.FilePaths.Add(filePath);
        Files.Add(new FileItemViewModel(filePath));
    }

    /// <summary>
    /// Remove a file from this fence.
    /// </summary>
    public void RemoveFile(string filePath)
    {
        _model.FilePaths.Remove(filePath);
        var item = Files.FirstOrDefault(f => f.FilePath == filePath);
        if (item is not null) Files.Remove(item);
    }

    /// <summary>
    /// Sync model FilePaths from the current Files collection.
    /// </summary>
    public void SyncToModel()
    {
        _model.FilePaths = Files.Select(f => f.FilePath).ToList();
    }

    public string Title
    {
        get => _title;
        set { if (SetProperty(ref _title, value)) _model.Title = value; }
    }

    public double X
    {
        get => _x;
        set { if (SetProperty(ref _x, value)) _model.Bounds.X = value; }
    }

    public double Y
    {
        get => _y;
        set { if (SetProperty(ref _y, value)) _model.Bounds.Y = value; }
    }

    public double Width
    {
        get => _width;
        set
        {
            var clamped = Math.Max(value, MinWidth);
            if (SetProperty(ref _width, clamped)) _model.Bounds.Width = clamped;
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            var clamped = Math.Max(value, MinHeight);
            if (SetProperty(ref _height, clamped)) _model.Bounds.Height = clamped;
        }
    }

    public bool IsRolledUp
    {
        get => _isRolledUp;
        set { if (SetProperty(ref _isRolledUp, value)) _model.IsRolledUp = value; }
    }

    /// <summary>
    /// Stores the full height before rollup, so we can restore it.
    /// </summary>
    public double ExpandedHeight { get; set; }

    public bool IsVisible
    {
        get => _isVisible;
        set { if (SetProperty(ref _isVisible, value)) _model.IsVisible = value; }
    }

    public int PageIndex
    {
        get => _pageIndex;
        set { if (SetProperty(ref _pageIndex, value)) _model.PageIndex = value; }
    }

    public int MonitorIndex
    {
        get => _monitorIndex;
        set { if (SetProperty(ref _monitorIndex, value)) _model.MonitorIndex = value; }
    }

    public string? PortalPath
    {
        get => _portalPath;
        set
        {
            if (SetProperty(ref _portalPath, value))
            {
                _model.PortalPath = value;
                OnPropertyChanged(nameof(IsPortalMode));
                OnPropertyChanged(nameof(PortalDisplayPath));
            }
        }
    }

    public bool IsPortalMode => !string.IsNullOrEmpty(_portalPath);

    /// <summary>
    /// Short display path for portal tooltip (e.g. "D:\Documents").
    /// </summary>
    public string? PortalDisplayPath => _portalPath;

    public string? BackgroundColor
    {
        get => _backgroundColor;
        set { if (SetProperty(ref _backgroundColor, value)) _model.BackgroundColor = value; }
    }

    public string? TitleBarColor
    {
        get => _titleBarColor;
        set { if (SetProperty(ref _titleBarColor, value)) _model.TitleBarColor = value; }
    }

    public string? TextColor
    {
        get => _textColor;
        set { if (SetProperty(ref _textColor, value)) _model.TextColor = value; }
    }

    public double FenceOpacity
    {
        get => _opacity;
        set { if (SetProperty(ref _opacity, value)) _model.Opacity = value; }
    }

    public ViewMode ViewMode
    {
        get => _viewMode;
        set { if (SetProperty(ref _viewMode, value)) _model.ViewMode = value; }
    }

    public SortField SortBy
    {
        get => _sortBy;
        set
        {
            if (SetProperty(ref _sortBy, value))
            {
                _model.SortBy = value;
                ApplySort();
            }
        }
    }

    public SortDirection SortDirection
    {
        get => _sortDirection;
        set
        {
            if (SetProperty(ref _sortDirection, value))
            {
                _model.SortDirection = value;
                ApplySort();
            }
        }
    }

    /// <summary>
    /// Sort files by the current SortBy/SortDirection settings.
    /// </summary>
    public void ApplySort()
    {
        if (Files.Count <= 1) return;

        var sorted = SortBy switch
        {
            SortField.Name => _sortDirection == SortDirection.Ascending
                ? Files.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
                : Files.OrderByDescending(f => f.DisplayName, StringComparer.OrdinalIgnoreCase),
            SortField.Extension => _sortDirection == SortDirection.Ascending
                ? Files.OrderBy(f => f.Extension, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
                : Files.OrderByDescending(f => f.Extension, StringComparer.OrdinalIgnoreCase)
                      .ThenBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase),
            SortField.Size => SortByFileInfo(f => new FileInfo(f.FilePath).Length),
            SortField.DateModified => SortByFileInfo(f => File.GetLastWriteTime(f.FilePath).Ticks),
            SortField.DateCreated => SortByFileInfo(f => File.GetCreationTime(f.FilePath).Ticks),
            _ => Files.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase)
        };

        var list = sorted.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var currentIndex = Files.IndexOf(list[i]);
            if (currentIndex != i)
                Files.Move(currentIndex, i);
        }
        SyncToModel();
    }

    private IOrderedEnumerable<FileItemViewModel> SortByFileInfo(Func<FileItemViewModel, long> selector)
    {
        return _sortDirection == SortDirection.Ascending
            ? Files.OrderBy(f => SafeGetValue(f, selector))
            : Files.OrderByDescending(f => SafeGetValue(f, selector));
    }

    private static long SafeGetValue(FileItemViewModel f, Func<FileItemViewModel, long> selector)
    {
        try { return selector(f); }
        catch { return 0; }
    }

    public const double MinWidth = 120;
    public const double MinHeight = 60;
}
