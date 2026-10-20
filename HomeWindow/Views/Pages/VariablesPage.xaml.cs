using System.Windows;
using System.Windows.Controls;
using HomeWindow.Core;

namespace HomeWindow.Views.Pages;

public partial class VariablesPage : UserControl
{
    readonly Debounce filter;

    public VariablesPage()
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
        VarList.Items.Filter = q.Length == 0 ? null : o => o is VariableVM v && v.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Clear();
}
