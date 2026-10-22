using System.Windows;
using System.Windows.Controls;
using HomeWindow.Core;

namespace HomeWindow.Views.Pages;

public partial class DevicesPage : UserControl
{
    public DevicesPage()
    {
        InitializeComponent();
        DataContext = new DeviceBrowser();
        UpdateToggle();
        Theme.Changed += UpdateToggle;
    }

    // The same page, with only the lights: the Lights item in the sidebar
    public bool LightsOnly
    {
        get => ((DeviceBrowser)DataContext).ClassFilter == "light";
        set
        {
            ((DeviceBrowser)DataContext).ClassFilter = value ? "light" : null;
            Heading.Text = Loc.T(value ? "Lampen" : "Apparaten");
            ClassChips.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    void UpdateToggle() => ViewToggle.Content = App.Settings.MainView == "grid" ? Icons.List : Icons.Grid;

    void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Clear();

    void ViewToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.MainView = App.Settings.MainView == "grid" ? "list" : "grid";
        App.Settings.Save();
        Theme.Apply();
    }
}
