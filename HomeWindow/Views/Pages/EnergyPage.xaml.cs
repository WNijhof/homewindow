using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    void Solar_Loaded(object sender, RoutedEventArgs e)
    {
        SolarChoice.Children.Clear();
        foreach (var d in HomeyStore.I.SolarPanels())
        {
            var chip = new ToggleButton { Content = d.Name, Tag = d.Id, IsChecked = HomeyStore.I.Config?.ExcludedSolar.Contains(d.Id) != true };
            chip.SetResourceReference(StyleProperty, "ToggleChip");
            chip.Click += (_, _) => HomeyStore.I.CountSolar(d.Id, chip.IsChecked == true);
            SolarChoice.Children.Add(chip);
        }
    }

    void Reload_Click(object sender, RoutedEventArgs e)
    {
        HomeyStore.I.EnergyReportRequested = true;
        HomeyStore.I.Kick();
    }
}
