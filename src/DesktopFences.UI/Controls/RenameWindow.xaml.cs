using System.Windows;
using System.Windows.Input;

namespace DesktopFences.UI.Controls;

public partial class RenameWindow : Window
{
    public string? NewName { get; private set; }

    public RenameWindow(string currentName)
    {
        InitializeComponent();
        OriginalNameText.Text = currentName;
        NewNameBox.Text = currentName;
        Loaded += (_, _) =>
        {
            NewNameBox.Focus();
            NewNameBox.SelectAll();
        };
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var trimmed = NewNameBox.Text.Trim();
        if (!string.IsNullOrEmpty(trimmed))
        {
            NewName = trimmed;
            DialogResult = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            OkButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelButton_Click(sender, e);
            e.Handled = true;
        }
    }
}
