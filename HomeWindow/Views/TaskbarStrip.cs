using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HomeWindow.Core;
using UIA = System.Windows.Automation;

namespace HomeWindow.Views;

// Live energy on the taskbar, just left of the notification area, or at the far left when the taskbar
// icons are centred. Windows 11 has no API for this, so the strip is a child window of the taskbar,
// the way tools such as TrafficMonitor do it.
public sealed class TaskbarStrip : IDisposable
{
    const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000, WS_POPUP = unchecked((int)0x80000000);
    const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    // ASYNCWINDOWPOS: the taskbar belongs to Explorer; when it hangs, HomeWindow must not hang with it
    const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_ASYNCWINDOWPOS = 0x4000;
    const int Gap = 8;

    readonly EnergyVM energy = HomeyStore.I.Energy;
    readonly WeatherVM weatherData = HomeyStore.I.Weather;
    readonly DispatcherTimer timer;
    readonly Border root;
    readonly TextBlock weather, home, solar, grid, battery, charger, weatherIcon, solarIcon, gridIcon, batteryIcon, chargerIcon, topIcon;
    readonly StackPanel weatherPart, homePart, solarPart, gridPart, batteryPart, chargerPart, topPart;
    // The biggest consumers: per device the name above and the power below, side by side
    readonly StackPanel topItems = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    string topKey = "";
    // How many of the biggest consumers fit; fewer at the left, so the widget stays where it was chosen
    int topMax = 3;
    HwndSource? source;
    IntPtr taskbar, failedOn;
    // Windows or a security program can refuse the window at first (access denied, seen right after a restart);
    // so it is tried again a few times, further and further apart, before giving up on this taskbar
    int failures;
    DateTime retryAt;
    static readonly int[] RetrySeconds = [3, 6, 12, 30, 60, 120];
    (int x, int y, int width, int height, string text) last;
    // Some PCs (a security program, or Windows itself) refuse a window inside the taskbar unless HomeWindow runs as
    // administrator. The strip is then a separate window on top of the taskbar instead.
    bool overlay;
    int ticks;
    string lastInput = "";
    bool light;
    // Free room on the taskbar, in pixels from its left edge. Left: from the end of the buttons there (Widgets) to the
    // Start button, null when there is none. ButtonsEnd: where the last taskbar button ends, so the strip next to the
    // notification area does not cover the icons of open programs; null when unknown.
    // Looked up in the background now and then, because the icons move as programs open and close.
    readonly record struct Rooms((int from, int to)? Left, int? ButtonsEnd);
    Rooms? rooms;
    IntPtr roomBar;
    DateTime roomChecked;
    bool checkingRoom;
    // What the last placement and lookup found, for the diagnostics report
    public static string Status { get; private set; } = "not placed";
    static string lookup = "no lookup yet";

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

        weather = Value(); home = Value(); solar = Value(); grid = Value(); battery = Value(); charger = Value();
        weatherIcon = Glyph(Icons.Sun);
        var homeIcon = Glyph(Icons.Home);
        solarIcon = Glyph(Icons.Sun);
        gridIcon = Glyph(Icons.Energy);
        batteryIcon = Glyph(Icons.Battery);
        chargerIcon = Glyph(Icons.Car);
        topIcon = Glyph(Icons.Power);
        weatherPart = Part(weatherIcon, weather, 12);
        homePart = Part(homeIcon, home, 12);
        solarPart = Part(solarIcon, solar, 12);
        gridPart = Part(gridIcon, grid, 12);
        batteryPart = Part(batteryIcon, battery, 12);
        chargerPart = Part(chargerIcon, charger, 12);
        topPart = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), Children = { topIcon, topItems } };
        root = new Border
        {
            // Nearly transparent, so the whole strip takes clicks
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Padding = new Thickness(8, 0, 8, 0),
            Cursor = Cursors.Hand,
            Child = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { weatherPart, homePart, solarPart, gridPart, batteryPart, chargerPart, topPart } },
        };
        root.MouseLeftButtonUp += (_, _) => Clicked?.Invoke();
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);

        energy.PropertyChanged += OnEnergyChanged;
        weatherData.PropertyChanged += OnEnergyChanged;
        HomeyStore.I.PropertyChanged += OnStoreChanged;
        timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Place(), Dispatcher.CurrentDispatcher);
        timer.Start();
        Place();
    }

    // The parts the user picked that have something to show, in the order of the strip
    static IReadOnlyList<string> Shown()
    {
        var items = App.Settings.TaskbarItems;
        var e = HomeyStore.I.Energy;
        var list = new List<string>();
        if (items.Contains("weather") && HomeyStore.I.Weather.HasData) list.Add("weather");
        if (items.Contains("home") && e.HomeW != null) list.Add("home");
        if (items.Contains("solar") && e.HasSolar) list.Add("solar");
        if (items.Contains("grid") && e.HasGrid) list.Add("grid");
        if (items.Contains("battery") && e.BatteryPercent != null) list.Add("battery");
        // The car only while it charges; an idle charger would just say 0 W
        if (items.Contains("charger") && e.IsCharging) list.Add("charger");
        // The biggest consumers right now, so a charger or a dryer shows up by itself while it draws power
        if (items.Contains("top") && e.Consumers.Count > 0) list.Add("top");
        return list;
    }

    // Only with live numbers: while offline the last ones would look current
    static bool Wanted => App.Settings.TaskbarEnergy && HomeyStore.I.IsConnected && Shown().Count > 0;

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
        if (bar == failedOn)
        {
            if (failures > RetrySeconds.Length || DateTime.UtcNow < retryAt) return;
        }
        if (bar != taskbar || source == null || !IsWindow(source.Handle)) Create(bar);
        if (source == null) return;

        // As a window of its own it must give way to full-screen programs and to a hidden taskbar
        if (overlay && (!IsWindowVisible(bar) || FullScreenProgram()))
        {
            ShowWindow(source.Handle, 0);
            last = default;
            return;
        }

        var scale = Native.ScaleAt(tb.Left + 1, tb.Top + 1);
        var gap = (int)(Gap * scale);
        var height = tb.Bottom - tb.Top;

        // Left of the notification area; without one, keep clear of the clock
        var notify = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
        var right = notify != IntPtr.Zero && GetWindowRect(notify, out var nr) && nr.Left > tb.Left && nr.Left < tb.Right
            ? nr.Left - tb.Left
            : tb.Right - tb.Left - (int)(260 * scale);
        // Until the first lookup of the free room is back the strip waits, rather than showing up on top of the icons
        // and jumping away a moment later
        if (FreeRoom(bar, tb, right) is not { } room)
        {
            Hide();
            return;
        }
        // Nothing changed since the last time: do not measure and refill, that redraws the strip and makes it blink.
        // Now and then it is put on top again anyway (Explorer can cover it).
        var input = string.Join("|", room, right, tb.Left, tb.Top, height, scale, Theme.TaskbarIsLight(), weatherData.Temperature, energy.HomeText, energy.SolarText,
            energy.GridText, energy.BatteryPercentText, energy.ChargerText, energy.CarPercentText, string.Join(',', Shown()),
            string.Join(",", energy.Consumers.Select(c => c.Name + c.ValueText)), App.Settings.TaskbarTextSize, App.Settings.TaskbarPosition);
        if (last != default && input == lastInput && ++ticks % (overlay ? 15 : 10) != 0) return;
        lastInput = input;
        int x, width;
        // At the far left when chosen and when it fits before the Start button, with fewer biggest consumers if need be;
        // otherwise on the right, also with fewer when the icons of open programs reach far to the right
        if (App.Settings.TaskbarPosition == "left" && room.Left is { } left && Fit(left.to - left.from - 2 * gap, scale) is { } leftWidth)
        {
            x = left.from + gap;
            width = leftWidth;
        }
        else
        {
            var from = room.ButtonsEnd is { } end && end < right ? end : 0;
            if (Fit(right - from - 2 * gap, scale) is not { } rightWidth)
            {
                // Not even without the biggest consumers: rather no strip than one on top of the taskbar icons
                Hide();
                return;
            }
            width = rightWidth;
            x = Math.Max(0, right - width - gap);
        }
        // The root does not stretch to the child window by itself; size it so the text sits in the middle
        root.Width = width / scale;
        root.Height = height / scale;
        // Explorer can put its own content on top again; every tenth tick (20 s) the strip moves back to the top anyway
        // As an overlay the position is on the screen
        var (px, py) = overlay ? (tb.Left + x, tb.Top) : (x, 0);
        Status = $"taskbar {tb.Left},{tb.Top} {tb.Right - tb.Left}x{height}, scale {scale}, notification area at {right}, {lookup}, "
            + $"room left {room.Left?.ToString() ?? "-"}, buttons end {room.ButtonsEnd?.ToString() ?? "-"}, strip at {x} width {width}{(overlay ? " (overlay)" : "")}";
        var now = (px, py, width, height, "");
        last = now;
        SetWindowPos(source.Handle, overlay ? HWND_TOPMOST : IntPtr.Zero, px, py, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_ASYNCWINDOWPOS);
    }

    void Hide()
    {
        if (source != null && last != default) ShowWindow(source.Handle, 0);
        last = default;
    }

    // The width in pixels with as many of the biggest consumers as fit in the room (3, 2, 1, none), and the strip
    // filled with those; null when it does not fit at all
    int? Fit(int room, double scale)
    {
        topMax = 3;
        var width = Measure(scale);
        while (width > room && topMax > 0 && Shown().Contains("top"))
        {
            topMax--;
            width = Measure(scale);
        }
        return width <= room ? width : null;
    }

    // The width in pixels with what is in it now
    int Measure(double scale)
    {
        Fill();
        root.Width = root.Height = double.NaN;
        topItems.InvalidateMeasure();
        topPart.InvalidateMeasure();
        root.InvalidateMeasure();
        root.UpdateLayout();
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return (int)Math.Ceiling(root.DesiredSize.Width * scale);
    }

    // The last known free room; starts a new lookup when it is old or the taskbar is new. Null until the first lookup
    // on this taskbar is back. UI Automation asks Explorer, which can be slow or hang, so it runs off the UI thread.
    Rooms? FreeRoom(IntPtr bar, RECT tb, int notifyLeft)
    {
        if (bar != roomBar) rooms = null;
        if (!checkingRoom && (bar != roomBar || DateTime.UtcNow - roomChecked > TimeSpan.FromSeconds(5)))
        {
            checkingRoom = true;
            roomBar = bar;
            roomChecked = DateTime.UtcNow;
            var left = tb.Left;
            Task.Run(() => FindRooms(bar, left, notifyLeft, tb.Right - tb.Left)).ContinueWith(t =>
            {
                checkingRoom = false;
                if (bar != roomBar) return;
                // A failed lookup counts as done too: the strip then goes where it always went
                if (!t.IsCompletedSuccessfully) lookup = $"lookup failed: {t.Exception?.InnerException?.GetType().Name}";
                var found = t.IsCompletedSuccessfully ? t.Result : new Rooms(null, null);
                if (found != rooms)
                {
                    rooms = found;
                    last = default;
                    Place();
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        return rooms;
    }

    // Left: from the right edge of the buttons left of Start (Widgets, if shown) to the Start button; none when Start
    // sits at the left itself (taskbar alignment: Left). ButtonsEnd: the right edge of the last button before the
    // notification area, which with many programs open reaches far to the right.
    static Rooms FindRooms(IntPtr bar, int taskbarLeft, int notifyLeft, int taskbarWidth)
    {
        try
        {
            var taskbar = UIA.AutomationElement.FromHandle(bar);
            // Buttons, but also list items and split buttons: not every Windows build shows the program buttons as buttons
            var kinds = new UIA.OrCondition(
                new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Button),
                new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.ListItem),
                new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.SplitButton),
                new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.MenuItem));
            var found = new Found(taskbarLeft, notifyLeft, taskbarWidth);
            foreach (UIA.AutomationElement e in taskbar.FindAll(UIA.TreeScope.Descendants, kinds)) found.Add(e);
            // Neither Start nor a single program button: the control view hides them, so walk the whole tree once
            var raw = found.StartLeft == null && found.Apps == 0;
            if (raw)
            {
                found = new Found(taskbarLeft, notifyLeft, taskbarWidth);
                Walk(UIA.TreeWalker.RawViewWalker, taskbar, found);
            }
            lookup = $"{found.Count} buttons ({found.Apps} programs{(raw ? ", full tree" : "")}), start at {found.StartLeft?.ToString() ?? "-"}"
                + (found.Apps == 0 ? $", kinds {string.Join(" ", found.Kinds.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key}:{k.Value}"))}, windows {Windows(bar, taskbarLeft)}" : "");
            return new Rooms(found.StartLeft >= 200 ? (found.From, found.StartLeft.Value) : null, found.End > 0 ? found.End : null);
        }
        catch (Exception e) when (e is UIA.ElementNotAvailableException or InvalidOperationException or COMException)
        {
            lookup = $"lookup failed: {e.GetType().Name}";
            return new Rooms(null, null);
        }
    }

    // The windows inside the taskbar, for the diagnosis: a taskbar tool (a Windows 10 taskbar, centred icons with
    // Start at the left) shows up here with class names of its own
    static string Windows(IntPtr bar, int taskbarLeft)
    {
        var list = new List<string>();
        var name = new System.Text.StringBuilder(64);
        EnumChildWindows(bar, (hwnd, _) =>
        {
            if (list.Count >= 16) return false;
            if (IsWindowVisible(hwnd) && GetWindowRect(hwnd, out var r) && r.Right > r.Left && GetClassName(hwnd, name, name.Capacity) > 0)
                list.Add($"{name}:{r.Left - taskbarLeft}-{r.Right - taskbarLeft}");
            return true;
        }, IntPtr.Zero);
        return list.Count > 0 ? string.Join(" ", list) : "-";
    }

    static void Walk(UIA.TreeWalker walker, UIA.AutomationElement parent, Found found, int depth = 0)
    {
        // Explorer's tree is small; the limits only guard against a runaway one
        if (depth > 12) return;
        for (var e = walker.GetFirstChild(parent); e != null && found.Seen < 3000; e = walker.GetNextSibling(e))
        {
            found.Seen++;
            found.Add(e);
            Walk(walker, e, found, depth + 1);
        }
    }

    // What a lookup collects: Start, the right edge of what lies before it and before the notification area
    sealed class Found(int taskbarLeft, int notifyLeft, int taskbarWidth)
    {
        public int? StartLeft;
        public int Count, Apps, Seen;
        public readonly Dictionary<string, int> Kinds = [];
        readonly List<int> rights = [];

        public int From => rights.Where(x => x <= StartLeft).DefaultIfEmpty(0).Max();
        public int End => rights.Where(x => x <= notifyLeft).DefaultIfEmpty(0).Max();

        public void Add(UIA.AutomationElement e)
        {
            var c = e.Current;
            var r = c.BoundingRectangle;
            if (r.IsEmpty || r.Width <= 0) return;
            var id = c.AutomationId ?? "";
            var cls = c.ClassName ?? "";
            var app = cls.Contains("TaskListButton") || id.StartsWith("Appid:", StringComparison.Ordinal);
            var kind = c.ControlType.ProgrammaticName.Replace("ControlType.", "");
            if (id == "StartButton") StartLeft = (int)r.Left - taskbarLeft;
            // Only things the size of a button: a pane around all of them would claim the whole taskbar
            else if (!app && (c.ControlType != UIA.ControlType.Button && c.ControlType != UIA.ControlType.ListItem
                && c.ControlType != UIA.ControlType.SplitButton && c.ControlType != UIA.ControlType.MenuItem || r.Width > taskbarWidth / 3)) return;
            Count++;
            if (app) Apps++;
            var key = kind + (cls.Length > 0 ? "/" + cls : "");
            Kinds[key] = Kinds.GetValueOrDefault(key) + 1;
            rights.Add((int)r.Right - taskbarLeft);
        }
    }

    void Create(IntPtr bar)
    {
        Close();
        taskbar = bar;
        last = default;
        overlay = false;
        try
        {
            HwndSource Make(bool asOverlay) => new(new HwndSourceParameters("HomeWindowEnergy")
            {
                ParentWindow = asOverlay ? IntPtr.Zero : bar,
                WindowStyle = asOverlay ? WS_POPUP | WS_VISIBLE : WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
                ExtendedWindowStyle = asOverlay ? WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST : 0,
                UsesPerPixelTransparency = true,
                Width = 1, Height = 1,
            }) { RootVisual = root, SizeToContent = SizeToContent.Manual };

            try { source = Make(false); }
            catch (System.ComponentModel.Win32Exception e) when (e.NativeErrorCode == 5)
            {
                Log.Error("Taskbar strip: no window in the taskbar, using a window on top of it", e);
                overlay = true;
                source = Make(true);
            }
            failedOn = IntPtr.Zero;
            failures = 0;
        }
        catch (Exception e)
        {
            // Logged on the first and the last try only; a new taskbar (Explorer restarted) starts over
            if (failedOn != bar) failures = 0;
            if (failures == 0 || failures == RetrySeconds.Length) Log.Error($"Taskbar strip (try {failures + 1} of {RetrySeconds.Length + 1})", e);
            source = null;
            failedOn = bar;
            retryAt = DateTime.UtcNow.AddSeconds(failures < RetrySeconds.Length ? RetrySeconds[failures] : 0);
            failures++;
        }
    }

    // Two lines per consumer, so the names stay readable: the name small above, the power below
    void FillTop(Brush text)
    {
        var large = App.Settings.TaskbarTextSize == "large";
        var shown = energy.Consumers.Take(topMax).ToList();
        var key = string.Join("|", shown.Select(c => c.Name + c.ValueText)) + large + text + topMax;
        if (key == topKey) return;
        topKey = key;
        topItems.Children.Clear();
        foreach (var c in shown)
        {
            // A fixed width, so the widget does not grow and shrink with the names and the power
            topItems.Children.Add(new StackPanel
            {
                Width = large ? 96 : 76,
                Margin = new Thickness(topItems.Children.Count == 0 ? 0 : 10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = c.Name, FontSize = large ? 11.5 : 9.5, Opacity = 0.75, MaxWidth = large ? 96 : 76, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = large ? 13 : 11, LineStackingStrategy = LineStackingStrategy.BlockLineHeight },
                    new TextBlock { Text = c.ValueText.TrimEnd(' ', '*'), FontSize = large ? 14 : 12, FontWeight = FontWeights.SemiBold, LineHeight = large ? 16 : 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight },
                },
            });
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

        weather.Text = weatherData.Temperature;
        weatherIcon.Text = weatherData.Glyph;
        home.Text = energy.HomeText;
        solar.Text = energy.SolarText;
        grid.Text = energy.GridText;
        grid.Foreground = energy.IsExporting ? good : text;
        battery.Text = energy.BatteryPercentText;
        // Green while charging, so the direction shows without the watts
        batteryIcon.Foreground = energy.BatteryW > 50 ? good : text;
        // The power going into the car, and its charge when the charger knows it
        charger.Text = energy.ChargerText + (energy.CarPercent != null ? " · " + energy.CarPercentText : "");
        chargerIcon.Foreground = good;
        FillTop(text);

        // Larger text when chosen, for readers who find the taskbar's own size small
        var large = App.Settings.TaskbarTextSize == "large";
        foreach (var part in new[] { weatherPart, homePart, solarPart, gridPart, batteryPart, chargerPart })
        {
            ((TextBlock)part.Children[0]).FontSize = large ? 16 : 13;
            ((TextBlock)part.Children[1]).FontSize = large ? 15.5 : 12.5;
        }
        topIcon.FontSize = large ? 16 : 13;

        // Only the parts picked in the settings; the first one needs no space before it
        var shown = Shown();
        var first = true;
        foreach (var (id, part) in new[] { ("weather", weatherPart), ("home", homePart), ("solar", solarPart), ("grid", gridPart), ("battery", batteryPart), ("charger", chargerPart), ("top", topPart) })
        {
            var visible = shown.Contains(id) && (id != "top" || topItems.Children.Count > 0);
            part.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            part.Margin = new Thickness(visible && !first ? 12 : 0, 0, 0, 0);
            first &= !visible;
        }
        var lines = string.Join("\n", new[]
        {
            weatherData.HasData ? $"{weatherData.Description} {weatherData.Temperature}".Trim() : null,
            $"{Loc.T("Huis")}: {energy.HomeText}",
            energy.HasSolar ? $"{Loc.T("Zon")}: {energy.SolarText}" : null,
            energy.HasGrid ? $"{energy.GridLabel}: {energy.GridText}" : null,
            energy.HasBattery ? $"{energy.BatteryLabel}: {energy.BatteryText}" + (energy.BatteryPercent != null ? $" · {energy.BatteryPercentText}" : "") : null,
            energy.HasCharger ? $"{energy.ChargerLabel}: {energy.ChargerText}" + (energy.CarPercent != null ? $" · {energy.CarPercentText}" : "") : null,
        }.Where(s => s != null));
        root.ToolTip = Graph() is { } graph
            ? new StackPanel { Children = { new TextBlock { Text = lines }, graph } }
            : lines;
    }

    // The house use over the chosen period as a small line graph, as in Homey Insights; null when it is switched off
    // or there is not enough yet to draw
    static FrameworkElement? Graph()
    {
        if (!int.TryParse(App.Settings.TaskbarGraph, out var minutes)) return null;
        var now = DateTime.UtcNow;
        var from = now.AddMinutes(-minutes);
        var points = HomeyStore.I.Energy.HomeHistory.Where(p => p.at >= from).ToList();
        if (points.Count < 2) return null;
        const double W = 280, H = 90;
        var max = Math.Max(100, points.Max(p => p.watts)) * 1.1;
        double X(DateTime at) => (at - from).TotalSeconds / (now - from).TotalSeconds * W;
        double Y(double watts) => H - watts / max * H;
        var line = new PointCollection(points.Select(p => new Point(X(p.at), Y(p.watts))));
        var area = new PointCollection(line) { new Point(line[^1].X, H), new Point(line[0].X, H) };
        var yellow = Color.FromRgb(0xE0, 0xB4, 0x00);
        var canvas = new Canvas { Width = W, Height = H, ClipToBounds = true };
        canvas.Children.Add(new System.Windows.Shapes.Polygon { Points = area, Fill = new SolidColorBrush(Color.FromArgb(0x40, yellow.R, yellow.G, yellow.B)) });
        canvas.Children.Add(new System.Windows.Shapes.Polyline { Points = line, Stroke = new SolidColorBrush(yellow), StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round });
        canvas.Children.Add(new System.Windows.Shapes.Line { X1 = 0, X2 = W, Y1 = H - 0.5, Y2 = H - 0.5, Stroke = new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)), StrokeThickness = 1 });

        TextBlock Small(string text) => new() { Text = text, FontSize = 10.5, Opacity = 0.7 };
        DockPanel Row(string left, string right, Thickness margin)
        {
            var end = Small(right);
            DockPanel.SetDock(end, Dock.Right);
            return new DockPanel { Width = W, Margin = margin, Children = { end, Small(left) } };
        }
        var period = minutes >= 60 ? Loc.T($"{minutes / 60} uur") : $"{minutes} min";
        // Above: the period and the highest use in it; below: from when to now
        return new StackPanel
        {
            Children =
            {
                Row(Loc.F("Huis, laatste {0}", period), EnergyVM.Watts(points.Max(p => p.watts)), new Thickness(0, 10, 0, 2)),
                canvas,
                Row(from.ToLocalTime().ToString("HH:mm", Loc.Culture), Loc.T("nu"), new Thickness(0, 2, 0, 0)),
            },
        };
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
        weatherData.PropertyChanged -= OnEnergyChanged;
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

    delegate bool EnumProc(IntPtr hwnd, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("shell32.dll")]
    static extern int SHQueryUserNotificationState(out int state);

    // A game or video in full screen, or a presentation: busy, Direct3D full screen or presentation mode
    static bool FullScreenProgram() => SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
