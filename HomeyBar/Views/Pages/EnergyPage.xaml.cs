using System.Windows;
using System.Windows.Controls;
using HomeyBar.Core;

namespace HomeyBar.Views.Pages;

public partial class EnergyPage : UserControl
{
    public EnergyPage() => InitializeComponent();

    void Reload_Click(object sender, RoutedEventArgs e)
    {
        HomeyStore.I.EnergyReportRequested = true;
        HomeyStore.I.Kick();
    }
}
