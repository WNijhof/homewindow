using System.Windows;
using System.Windows.Controls;
using HomeWindow.Core;

namespace HomeWindow.Views.Pages;

public partial class EnergyPage : UserControl
{
    public EnergyPage() => InitializeComponent();

    void Reload_Click(object sender, RoutedEventArgs e)
    {
        HomeyStore.I.EnergyReportRequested = true;
        HomeyStore.I.Kick();
    }
}
