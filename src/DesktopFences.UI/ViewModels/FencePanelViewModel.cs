using System.Collections.ObjectModel;
using System.Windows;
using DesktopFences.Core.Models;
using DesktopFences.Core.Services;

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
    private bool _isFocused;
    private bool _isDropHover;
    private bool _isMergeTarget;
    private FileIconStyle? _iconStyleOverride;

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
        _iconStyleOverride = model.IconStyleOverride;

        // Load existing file paths from model
        foreach (var path in model.FilePaths)
            Files.Add(new FileItemViewModel(path));
    }

    public Guid Id => _model.Id;
    public FenceDefinition Model => _model;

    /// <summary>
    /// Whether this fence already contains the file. Windows 路径不区分大小写，
    /// 必须用 OrdinalIgnoreCase（FSW 事件 / 全量扫描 / 拖拽给出的同一路径
    /// 大小写可能不同，大小写敏感比较会产生重复条目）。
    /// </summary>
    public bool ContainsFile(string filePath)
        => _model.FilePaths.Contains(filePath, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Add a file to this fence. Updates both ViewModel and Model and returns the
    /// newly created item (or <c>null</c> if the file was already present).
    /// </summary>
    /// <remarks>
    /// Deliberately appends to the end and does NOT re-sort here: callers rely on
    /// the new item being last (icon loading / drop animation). After a batch of
    /// adds, call <see cref="ResortAfterChange"/> to restore the active sort.
    /// </remarks>
    public FileItemViewModel? AddFile(string filePath)
    {
        if (ContainsFile(filePath)) return null;
        _model.FilePaths.Add(filePath);
        var item = new FileItemViewModel(filePath);
        Files.Add(item);
        return item;
    }

    /// <summary>
    /// Remove a file from this fence.
    /// </summary>
    public void RemoveFile(string filePath)
    {
        _model.FilePaths.RemoveAll(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
        var item = Files.FirstOrDefault(f =>
            string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
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
    /// Reorder <see cref="Files"/> in place by the current SortBy/SortDirection.
    /// No-op in <see cref="SortField.Manual"/> mode (the user owns the order).
    /// </summary>
    public void ApplySort()
    {
        if (_sortBy == SortField.Manual) return;
        if (Files.Count <= 1) return;

        // For metadata-driven sorts, refresh up front so the order matches the
        // values the Detail view shows (and reflects on-disk changes).
        if (_sortBy is SortField.Size or SortField.DateModified or SortField.DateCreated)
            foreach (var f in Files)
                f.RefreshMetadata();

        var sorted = FileSorter.Sort(Files, _sortBy, _sortDirection, f => f.ToSortKey());
        for (int i = 0; i < sorted.Count; i++)
        {
            var currentIndex = Files.IndexOf(sorted[i]);
            if (currentIndex != i)
                Files.Move(currentIndex, i);
        }
        SyncToModel();
    }

    /// <summary>
    /// Re-applies the active sort after files are added / removed / renamed.
    /// In <see cref="SortField.Manual"/> mode the existing order is preserved.
    /// </summary>
    public void ResortAfterChange()
    {
        if (_sortBy != SortField.Manual)
            ApplySort();
    }

    /// <summary>
    /// Phase 14c: move <paramref name="filePath"/> to <paramref name="insertIndex"/>
    /// (a "drop before index" intent) within this fence and switch to
    /// <see cref="SortField.Manual"/> so the user-defined order sticks (Explorer
    /// semantics). The persisted order is the resulting <see cref="Files"/> /
    /// <c>FilePaths</c> sequence. Returns true if anything actually moved or the
    /// mode changed.
    /// </summary>
    public bool ReorderFile(string filePath, int insertIndex)
    {
        var item = Files.FirstOrDefault(f =>
            string.Equals(f.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        if (item is null) return false;

        int oldIndex = Files.IndexOf(item);
        int target = FileSorter.AdjustMoveIndex(oldIndex, insertIndex, Files.Count);

        bool wasManual = _sortBy == SortField.Manual;
        if (oldIndex == target && wasManual) return false; // nothing to do

        SortBy = SortField.Manual; // setter persists to model; ApplySort early-returns for Manual
        if (oldIndex != target)
        {
            Files.Move(oldIndex, target);
            SyncToModel();
        }
        return true;
    }

    /// <summary>
    /// True when the containing fence window has keyboard / activation focus.
    /// Drives the accent-color border glow on FencePanel.
    /// </summary>
    public bool IsFocused
    {
        get => _isFocused;
        set => SetProperty(ref _isFocused, value);
    }

    /// <summary>
    /// True while a drag-drop is hovering over this fence (files being
    /// dropped). Drives a softer accent glow to signal a valid target.
    /// </summary>
    public bool IsDropHover
    {
        get => _isDropHover;
        set => SetProperty(ref _isDropHover, value);
    }

    /// <summary>
    /// True when another fence is being dragged over this one with sufficient
    /// overlap to be merged into this fence as a new tab.
    /// Drives the teal merge-target glow.
    /// </summary>
    public bool IsMergeTarget
    {
        get => _isMergeTarget;
        set => SetProperty(ref _isMergeTarget, value);
    }

    /// <summary>
    /// Phase 13: per-fence file icon style override. <c>null</c> = inherit
    /// the global <see cref="AppSettings.IconStyle"/>; non-null = force this
    /// fence to use the chosen style. Setter also raises a change notification
    /// for <see cref="EffectiveIconStyle"/> so subscribers can refresh tiles.
    /// </summary>
    public FileIconStyle? IconStyleOverride
    {
        get => _iconStyleOverride;
        set
        {
            if (SetProperty(ref _iconStyleOverride, value))
            {
                _model.IconStyleOverride = value;
                OnPropertyChanged(nameof(EffectiveIconStyle));
            }
        }
    }

    /// <summary>
    /// Phase 13: the icon style that should actually drive this fence's tiles.
    /// Resolves to <see cref="IconStyleOverride"/> when set, otherwise reads the
    /// global <c>Application.Resources["IconStyle"]</c> string published by
    /// <c>App.xaml.cs::ApplyIconAppearance</c>. Falls back to
    /// <see cref="FileIconStyle.App"/> if neither is available.
    /// </summary>
    public FileIconStyle EffectiveIconStyle
    {
        get
        {
            if (_iconStyleOverride is { } o) return o;
            if (Application.Current?.TryFindResource("IconStyle") is string s
                && Enum.TryParse<FileIconStyle>(s, out var parsed))
                return parsed;
            return FileIconStyle.App;
        }
    }

    public const double MinWidth = 120;
    public const double MinHeight = 60;
}
