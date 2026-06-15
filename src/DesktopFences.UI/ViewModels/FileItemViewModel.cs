using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using DesktopFences.Core.Services;

namespace DesktopFences.UI.ViewModels;

public class FileItemViewModel : ViewModelBase
{
    /// <summary>
    /// System-style badge text (Phase 12). Differs from <see cref="ExtToLabel"/>
    /// in that img/video/exe/folder return empty (the System DrawingImage already
    /// contains the visual cue — photo / filmstrip / monitor / folder shape).
    /// Other kinds carry shorter, system-classic labels (W/X/P/PDF/SQL/MD/...).
    /// </summary>
    private static readonly Dictionary<string, string> ExtToSystemBadge =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".doc"] = "W", [".docx"] = "W", [".rtf"] = "W",
            [".xls"] = "X", [".xlsx"] = "X", [".csv"] = "X",
            [".ppt"] = "P", [".pptx"] = "P",
            [".pdf"] = "PDF",
            // img — empty (photo glyph in DrawingImage)
            [".png"] = "", [".jpg"] = "", [".jpeg"] = "",
            [".gif"] = "", [".bmp"] = "", [".webp"] = "",
            // video — empty (filmstrip glyph)
            [".mp4"] = "", [".mov"] = "", [".mkv"] = "", [".avi"] = "", [".webm"] = "",
            // music — same as App style
            [".mp3"] = "♪", [".wav"] = "♪", [".flac"] = "♪", [".m4a"] = "♪",
            [".cs"] = "<>", [".js"] = "<>", [".ts"] = "<>",
            [".jsx"] = "<>", [".tsx"] = "<>", [".py"] = "<>",
            [".go"] = "<>", [".rs"] = "<>", [".json"] = "<>",
            [".xml"] = "<>", [".html"] = "<>", [".css"] = "<>",
            [".java"] = "<>", [".cpp"] = "<>", [".c"] = "<>",
            [".h"] = "<>", [".sh"] = "<>",
            [".sql"] = "SQL",
            [".ps1"] = ">_",
            [".zip"] = "ZIP", [".7z"] = "ZIP", [".rar"] = "ZIP",
            [".tar"] = "ZIP", [".gz"] = "ZIP",
            // exe — empty (monitor glyph)
            [".exe"] = "", [".msi"] = "", [".dll"] = "",
            [".bat"] = "", [".cmd"] = "",
            [".txt"] = "TXT", [".log"] = "TXT", [".ini"] = "TXT",
            [".yml"] = "TXT", [".yaml"] = "TXT",
            [".md"] = "MD",
            [".lnk"] = "↗", [".url"] = "↗",
            [".ttf"] = "Aa", [".otf"] = "Aa", [".woff"] = "Aa", [".woff2"] = "Aa",
        };

    private static readonly Dictionary<string, string> ExtToLabel =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".doc"] = "W", [".docx"] = "W", [".rtf"] = "W",
            [".xls"] = "X", [".xlsx"] = "X", [".csv"] = "X",
            [".ppt"] = "P", [".pptx"] = "P",
            [".pdf"] = "PDF",
            [".png"] = "IMG", [".jpg"] = "IMG", [".jpeg"] = "IMG",
            [".gif"] = "IMG", [".bmp"] = "IMG", [".webp"] = "IMG",
            [".mp4"] = "MP4", [".mov"] = "MP4", [".mkv"] = "MP4", [".avi"] = "MP4", [".webm"] = "MP4",
            [".mp3"] = "♪", [".wav"] = "♪", [".flac"] = "♪", [".m4a"] = "♪",
            [".cs"] = "<>", [".js"] = "<>", [".ts"] = "<>",
            [".jsx"] = "<>", [".tsx"] = "<>", [".py"] = "<>",
            [".go"] = "<>", [".rs"] = "<>", [".json"] = "<>",
            [".xml"] = "<>", [".html"] = "<>", [".css"] = "<>",
            [".java"] = "<>", [".cpp"] = "<>", [".c"] = "<>",
            [".h"] = "<>", [".sh"] = "<>", [".ps1"] = ">_",
            [".zip"] = "ZIP", [".7z"] = "ZIP", [".rar"] = "ZIP",
            [".tar"] = "ZIP", [".gz"] = "ZIP",
            [".exe"] = "EXE", [".msi"] = "EXE", [".dll"] = "EXE",
            [".bat"] = "EXE", [".cmd"] = "EXE",
            [".txt"] = "TXT", [".md"] = "TXT", [".log"] = "TXT",
            [".ini"] = "TXT", [".yml"] = "TXT", [".yaml"] = "TXT",
            [".lnk"] = "↗", [".url"] = "↗",
            [".ttf"] = "Aa", [".otf"] = "Aa", [".woff"] = "Aa", [".woff2"] = "Aa",
        };
    private string _filePath = string.Empty;
    private string _displayName = string.Empty;
    private ImageSource? _icon;
    private bool _isSelected;
    private bool _isRenaming;

    // Phase 14: lazily-loaded file metadata for the Detail view / sorting.
    // Loaded on first access (single FileInfo read) and invalidated by
    // RefreshMetadata() when the file is renamed / changed on disk.
    private bool _metaLoaded;
    private long _sizeBytes;       // -1 for folders or IO failure
    private DateTime _dateModified;
    private DateTime _dateCreated;

    public FileItemViewModel(string filePath)
    {
        _filePath = filePath;
        _displayName = GetDisplayNameWithoutLnkExtension(filePath);
    }

    private static string GetDisplayNameWithoutLnkExtension(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.Substring(0, fileName.Length - 4);
        }
        return fileName;
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                DisplayName = GetDisplayNameWithoutLnkExtension(value);
                OnPropertyChanged(nameof(Extension));
                OnPropertyChanged(nameof(KindLabel));
                OnPropertyChanged(nameof(SystemBadgeText));
                OnPropertyChanged(nameof(IsDirectory));
                RefreshMetadata(); // path changed (rename) → invalidate size/date cache
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public ImageSource? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsRenaming
    {
        get => _isRenaming;
        set => SetProperty(ref _isRenaming, value);
    }

    public string Extension => Path.GetExtension(_filePath);
    public bool IsDirectory => Directory.Exists(_filePath);

    // ── Phase 14: file metadata (Detail view columns + sorting) ──────

    /// <summary>File length in bytes; <c>-1</c> for folders or on IO failure.</summary>
    public long SizeBytes { get { EnsureMeta(); return _sizeBytes; } }

    /// <summary>Last-write time; <c>default</c> on IO failure.</summary>
    public DateTime DateModified { get { EnsureMeta(); return _dateModified; } }

    /// <summary>Creation time; <c>default</c> on IO failure.</summary>
    public DateTime DateCreated { get { EnsureMeta(); return _dateCreated; } }

    /// <summary>Human-readable size for the Detail column (empty for folders).</summary>
    public string SizeDisplay
    {
        get
        {
            var bytes = SizeBytes;
            return bytes < 0 ? string.Empty : FormatSize(bytes);
        }
    }

    /// <summary>Formatted last-write time for the Detail column.</summary>
    public string DateModifiedDisplay
    {
        get
        {
            var dt = DateModified;
            return dt == default ? string.Empty : dt.ToString("yyyy/MM/dd HH:mm");
        }
    }

    /// <summary>
    /// Pure sort key consumed by <see cref="DesktopFences.Core.Services.FileSorter"/>.
    /// Built from the lazily-loaded metadata so the Core sorter never touches disk.
    /// </summary>
    public FileSortKey ToSortKey()
        => new(DisplayName, Extension, SizeBytes, DateModified.Ticks, DateCreated.Ticks);

    /// <summary>
    /// Reads size + timestamps from disk on first access. All IO is guarded:
    /// folders / missing files / permission failures fall back to -1 / default
    /// so sorting and the Detail view stay deterministic.
    /// </summary>
    private void EnsureMeta()
    {
        if (_metaLoaded) return;
        _metaLoaded = true;
        try
        {
            if (IsDirectory)
            {
                _sizeBytes = -1;
                _dateModified = Directory.GetLastWriteTime(_filePath);
                _dateCreated = Directory.GetCreationTime(_filePath);
            }
            else
            {
                var fi = new FileInfo(_filePath);
                _sizeBytes = fi.Exists ? fi.Length : -1;
                _dateModified = fi.LastWriteTime;
                _dateCreated = fi.CreationTime;
            }
        }
        catch
        {
            _sizeBytes = -1;
            _dateModified = default;
            _dateCreated = default;
        }
    }

    /// <summary>
    /// Invalidates the cached metadata and re-raises change notifications so the
    /// Detail view re-reads size / date. Called on rename (FilePath setter) and
    /// when a portal refresh signals the file changed on disk.
    /// </summary>
    public void RefreshMetadata()
    {
        _metaLoaded = false;
        OnPropertyChanged(nameof(SizeBytes));
        OnPropertyChanged(nameof(DateModified));
        OnPropertyChanged(nameof(DateCreated));
        OnPropertyChanged(nameof(SizeDisplay));
        OnPropertyChanged(nameof(DateModifiedDisplay));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = -1;
        do { value /= 1024; unit++; } while (value >= 1024 && unit < units.Length - 1);
        return $"{value:0.#} {units[unit]}";
    }

    /// <summary>
    /// Short overlay label rendered on top of the colored file-type tile
    /// (e.g. "W", "PDF", "IMG"). Empty string for folders — tile-letter-layer
    /// will hide itself when length == 0.
    /// </summary>
    public string KindLabel
    {
        get
        {
            if (IsDirectory) return string.Empty;
            var ext = Extension;
            if (string.IsNullOrEmpty(ext)) return string.Empty;
            return ExtToLabel.TryGetValue(ext, out var label) ? label : "TXT";
        }
    }

    /// <summary>
    /// Phase 12: badge label for System-style icons. Empty for folders /
    /// img / video / exe (those use specialized DrawingImage shapes,
    /// no overlay text wanted).
    /// </summary>
    public string SystemBadgeText
    {
        get
        {
            if (IsDirectory) return string.Empty;
            var ext = Extension;
            if (string.IsNullOrEmpty(ext)) return string.Empty;
            return ExtToSystemBadge.TryGetValue(ext, out var label) ? label : "TXT";
        }
    }
}
