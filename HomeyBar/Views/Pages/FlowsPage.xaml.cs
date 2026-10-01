using System.Windows;
using System.Windows.Controls;
using HomeyBar.Core;

namespace HomeyBar.Views.Pages;

public partial class FlowsPage : UserControl
{
    readonly HomeyStore store = HomeyStore.I;

    public FlowsPage()
    {
        InitializeComponent();
        store.FlowGroups.CollectionChanged += (_, _) => Filter();
        Filter();
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        Hint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Filter();
    }

    void Filter()
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) { Groups.ItemsSource = store.FlowGroups; return; }
        Groups.ItemsSource = store.FlowGroups
            .Select(g => new FlowGroup(g.Key, g.Title, g.Items.Where(f => f.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)), true))
            .Where(g => g.Count > 0)
            .ToList();
    }
}
