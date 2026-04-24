using System;
using System.Globalization;
using System.Windows.Data;

namespace DesktopFences.UI.Converters;

/// <summary>
/// Picks a readable font size for the overlay letter on a file tile:
/// - empty label (folder)         -> 0   (hidden)
/// - 1-2 chars (W / X / P)        -> 11
/// - 3+ chars (PDF / IMG / MP4)   -> 8.5
/// </summary>
public sealed class LabelLenToFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value as string ?? string.Empty;
        return s.Length switch
        {
            0 => 0.0,
            <= 2 => 11.0,
            _ => 8.5,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
