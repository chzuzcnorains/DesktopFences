// ══════════════════════════════════════════════════════════════
// DesktopFences.UI / Converters / FileKindToIconConverter.cs
//
// Maps a file-extension / FileKind enum to the matching DrawingImage
// resource from FileTypes.xaml.
//
// USAGE (XAML):
//     <UserControl.Resources>
//         <ui:FileKindToIconConverter x:Key="KindToIcon"/>
//     </UserControl.Resources>
//     <Image Source="{Binding Kind, Converter={StaticResource KindToIcon}}"
//            Width="24" Height="24"/>
// ══════════════════════════════════════════════════════════════
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DesktopFences.UI.Converters;

public sealed class FileKindToIconConverter : IValueConverter
{
    // Extension → resource-key map. Extend this as needed.
    private static readonly System.Collections.Generic.Dictionary<string, string> Map =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // folders
        [""]       = "FileIconFolder",
        ["folder"] = "FileIconFolder",
        // office
        [".doc"]  = "FileIconDoc",  [".docx"] = "FileIconDoc",  [".rtf"] = "FileIconDoc",
        [".xls"]  = "FileIconXls",  [".xlsx"] = "FileIconXls",  [".csv"] = "FileIconXls",
        [".ppt"]  = "FileIconPpt",  [".pptx"] = "FileIconPpt",
        [".pdf"]  = "FileIconPdf",
        // media
        [".png"]  = "FileIconImg",  [".jpg"] = "FileIconImg",  [".jpeg"] = "FileIconImg",
        [".gif"]  = "FileIconImg",  [".bmp"] = "FileIconImg",  [".webp"] = "FileIconImg",
        [".mp4"]  = "FileIconVideo", [".mov"] = "FileIconVideo", [".mkv"] = "FileIconVideo", [".avi"] = "FileIconVideo",
        [".mp3"]  = "FileIconMusic", [".wav"] = "FileIconMusic", [".flac"] = "FileIconMusic", [".m4a"] = "FileIconMusic",
        // code
        [".cs"]   = "FileIconCode", [".js"] = "FileIconCode", [".ts"] = "FileIconCode",
        [".jsx"]  = "FileIconCode", [".tsx"] = "FileIconCode", [".py"] = "FileIconCode",
        [".go"]   = "FileIconCode", [".rs"] = "FileIconCode", [".json"] = "FileIconCode",
        [".xml"]  = "FileIconCode", [".html"] = "FileIconCode", [".css"] = "FileIconCode",
        // archives
        [".zip"]  = "FileIconZip",  [".7z"] = "FileIconZip",  [".rar"] = "FileIconZip",  [".tar"] = "FileIconZip",
        // binaries
        [".exe"]  = "FileIconExe",  [".msi"] = "FileIconExe",  [".dll"] = "FileIconExe",
        // text
        [".txt"]  = "FileIconTxt",  [".md"] = "FileIconTxt",  [".log"] = "FileIconTxt",
        // shortcut
        [".lnk"]  = "FileIconLink", [".url"] = "FileIconLink",
        // fonts
        [".ttf"]  = "FileIconTtf",  [".otf"] = "FileIconTtf",  [".woff"] = "FileIconTtf",
    };

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            string s when Map.TryGetValue(s, out var k) => k,
            _ => "FileIconTxt" // fallback
        };
        return Application.Current.TryFindResource(key) as DrawingImage;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
