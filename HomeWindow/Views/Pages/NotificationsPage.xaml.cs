using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HomeWindow.Core;

namespace HomeWindow.Views.Pages;

public partial class NotificationsPage : UserControl
{
    readonly Debounce filter;

    public NotificationsPage()
    {
        InitializeComponent();
        filter = new(Filter);
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        Hint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearBtn.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        filter.Kick();
    }

    void Filter()
    {
        var q = SearchBox.Text.Trim();
        if (((CollectionViewSource)Resources["Grouped"]).View is { } view)
            view.Filter = q.Length == 0 ? null : o => o is NotificationVM n
                && (n.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase) || n.Owner.Contains(q, StringComparison.CurrentCultureIgnoreCase));
    }

    void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Clear();

    void Selection_Changed(object sender, RoutedEventArgs e) =>
        DeleteSelected.Visibility = HomeyStore.I.Notifications.Any(n => n.IsSelected) ? Visibility.Visible : Visibility.Collapsed;

    async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        foreach (var n in HomeyStore.I.Notifications.Where(n => n.IsSelected).ToList()) await HomeyStore.I.DeleteNotificationAsync(n);
        Selection_Changed(sender, e);
    }
}
