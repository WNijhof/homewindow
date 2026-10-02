using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HomeWindow.Core;

namespace HomeWindow.Views;

// The panel that opens above the tray icon, like the menu bar panel of HomeBar on the Mac
public partial class FlyoutWindow : Window, IDetailsHost
{
    readonly HomeyStore store = HomeyStore.I;
    readonly DeviceBrowser rooms = new();
    DateTime hiddenAt = DateTime.MinValue;
    bool hiding;
    string tab = "favorites";

    public FlyoutWindow()
    {
        InitializeComponent();
        DataContext = store;
        RoomsPanel.DataContext = rooms;
        SourceInitialized += (_, _) => Theme.ApplyToWindow(this, transient: true);
        Theme.Changed += () => { Theme.ApplyToWindow(this, transient: true); ApplyView(); };
        Deactivated += (_, _) => HideFlyout();
        PreviewKeyDown += OnKey;
        store.PropertyChanged += OnStoreChanged;
        store.FavoriteDevices.CollectionChanged += (_, _) => UpdateEmpty();
        store.FavoriteFlows.CollectionChanged += (_, _) => UpdateEmpty();
        store.FlowGroups.CollectionChanged += (_, _) => FilterFlows();
        store.Moods.CollectionChanged += (_, _) => FilterMoods();
        IsVisibleChanged += (_, _) => { if (IsVisible) store.WindowShown(); else store.WindowHidden(); };

        tab = App.Settings.FlyoutTab;
        ApplyView();
        SelectTab(tab);
        UpdateStatus();
        UpdateEmpty();
    }

    void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeyStore.Status) or "" or null) UpdateStatus();
        if (e.PropertyName is "" or null) { FilterFlows(); FilterMoods(); }
    }

    void UpdateStatus()
    {
        StatusDot.Fill = (Brush)FindResource(store.Status switch
        {
            "local" => "GoodBrush",
            "cloud" => "GridBrush",
            "connecting" => "WarnBrush",
            _ => "AlarmBrush",
        });
        OfflinePanel.Visibility = store.IsConnected || (store.Status == "connecting" && store.Devices.Count > 0) ? Visibility.Collapsed : Visibility.Visible;
        OfflineTitle.Text = store.Config == null ? Loc.T("Stel eerst je Homey in") : store.Status == "connecting" ? Loc.T("Verbinden…") : Loc.T("Niet verbonden");
        WeatherPart.Visibility = App.Settings.FlyoutWeather && store.Weather.HasData ? Visibility.Visible : Visibility.Collapsed;
        EnergyPart.Visibility = App.Settings.FlyoutEnergy && store.Energy.HasData ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateEmpty() =>
        NoFavorites.Visibility = store.FavoriteDevices.Count == 0 && store.FavoriteFlows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // List or grid, as chosen in the settings
    public void ApplyView()
    {
        var grid = App.Settings.FlyoutView == "grid";
        Resources["FlyoutDeviceTemplate"] = FindResource(grid ? "DeviceTile" : "DeviceRow");
        Resources["FlyoutDevicePanel"] = FindResource(grid ? "WrapItems" : "StackItems");
        Resources["FlyoutItemsMargin"] = grid ? new Thickness(-4, 0, -4, 0) : new Thickness(0);
        ViewToggle.Content = grid ? Icons.List : Icons.Grid;
    }

    void ViewToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.FlyoutView = App.Settings.FlyoutView == "grid" ? "list" : "grid";
        App.Settings.Save();
        ApplyView();
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string t } || !IsLoaded && t != tab) return;
        tab = t;
        App.Settings.FlyoutTab = t;
        App.Settings.Save();
        ShowTab();
    }

    public void SelectTab(string t)
    {
        var button = t switch { "rooms" => TabRooms, "flows" => TabFlows, "moods" => TabMoods, _ => TabFavorites };
        tab = (string)button.Tag;
        button.IsChecked = true;
        ShowTab();
    }

    void ShowTab()
    {
        FavoritesPanel.Visibility = tab == "favorites" ? Visibility.Visible : Visibility.Collapsed;
        RoomsPanel.Visibility = tab == "rooms" ? Visibility.Visible : Visibility.Collapsed;
        FlowsPanel.Visibility = tab == "flows" ? Visibility.Visible : Visibility.Collapsed;
        MoodsPanel.Visibility = tab == "moods" ? Visibility.Visible : Visibility.Collapsed;
        // Favourites has no search: the list is short and chosen by hand
        SearchBox.IsEnabled = tab != "favorites";
        SearchBox.Opacity = SearchHint.Opacity = tab == "favorites" ? 0.5 : 1;
        Scroller.ScrollToTop();
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        rooms.Search = SearchBox.Text;
        FilterFlows();
        FilterMoods();
    }

    void FilterFlows()
    {
        var q = SearchBox.Text.Trim();
        if (q.Length == 0) { FlowGroupsList.ItemsSource = store.FlowGroups; return; }
        FlowGroupsList.ItemsSource = store.FlowGroups
            .Select(g => new FlowGroup(g.Key, g.Title, g.Items.Where(f => f.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)), true))
            .Where(g => g.Count > 0)
            .ToList();
    }

    void FilterMoods()
    {
        var q = SearchBox.Text.Trim();
        MoodsList.ItemsSource = q.Length == 0 ? store.Moods
            : store.Moods.Where(m => m.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || m.ZoneName.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    public void ShowDetails(DeviceVM device)
    {
        DetailContent.Content = device;
        DetailPanel.Visibility = Visibility.Visible;
    }

    void CloseDetail_Click(object sender, RoutedEventArgs e) => CloseDetail();

    void CloseDetail()
    {
        DetailPanel.Visibility = Visibility.Collapsed;
        DetailContent.Content = null;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DetailPanel.Visibility == Visibility.Visible) CloseDetail();
            else HideFlyout();
            e.Handled = true;
        }
        else if (e.Key == Key.F5) { store.Kick(); e.Handled = true; }
        else if (!SearchBox.IsKeyboardFocused && tab != "favorites" && e.Key is >= Key.A and <= Key.Z && Keyboard.Modifiers == ModifierKeys.None)
        {
            // Typing starts a search right away
            SearchBox.Focus();
        }
    }

    public void ShowFlyout()
    {
        // A click on the tray icon while the flyout is open first deactivates it; don't reopen it then
        if ((DateTime.UtcNow - hiddenAt).TotalMilliseconds < 300) return;
        hiding = false;
        CloseDetail();
        UpdateStatus();

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(cursor);
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        var scale = Native.ScaleAt(cursor.X, cursor.Y);
        var margin = (int)(12 * scale);

        Height = Math.Min(640, work.Height / scale - 24);
        var w = (int)(Width * scale);
        var h = (int)(Height * scale);

        // Next to the taskbar, wherever it is
        int x = work.Right - w - margin, y = work.Bottom - h - margin;
        var fromTop = work.Top > bounds.Top;
        if (fromTop) y = work.Top + margin;
        if (work.Left > bounds.Left) x = work.Left + margin;
        var cursorInWork = work.Contains(cursor);
        if (cursorInWork)
        {
            // Opened with the hotkey: still in the corner by the clock
            y = fromTop ? work.Top + margin : work.Bottom - h - margin;
        }

        Opacity = 0;
        Show();
        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        Activate();
        Native.SetForegroundWindow(hwnd);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromTop ? -16 : 16, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        store.Kick();
    }

    public void HideFlyout()
    {
        if (!IsVisible || hiding) return;
        hiding = true;
        hiddenAt = DateTime.UtcNow;
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (_, _) =>
        {
            if (!hiding) return;
            Hide();
            hiding = false;
            BeginAnimation(OpacityProperty, null);
        };
        BeginAnimation(OpacityProperty, fade);
    }

    void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (store.IsConnected) store.Kick(); else store.Reconnect();
    }

    void Open_Click(object sender, RoutedEventArgs e) => App.Current.ShowMain();
    void Settings_Click(object sender, RoutedEventArgs e) => App.Current.ShowMain("settings");
}
