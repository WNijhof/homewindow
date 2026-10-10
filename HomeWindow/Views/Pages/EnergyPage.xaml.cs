using System.Windows;
using System.Windows.Controls;
using HomeWindow.Core;

namespace HomeWindow.Views.Pages;

public partial class EnergyPage : UserControl
{
    public EnergyPage() => InitializeComponent();

    bool loadingMeters;

    void Meter_Loaded(object sender, RoutedEventArgs e)
    {
        loadingMeters = true;
        var meters = HomeyStore.I.GridMeters();
        MeterChoice.ItemsSource = meters;
        MeterChoice.SelectedItem = meters.FirstOrDefault(d => d.Id == HomeyStore.I.Config?.GridMeterId) ?? meters.FirstOrDefault();
        loadingMeters = false;
    }

    void Meter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loadingMeters || MeterChoice.SelectedItem is not DeviceVM d) return;
        HomeyStore.I.ChooseGridMeter(d.Id);
    }

    void Reload_Click(object sender, RoutedEventArgs e)
    {
        HomeyStore.I.EnergyReportRequested = true;
        HomeyStore.I.Kick();
    }
}
