using System.IO;
using System.Windows.Media;

namespace DesktopFences.UI.ViewModels;

public class FileItemViewModel : ViewModelBase
{
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
                DisplayName = Path.GetFileName(value);
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
}
