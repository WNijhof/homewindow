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
            UpdateToggle();
        }
    }

    // Devices and Lights each keep their own list or grid
    string View
    {
        get => LightsOnly ? App.Settings.LightsView : App.Settings.MainView;
        set { if (LightsOnly) App.Settings.LightsView = value; else App.Settings.MainView = value; }
    }

    void UpdateToggle() => ViewToggle.Content = View == "grid" ? Icons.List : Icons.Grid;

    void ClearSearch_Click(object sender, RoutedEventArgs e) => SearchBox.Clear();

    void ViewToggle_Click(object sender, RoutedEventArgs e)
    {
        View = View == "grid" ? "list" : "grid";
        App.Settings.Save();
        Theme.Apply();
    }
}
