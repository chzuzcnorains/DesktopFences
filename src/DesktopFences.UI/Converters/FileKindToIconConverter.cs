using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DesktopFences.UI.Converters;

/// <summary>
/// Maps a file extension (or path) to the matching DrawingImage resource
/// from FileTypes.xaml. Bind against FileItemViewModel.Extension or FilePath.
/// </summary>
public sealed class FileKindToIconConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [""] = "FileIconFolder",
            ["folder"] = "FileIconFolder",

            [".doc"] = "FileIconDoc", [".docx"] = "FileIconDoc", [".rtf"] = "FileIconDoc",
            [".xls"] = "FileIconXls", [".xlsx"] = "FileIconXls", [".csv"] = "FileIconXls",
            [".ppt"] = "FileIconPpt", [".pptx"] = "FileIconPpt",
            [".pdf"] = "FileIconPdf",

            [".png"] = "FileIconImg", [".jpg"] = "FileIconImg", [".jpeg"] = "FileIconImg",
            [".gif"] = "FileIconImg", [".bmp"] = "FileIconImg", [".webp"] = "FileIconImg",
            [".mp4"] = "FileIconVideo", [".mov"] = "FileIconVideo", [".mkv"] = "FileIconVideo", [".avi"] = "FileIconVideo", [".webm"] = "FileIconVideo",
            [".mp3"] = "FileIconMusic", [".wav"] = "FileIconMusic", [".flac"] = "FileIconMusic", [".m4a"] = "FileIconMusic",

            [".cs"] = "FileIconCode", [".js"] = "FileIconCode", [".ts"] = "FileIconCode",
            [".jsx"] = "FileIconCode", [".tsx"] = "FileIconCode", [".py"] = "FileIconCode",
            [".go"] = "FileIconCode", [".rs"] = "FileIconCode", [".json"] = "FileIconCode",
            [".xml"] = "FileIconCode", [".html"] = "FileIconCode", [".css"] = "FileIconCode",
            [".java"] = "FileIconCode", [".cpp"] = "FileIconCode", [".c"] = "FileIconCode",
            [".h"] = "FileIconCode", [".sh"] = "FileIconCode", [".ps1"] = "FileIconCode",

            [".zip"] = "FileIconZip", [".7z"] = "FileIconZip", [".rar"] = "FileIconZip",
            [".tar"] = "FileIconZip", [".gz"] = "FileIconZip",

            [".exe"] = "FileIconExe", [".msi"] = "FileIconExe", [".dll"] = "FileIconExe",
            [".bat"] = "FileIconExe", [".cmd"] = "FileIconExe",

            [".txt"] = "FileIconTxt", [".md"] = "FileIconTxt", [".log"] = "FileIconTxt",
            [".ini"] = "FileIconTxt", [".yml"] = "FileIconTxt", [".yaml"] = "FileIconTxt",

            [".lnk"] = "FileIconLink", [".url"] = "FileIconLink",

            [".ttf"] = "FileIconTtf", [".otf"] = "FileIconTtf", [".woff"] = "FileIconTtf", [".woff2"] = "FileIconTtf",
        };

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            null => "FileIconTxt",
            string s when string.IsNullOrEmpty(s) => "FileIconFolder",
            string s when Map.TryGetValue(s.StartsWith('.') ? s : Path.GetExtension(s), out var k) => k,
            _ => "FileIconTxt"
        };
        return Application.Current?.TryFindResource(key) as DrawingImage;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
