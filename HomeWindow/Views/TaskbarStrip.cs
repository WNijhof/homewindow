using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HomeWindow.Core;

namespace HomeWindow.Views;

// Live energy on the taskbar, just left of the notification area, or at the far left when the taskbar
// icons are centred. Windows 11 has no API for this, so the strip is a child window of the taskbar,
// the way tools such as TrafficMonitor do it.
public sealed class TaskbarStrip : IDisposable
{
    const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
    // ASYNCWINDOWPOS: the taskbar belongs to Explorer; when it hangs, HomeWindow must not hang with it
    const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_ASYNCWINDOWPOS = 0x4000;
    const int Gap = 8;

    readonly EnergyVM energy = HomeyStore.I.Energy;
    readonly DispatcherTimer timer;
    readonly Border root;
    readonly TextBlock home, solar, grid, battery, solarIcon, gridIcon, batteryIcon;
    readonly StackPanel solarPart, gridPart, batteryPart;
    HwndSource? source;
    IntPtr taskbar, failedOn;
    (int x, int width, int height, string text) last;
    int ticks;
    bool light;
    // Free room at the left of the taskbar, in pixels from its left edge: from the end of the buttons there
    // (Widgets) to the Start button. Looked up in the background now and then; null when there is none.
    (int from, int to)? leftRoom;
    IntPtr roomBar;
    DateTime roomChecked;
    bool checkingRoom;

    public event Action? Clicked;

    public TaskbarStrip()
    {
        TextBlock Glyph(string text) => new()
        {
            Text = text, FontFamily = (FontFamily)Application.Current.FindResource("IconFont"), FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0),
        };
        TextBlock Value() => new() { FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        StackPanel Part(TextBlock icon, TextBlock value, double left) => new()
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(left, 0, 0, 0), Children = { icon, value },
        };

        home = Value(); solar = Value(); grid = Value(); battery = Value();
        var homeIcon = Glyph(Icons.Home);
        solarIcon = Glyph(Icons.Sun);
        gridIcon = Glyph(Icons.Energy);
        batteryIcon = Glyph(Icons.Battery);
        solarPart = Part(solarIcon, solar, 12);
        gridPart = Part(gridIcon, grid, 12);
        batteryPart = Part(batteryIcon, battery, 12);
        root = new Border
        {
            // Nearly transparent, so the whole strip takes clicks
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Padding = new Thickness(8, 0, 8, 0),
            Cursor = Cursors.Hand,
            Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { Part(homeIcon, home, 0), solarPart, gridPart, batteryPart } },
        };
        root.MouseLeftButtonUp += (_, _) => Clicked?.Invoke();
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);

        energy.PropertyChanged += OnEnergyChanged;
        HomeyStore.I.PropertyChanged += OnStoreChanged;
        timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Place(), Dispatcher.CurrentDispatcher);
        timer.Start();
        Place();
    }

    // Only with live numbers: while offline the last ones would look current
    static bool Wanted => App.Settings.TaskbarEnergy && HomeyStore.I.IsConnected && HomeyStore.I.Energy.HasData;

    void OnEnergyChanged(object? sender, PropertyChangedEventArgs e) => Place();

    void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeyStore.IsConnected) or nameof(HomeyStore.Status)) Place();
    }

    // Creates, updates, moves or removes the strip; also recovers when Explorer restarts
    public void Place()
    {
        var bar = FindWindow("Shell_TrayWnd", null);
        if (!Wanted || bar == IntPtr.Zero || !GetWindowRect(bar, out var tb) || tb.Right - tb.Left < tb.Bottom - tb.Top)
        {
            Close();
            return;
        }
        if (bar == failedOn) return;
        if (bar != taskbar || source == null || !IsWindow(source.Handle)) Create(bar);
        if (source == null) return;

        Fill();
        var scale = Native.ScaleAt(tb.Left + 1, tb.Top + 1);
        root.Width = root.Height = double.NaN;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = (int)Math.Ceiling(root.DesiredSize.Width * scale);
        var height = tb.Bottom - tb.Top;

        // Left of the notification area; without one, keep clear of the clock
        var notify = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
        var right = notify != IntPtr.Zero && GetWindowRect(notify, out var nr) && nr.Left > tb.Left && nr.Left < tb.Right
            ? nr.Left - tb.Left
            : tb.Right - tb.Left - (int)(260 * scale);
        var gap = (int)(Gap * scale);
        var x = Math.Max(0, right - width - gap);
        // At the far left when chosen and when it fits before the Start button; otherwise on the right after all
        if (App.Settings.TaskbarPosition == "left" && LeftRoom(bar, tb) is { } room && room.to - room.from >= width + 2 * gap)
            x = room.from + gap;
        // The root does not stretch to the child window by itself; size it so the text sits in the middle
        root.Width = width / scale;
        root.Height = height / scale;
        // Explorer can put its own content on top again; every tenth tick (20 s) the strip moves back to the top anyway
        var now = (x, width, height, energy.HomeText + energy.SolarText + energy.GridText + energy.BatteryPercentText);
        if (now == last && ++ticks % 10 != 0) return;
        last = now;
        SetWindowPos(source.Handle, IntPtr.Zero, x, 0, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_ASYNCWINDOWPOS);
    }

    // The last known free room at the left; starts a new lookup when it is old or the taskbar is new.
    // UI Automation asks Explorer, which can be slow or hang, so it runs off the UI thread.
    (int from, int to)? LeftRoom(IntPtr bar, RECT tb)
    {
        if (!checkingRoom && (bar != roomBar || DateTime.UtcNow - roomChecked > TimeSpan.FromSeconds(30)))
        {
            checkingRoom = true;
            roomBar = bar;
            roomChecked = DateTime.UtcNow;
            var left = tb.Left;
            Task.Run(() => FindLeftRoom(bar, left)).ContinueWith(t =>
            {
                checkingRoom = false;
                var found = t.IsCompletedSuccessfully ? t.Result : null;
                if (found != leftRoom)
                {
                    leftRoom = found;
                    last = default;
                    Place();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        return leftRoom;
    }

    // From the right edge of the buttons left of Start (Widgets, if shown) to the Start button.
    // None when Start sits at the left itself (taskbar alignment: Left).
    static (int from, int to)? FindLeftRoom(IntPtr bar, int taskbarLeft)
    {
        try
        {
            var taskbar = System.Windows.Automation.AutomationElement.FromHandle(bar);
            var start = taskbar.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, "StartButton"));
            if (start == null) return null;
            var startLeft = (int)start.Current.BoundingRectangle.Left - taskbarLeft;
            if (startLeft < 200) return null;
            var buttons = taskbar.FindAll(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Button));
            var from = 0;
            foreach (System.Windows.Automation.AutomationElement b in buttons)
            {
                var r = b.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0) continue;
                var bRight = (int)r.Right - taskbarLeft;
                if (bRight <= startLeft && bRight > from) from = bRight;
            }
            return (from, startLeft);
        }
        catch (Exception e) when (e is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    void Create(IntPtr bar)
    {
        Close();
        taskbar = bar;
        last = default;
        try
        {
            var p = new HwndSourceParameters("HomeWindowEnergy")
            {
                ParentWindow = bar,
                WindowStyle = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                UsesPerPixelTransparency = true,
                Width = 1, Height = 1,
            };
            source = new HwndSource(p) { RootVisual = root, SizeToContent = SizeToContent.Manual };
        }
        catch (Exception e)
        {
            // Not again on this taskbar; a new one (Explorer restarted) gets a new try
            Log.Error("Taskbar strip", e);
            source = null;
            failedOn = bar;
        }
    }

    void Fill()
    {
        light = Theme.TaskbarIsLight();
        var text = light ? Brushes.Black : Brushes.White;
        var good = new SolidColorBrush(light ? Color.FromRgb(0x10, 0x7C, 0x41) : Color.FromRgb(0x5E, 0xD3, 0x8A));
        // The strip lives outside the app's windows, so it follows the taskbar's colours itself
        System.Windows.Documents.TextElement.SetForeground(root, text);
        solarIcon.Foreground = new SolidColorBrush(light ? Color.FromRgb(0xE0, 0x9B, 0x00) : Color.FromRgb(0xFF, 0xC8, 0x3D));

        home.Text = energy.HomeText;
        solar.Text = energy.SolarText;
        grid.Text = energy.GridText;
        grid.Foreground = energy.IsExporting ? good : text;
        battery.Text = energy.BatteryPercentText;
        // Green while charging, so the direction shows without the watts
        batteryIcon.Foreground = energy.BatteryW > 50 ? good : text;
        solarPart.Visibility = energy.HasSolar ? Visibility.Visible : Visibility.Collapsed;
        gridPart.Visibility = energy.HasGrid ? Visibility.Visible : Visibility.Collapsed;
        batteryPart.Visibility = energy.BatteryPercent != null ? Visibility.Visible : Visibility.Collapsed;
        root.ToolTip = string.Join("\n", new[]
        {
            $"{Loc.T("Huis")}: {energy.HomeText}",
            energy.HasSolar ? $"{Loc.T("Zon")}: {energy.SolarText}" : null,
            energy.HasGrid ? $"{energy.GridLabel}: {energy.GridText}" : null,
            energy.HasBattery ? $"{energy.BatteryLabel}: {energy.BatteryText}" + (energy.BatteryPercent != null ? $" · {energy.BatteryPercentText}" : "") : null,
        }.Where(s => s != null));
    }

    void Close()
    {
        if (source == null) return;
        source.RootVisual = null;
        source.Dispose();
        source = null;
    }

    public void Dispose()
    {
        timer.Stop();
        energy.PropertyChanged -= OnEnergyChanged;
        HomeyStore.I.PropertyChanged -= OnStoreChanged;
        Close();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindow(string cls, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? title);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
