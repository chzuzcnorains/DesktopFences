using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopFences.UI.Controls;

public partial class SearchWindow : Window
{
    /// <summary>
    /// Represents a search result item.
    /// </summary>
    public class SearchResult
    {
        public string FilePath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string FenceName { get; set; } = string.Empty;
        public Guid FenceId { get; set; }
        public ImageSource? Icon { get; set; }
    }

    private List<SearchResult> _allItems = [];

    /// <summary>
    /// Fired when user selects a result. Provides (filePath, fenceId).
    /// </summary>
    public event Action<string, Guid>? ResultSelected;

    public SearchWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            // Fade in
            Opacity = 0;
            var animation = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                TimeSpan.FromMilliseconds(150));
            BeginAnimation(OpacityProperty, animation);
        };
        Deactivated += (_, _) => Close();
    }

    /// <summary>
    /// Set the full list of searchable items.
    /// </summary>
    public void SetItems(List<SearchResult> items)
    {
        _allItems = items;
        ResultsList.ItemsSource = items;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            ResultsList.ItemsSource = _allItems;
        }
        else
        {
            ResultsList.ItemsSource = _allItems
                .Where(r => r.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                             || r.FenceName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && SearchBox.IsFocused)
        {
            ResultsList.Focus();
            if (ResultsList.Items.Count > 0)
                ResultsList.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            ActivateSelectedResult();
            e.Handled = true;
        }
    }

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ActivateSelectedResult();
            e.Handled = true;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ActivateSelectedResult();
    }

    private void ActivateSelectedResult()
    {
        if (ResultsList.SelectedItem is SearchResult result)
        {
            ResultSelected?.Invoke(result.FilePath, result.FenceId);
            Close();
        }
    }
}
