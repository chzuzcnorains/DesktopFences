using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace DesktopFences.UI.ViewModels;

public class FileItemViewModel : ViewModelBase
{
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

    public FileItemViewModel(string filePath)
    {
        _filePath = filePath;
        _displayName = Path.GetFileName(filePath);
    }

    public string FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                DisplayName = Path.GetFileName(value);
                OnPropertyChanged(nameof(Extension));
                OnPropertyChanged(nameof(KindLabel));
                OnPropertyChanged(nameof(IsDirectory));
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
}
