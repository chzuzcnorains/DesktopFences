using System.Windows;
using System.Windows.Input;

namespace DesktopFences.UI.Controls;

public partial class RenameWindow : Window
{
    public string? NewName { get; private set; }

    /// <summary>
    /// Fired when the user confirms a non-empty new name. The window is shown
    /// non-modally (Show, not ShowDialog) — a modal dialog would make Win32
    /// EnableWindow(FALSE) every other top-level window on the thread, freezing
    /// all fences and the desktop icon overlay (see docs/bug/settings_modal_disables_fences.md).
    /// </summary>
    public event Action<string>? RenameConfirmed;

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
            RenameConfirmed?.Invoke(trimmed);
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
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
