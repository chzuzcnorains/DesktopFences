using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DesktopFences.UI.Converters;

/// <summary>
/// Phase 12 — maps a file extension (or path) to the matching DrawingImage
/// resource from <c>Themes/SystemFileTypes.xaml</c> (Windows-classic page-with-fold).
/// Parallel to <see cref="FileKindToIconConverter"/> but keyed <c>SysFileIcon...</c>.
/// </summary>
public sealed class SystemFileKindToIconConverter : IValueConverter
{
    private static readonly Dictionary<string, string> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [""] = "SysFileIconFolder",
            ["folder"] = "SysFileIconFolder",

            [".doc"] = "SysFileIconDoc", [".docx"] = "SysFileIconDoc", [".rtf"] = "SysFileIconDoc",
            [".xls"] = "SysFileIconXls", [".xlsx"] = "SysFileIconXls", [".csv"] = "SysFileIconXls",
            [".ppt"] = "SysFileIconPpt", [".pptx"] = "SysFileIconPpt",
            [".pdf"] = "SysFileIconPdf",

            [".png"] = "SysFileIconImg", [".jpg"] = "SysFileIconImg", [".jpeg"] = "SysFileIconImg",
            [".gif"] = "SysFileIconImg", [".bmp"] = "SysFileIconImg", [".webp"] = "SysFileIconImg",
            [".mp4"] = "SysFileIconVideo", [".mov"] = "SysFileIconVideo", [".mkv"] = "SysFileIconVideo",
            [".avi"] = "SysFileIconVideo", [".webm"] = "SysFileIconVideo",
            [".mp3"] = "SysFileIconMusic", [".wav"] = "SysFileIconMusic",
            [".flac"] = "SysFileIconMusic", [".m4a"] = "SysFileIconMusic",

            [".cs"] = "SysFileIconCode", [".js"] = "SysFileIconCode", [".ts"] = "SysFileIconCode",
            [".jsx"] = "SysFileIconCode", [".tsx"] = "SysFileIconCode", [".py"] = "SysFileIconCode",
            [".go"] = "SysFileIconCode", [".rs"] = "SysFileIconCode", [".json"] = "SysFileIconCode",
            [".xml"] = "SysFileIconCode", [".html"] = "SysFileIconCode", [".css"] = "SysFileIconCode",
            [".java"] = "SysFileIconCode", [".cpp"] = "SysFileIconCode", [".c"] = "SysFileIconCode",
            [".h"] = "SysFileIconCode", [".sh"] = "SysFileIconCode",

            [".sql"] = "SysFileIconSql",
            [".ps1"] = "SysFileIconPs1",

            [".zip"] = "SysFileIconZip", [".7z"] = "SysFileIconZip", [".rar"] = "SysFileIconZip",
            [".tar"] = "SysFileIconZip", [".gz"] = "SysFileIconZip",

            [".exe"] = "SysFileIconExe", [".msi"] = "SysFileIconExe", [".dll"] = "SysFileIconExe",
            [".bat"] = "SysFileIconExe", [".cmd"] = "SysFileIconExe",

            [".txt"] = "SysFileIconTxt", [".log"] = "SysFileIconTxt", [".ini"] = "SysFileIconTxt",
            [".yml"] = "SysFileIconTxt", [".yaml"] = "SysFileIconTxt",

            [".md"] = "SysFileIconMd",

            [".lnk"] = "SysFileIconLink", [".url"] = "SysFileIconLink",

            [".ttf"] = "SysFileIconTtf", [".otf"] = "SysFileIconTtf",
            [".woff"] = "SysFileIconTtf", [".woff2"] = "SysFileIconTtf",
        };

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            null => "SysFileIconTxt",
            string s when string.IsNullOrEmpty(s) => "SysFileIconFolder",
            string s when Map.TryGetValue(s.StartsWith('.') ? s : Path.GetExtension(s), out var k) => k,
            _ => "SysFileIconTxt"
        };
        return Application.Current?.TryFindResource(key) as DrawingImage;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
