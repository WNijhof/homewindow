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

    void UpdateToggle() => ViewToggle.Content = App.Settings.MainView == "grid" ? Icons.List : Icons.Grid;

    void ViewToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.MainView = App.Settings.MainView == "grid" ? "list" : "grid";
        App.Settings.Save();
        Theme.Apply();
    }
}
