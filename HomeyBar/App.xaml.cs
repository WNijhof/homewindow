using System.IO;
using System.Windows;
using System.Windows.Interop;
using HomeyBar.Core;
using HomeyBar.Views;
using Microsoft.Win32;

namespace HomeyBar;

public partial class App : Application
{
    const string InstanceName = "HomeyBar-7d1c5e2a";
    const int HotkeyId = 0x4842;

    public static AppSettings Settings { get; private set; } = new();

    static Mutex? mutex;
    static EventWaitHandle? showSignal;
    Tray? tray;
    FlyoutWindow? flyout;
    MainWindow? main;
    HwndSource? messages;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        // A tray app should keep running after an error in one screen; every error is logged
        DispatcherUnhandledException += (_, ev) =>
        {
            Log.Error("UI", ev.Exception);
            ev.Handled = true;
            HomeyStore.I.ShowError(Loc.F("Er ging iets mis: {0}", ev.Exception.Message));
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => { if (ev.ExceptionObject is Exception ex) Log.Error("Fatal", ex); };
        TaskScheduler.UnobservedTaskException += (_, ev) => { Log.Error("Task", ev.Exception); ev.SetObserved(); };

        var args = e.Args.ToList();
        var snapshot = args.IndexOf("--snapshot") is var si && si >= 0 && si + 1 < args.Count ? args[si + 1] : null;
        if (snapshot != null)
        {
            base.OnStartup(e);
            _ = SnapshotOrFailAsync(snapshot, args.Contains("--dark"));
            return;
        }

        mutex = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            // Already running: ask that one to show its window
            try { EventWaitHandle.OpenExisting(InstanceName + "-show").Set(); } catch { }
            Shutdown();
            return;
        }
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + "-show");
        new Thread(() =>
        {
            while (showSignal.WaitOne()) Dispatcher.BeginInvoke(() => ShowMain());
        }) { IsBackground = true }.Start();

        base.OnStartup(e);
        Settings = AppSettings.Load();
        Loc.Init(Settings.Language);
        Theme.Apply();
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
                Dispatcher.BeginInvoke(() => Theme.Apply());
        };

        tray = new Tray();
        tray.Clicked += ToggleFlyout;
        tray.OpenMain += () => ShowMain();
        tray.OpenSettings += () => ShowMain("settings");
        tray.Quit += Quit;

        HomeyStore.I.NewNotification += n =>
        {
            if (Settings.Toasts) tray.Notify(n.Owner.Length > 0 ? n.Owner : HomeyStore.I.HomeyName, n.Text);
        };

        flyout = new FlyoutWindow();
        RegisterHotkey();
        var demo = args.Contains("--demo");
        HomeyStore.I.Start(demo ? Demo.Config() : Settings.ActiveHomey);

        var quiet = args.Contains("--tray");
        if (Settings.Homeys.Count == 0 && !demo) ShowMain("settings");
        else if (!quiet) ShowMain();

        if (Settings.LastVersion != Updater.CurrentText)
        {
            if (Settings.LastVersion != null) tray.Notify("HomeyBar", Loc.F("Bijgewerkt naar versie {0}", Updater.CurrentText));
            Settings.LastVersion = Updater.CurrentText;
            Settings.Save();
        }
        if (!demo) Updater.I.Start();
    }

    // HomeyBar is in use while the panel is open or the main window has the focus
    public bool IsInUse => flyout?.IsVisible == true || main?.IsActive == true;

    // The setup of a new version replaces the files; it starts HomeyBar again when it is done
    public void QuitForUpdate() => Quit();

    // A failing snapshot leaves error.txt behind and ends, instead of waiting forever
    async Task SnapshotOrFailAsync(string folder, bool dark)
    {
        try { await SnapshotAsync(folder, dark); }
        catch (Exception e)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "error.txt"), e.ToString());
            Shutdown(1);
        }
    }

    // Renders every page and the flyout of the demo home to PNG files, off screen.
    // For checking the design: HomeyBar.exe --snapshot <folder> [--dark]
    async Task SnapshotAsync(string folder, bool dark)
    {
        Settings = new AppSettings { Theme = dark ? "dark" : "light", Backdrop = "aurora", Language = "nl", ReadOnly = true };
        Loc.Init("nl");
        Theme.Apply();
        Directory.CreateDirectory(folder);
        var store = HomeyStore.I;
        store.Start(Demo.Config());

        main = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false };
        main.Show();
        await Task.Delay(2500);
        foreach (var page in new[] { "overview", "devices", "flows", "moods", "variables", "energy", "batteries", "notifications", "system", "settings" })
        {
            main.Navigate(page);
            await Task.Delay(page is "energy" or "system" ? 3500 : 500);
            Render((FrameworkElement)main.Content, Path.Combine(folder, $"main-{page}.png"));
        }
        main.Navigate("devices");
        main.ShowDetails(store.Devices.First(d => d.Id == "thermostat"));
        await Task.Delay(400);
        Render((FrameworkElement)main.Content, Path.Combine(folder, "main-detail.png"));

        flyout = new FlyoutWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false };
        flyout.Show();
        foreach (var (tab, view) in new[] { ("favorites", "list"), ("favorites", "grid"), ("rooms", "list"), ("rooms", "grid"), ("flows", "list"), ("moods", "list") })
        {
            Settings.FlyoutView = view;
            flyout.ApplyView();
            flyout.SelectTab(tab);
            await Task.Delay(400);
            Render((FrameworkElement)flyout.Content, Path.Combine(folder, $"flyout-{tab}-{view}.png"));
        }
        Shutdown();
    }

    static void Render(FrameworkElement element, string file)
    {
        element.UpdateLayout();
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(element);
        var size = new Size(element.ActualWidth, element.ActualHeight);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(element), null, new Rect(size));
        }
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    public void ToggleFlyout()
    {
        if (flyout == null) return;
        if (flyout.IsVisible) flyout.HideFlyout();
        else flyout.ShowFlyout();
    }

    public void ShowMain(string? page = null)
    {
        flyout?.HideFlyout();
        main ??= new MainWindow();
        main.ShowPage(page);
    }

    public static void SwitchHomey(HomeyConfig homey)
    {
        Settings.ActiveHomeyId = homey.Id;
        Settings.Save();
        HomeyStore.I.Start(homey);
    }

    public void RegisterHotkey()
    {
        if (messages == null)
        {
            // A message-only window receives the hotkey
            messages = new HwndSource(new HwndSourceParameters("HomeyBarMessages") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
            messages.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == Native.WM_HOTKEY && w.ToInt32() == HotkeyId)
                {
                    ToggleFlyout();
                    handled = true;
                }
                return IntPtr.Zero;
            });
        }
        Native.UnregisterHotKey(messages.Handle, HotkeyId);
        if (Settings.Hotkey)
            Native.RegisterHotKey(messages.Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x48 /* H */);
    }

    void Quit()
    {
        HomeyStore.I.Stop();
        tray?.Dispose();
        tray = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        if (messages != null) Native.UnregisterHotKey(messages.Handle, HotkeyId);
        base.OnExit(e);
    }
}
