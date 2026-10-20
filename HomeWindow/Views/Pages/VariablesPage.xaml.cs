using System.Windows;
using System.Windows.Controls;
using HomeWindow.Core;

namespace HomeWindow.Views.Pages;

public partial class VariablesPage : UserControl
{
    public VariablesPage() => InitializeComponent();

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.Trim();
        Hint.Visibility = q.Length == 0 && SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearBtn.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        VarList.Items.Filter = q.Length == 0 ? null : o => o is VariableVM v && v.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Clear();
}
