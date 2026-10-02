using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HomeWindow.Core;

namespace HomeWindow.Views;

public partial class MainWindow : Window, IDetailsHost
{
    readonly HomeyStore store = HomeyStore.I;
    string page = "overview";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = store;
        SourceInitialized += (_, _) => Theme.ApplyToWindow(this, transient: false);
        Theme.Changed += () => { Theme.ApplyToWindow(this, transient: false); ApplyView(); };
        store.PropertyChanged += OnStoreChanged;
        App.Settings.Saved += RefreshHomeys;
        IsVisibleChanged += (_, _) => { if (IsVisible) store.WindowShown(); else store.WindowHidden(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && DetailPanel.Visibility == Visibility.Visible) { CloseDetail(); e.Handled = true; }
            if (e.Key == Key.F5) { store.Kick(); e.Handled = true; }
        };
        RefreshHomeys();
        ApplyView();
        UpdateStatus();
    }

    // Closing hides the window; HomeWindow keeps running in the tray
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        CloseDetail();
        Hide();
    }

    void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeyStore.Status) or "" or null) UpdateStatus();
        if (e.PropertyName is nameof(HomeyStore.HomeyName)) Title = $"HomeWindow – {store.HomeyName}";
    }

    void UpdateStatus() =>
        StatusDot.Fill = (Brush)FindResource(store.Status switch
        {
            "local" => "GoodBrush",
            "cloud" => "GridBrush",
            "connecting" => "WarnBrush",
            _ => "AlarmBrush",
        });

    void RefreshHomeys()
    {
        var homeys = App.Settings.Homeys;
        HomeyList.ItemsSource = null;
        HomeyList.ItemsSource = homeys.Count > 1 ? homeys : null;
        HomeyList.Visibility = homeys.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var item in homeys)
                if (HomeyList.ItemContainerGenerator.ContainerFromItem(item) is ContentPresenter cp
                    && VisualTreeHelper.GetChildrenCount(cp) > 0 && VisualTreeHelper.GetChild(cp, 0) is RadioButton rb)
                    rb.IsChecked = item.Id == store.Config?.Id;
        });
    }

    void Homey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: HomeyConfig homey } && homey.Id != store.Config?.Id)
        {
            CloseDetail();
            App.SwitchHomey(homey);
        }
    }

    void ApplyView()
    {
        var grid = App.Settings.MainView == "grid";
        Resources["MainDeviceTemplate"] = FindResource(grid ? "DeviceTile" : "DeviceRow");
        Resources["MainDevicePanel"] = FindResource(grid ? "WrapItems" : "StackItems");
        Resources["MainItemsMargin"] = grid ? new Thickness(-4, 0, -4, 0) : new Thickness(0);
        Resources["MainRowWidth"] = grid ? double.PositiveInfinity : 680.0;
    }

    public void ShowPage(string? name)
    {
        Navigate(name);
        Show();
        // Windows may pass on a minimized start-up state from the process that launched HomeWindow
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void Navigate(string? name)
    {
        if (name != null) page = name;
        foreach (var child in Nav.Children)
            if (child is RadioButton { Tag: string tag } rb && tag == page) rb.IsChecked = true;
        SelectPage();
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            page = tag;
            SelectPage();
        }
    }

    void SelectPage()
    {
        var pages = new Dictionary<string, FrameworkElement>
        {
            ["overview"] = OverviewPage, ["devices"] = DevicesPage, ["flows"] = FlowsPage, ["moods"] = MoodsPage,
            ["variables"] = VariablesPage, ["energy"] = EnergyPage, ["batteries"] = BatteriesPage,
            ["notifications"] = NotificationsPage, ["system"] = SystemPage, ["settings"] = SettingsPage,
        };
        foreach (var (key, element) in pages) element.Visibility = key == page ? Visibility.Visible : Visibility.Collapsed;
        store.EnergyPageVisible = page == "energy";
        store.SystemPageVisible = page == "system";
        if (page is "energy" or "system") store.Kick();
        if (page != "devices" && page != "overview" && page != "batteries") CloseDetail();
    }

    public void ShowDetails(DeviceVM device)
    {
        DetailContent.Content = device;
        DetailPanel.Visibility = Visibility.Visible;
    }

    void CloseDetail_Click(object sender, RoutedEventArgs e) => CloseDetail();

    public void CloseDetail()
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailContent.Content = null;
    }
}
