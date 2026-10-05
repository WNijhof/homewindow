using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HomeWindow.Core;

// The icon next to the clock: a click opens the flyout, the menu reaches the rest
public sealed class Tray : IDisposable
{
    readonly NotifyIcon icon;
    readonly Icon baseIcon;
    IntPtr lastHandle;
    readonly HomeyStore store = HomeyStore.I;

    public event Action? Clicked;
    public event Action? OpenMain;
    public event Action? OpenSettings;
    public event Action? Quit;

    public Tray()
    {
        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/HomeWindow.ico"))!.Stream;
        baseIcon = new Icon(stream, SystemInformation.SmallIconSize);
        icon = new NotifyIcon { Icon = baseIcon, Text = "HomeWindow", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        icon.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) Clicked?.Invoke(); };
        icon.BalloonTipClicked += (_, _) => OpenMain?.Invoke();
        icon.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        store.PropertyChanged += OnStoreChanged;
        UpdateIcon();
    }

    void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeyStore.Status) or nameof(HomeyStore.StatusText) or nameof(HomeyStore.HomeyName) or "" or null) UpdateIcon();
    }

    void UpdateIcon()
    {
        var text = $"{store.HomeyName} – {store.StatusText}";
        icon.Text = text.Length > 63 ? text[..63] : text;

        // A coloured dot shows how the connection runs: none when local, blue for the cloud, red when offline
        Color? dot = store.Status switch
        {
            "cloud" => Color.FromArgb(0x3B, 0x82, 0xF6),
            "offline" or "error" => Color.FromArgb(0xE5, 0x48, 0x4D),
            "connecting" => Color.FromArgb(0xF5, 0xA5, 0x24),
            _ => null,
        };
        if (dot == null) { SetIcon(baseIcon, IntPtr.Zero); return; }

        var size = SystemInformation.SmallIconSize;
        using var bmp = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawIcon(baseIcon, new Rectangle(0, 0, size.Width, size.Height));
            var d = size.Width * 0.45f;
            var rect = new RectangleF(size.Width - d, size.Height - d, d - 0.5f, d - 0.5f);
            using var border = new SolidBrush(Color.White);
            g.FillEllipse(border, RectangleF.Inflate(rect, 1, 1));
            using var fill = new SolidBrush(dot.Value);
            g.FillEllipse(fill, rect);
        }
        var handle = bmp.GetHicon();
        SetIcon(Icon.FromHandle(handle), handle);
    }

    void SetIcon(Icon next, IntPtr handle)
    {
        var old = lastHandle;
        icon.Icon = next;
        lastHandle = handle;
        if (old != IntPtr.Zero) Native.DestroyIcon(old);
    }

    void BuildMenu()
    {
        var menu = icon.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem($"{store.HomeyName} · {store.StatusText}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("HomeWindow openen"), null, (_, _) => OpenMain?.Invoke());

        // While locked the menu offers nothing that controls the home
        if (App.Gate.IsLocked)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Loc.T("Afsluiten"), null, (_, _) => Quit?.Invoke());
            return;
        }

        if (store.FavoriteFlows.Count > 0)
        {
            var flows = new ToolStripMenuItem(Loc.T("Flows starten"));
            foreach (var f in store.FavoriteFlows)
            {
                var flow = f;
                flows.DropDownItems.Add(new ToolStripMenuItem(flow.Name, null, (_, _) => flow.RunCommand.Execute(null)) { Enabled = flow.CanRun });
            }
            menu.Items.Add(flows);
        }
        if (store.Moods.Count > 0)
        {
            var moods = new ToolStripMenuItem(Loc.T("Sferen"));
            foreach (var m in store.Moods.Take(30))
            {
                var mood = m;
                moods.DropDownItems.Add(mood.ZoneName.Length > 0 ? $"{mood.Name} ({mood.ZoneName})" : mood.Name, null, (_, _) => mood.SetCommand.Execute(null));
            }
            menu.Items.Add(moods);
        }

        var homeys = App.Settings.Homeys;
        if (homeys.Count > 1)
        {
            var switcher = new ToolStripMenuItem(Loc.T("Homey wisselen"));
            foreach (var h in homeys)
            {
                var homey = h;
                switcher.DropDownItems.Add(new ToolStripMenuItem(homey.Name, null, (_, _) => App.SwitchHomey(homey)) { Checked = homey.Id == store.Config?.Id });
            }
            menu.Items.Add(switcher);
        }

        menu.Items.Add(Loc.T("Opnieuw verbinden"), null, (_, _) => store.Reconnect());
        menu.Items.Add(Loc.T("Instellingen"), null, (_, _) => OpenSettings?.Invoke());
        if (App.Gate.IsActive) menu.Items.Add(Loc.T("Nu vergrendelen"), null, (_, _) => App.Gate.Lock());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Loc.T("Afsluiten"), null, (_, _) => Quit?.Invoke());
    }

    public void Notify(string title, string text)
    {
        icon.BalloonTipTitle = title;
        icon.BalloonTipText = text.Length > 250 ? text[..250] + "…" : text;
        icon.BalloonTipIcon = ToolTipIcon.None;
        icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        store.PropertyChanged -= OnStoreChanged;
        icon.Visible = false;
        icon.Dispose();
        if (lastHandle != IntPtr.Zero) Native.DestroyIcon(lastHandle);
        baseIcon.Dispose();
    }
}
