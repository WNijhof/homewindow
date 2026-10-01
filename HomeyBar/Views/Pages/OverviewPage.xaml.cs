using System.Windows.Controls;
using System.Windows.Threading;
using HomeyBar.Core;

namespace HomeyBar.Views.Pages;

public partial class OverviewPage : UserControl
{
    public OverviewPage()
    {
        InitializeComponent();
        var store = HomeyStore.I;
        store.Notifications.CollectionChanged += (_, _) => Recent.ItemsSource = store.Notifications.Take(5).ToList();
        var clock = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        clock.Tick += (_, _) => UpdateGreeting();
        clock.Start();
        UpdateGreeting();
    }

    void UpdateGreeting()
    {
        var hour = DateTime.Now.Hour;
        Greeting.Text = Loc.T(hour < 6 ? "Goedenacht" : hour < 12 ? "Goedemorgen" : hour < 18 ? "Goedemiddag" : "Goedenavond");
        DateText.Text = DateTime.Now.ToString("dddd d MMMM", Loc.Culture);
    }
}
