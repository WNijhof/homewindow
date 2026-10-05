using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace HomeWindow.Core;

// Colours and backdrops as dynamic resources, so a change in the settings shows at once
public static class Theme
{
    public static event Action? Changed;
    public static bool IsDark { get; private set; }
    public static double Scale { get; private set; } = 1;

    public static readonly string[] Backdrops = ["dashboard", "paper", "aurora", "dusk", "ocean", "glass"];

    // Tested sizes of a device tile. The panel by the clock is 384 wide: four tiles a row when extra small, three, or two when large.
    // Compact: tighter margins and no detail line, for the extra small tiles.
    public sealed record TileMetrics(double Width, double Height, double NameHeight, double NameLine, double NameSize, double IconSize, bool Compact = false);

    public static TileMetrics Tiles(string? size, bool flyout) => (size, flyout) switch
    {
        ("xsmall", false) => new(104, 84, 16, 16, 12, 18, Compact: true),
        ("small", false) => new(128, 110, 18, 18, 13, 20),
        ("large", false) => new(184, 158, 40, 20, 15.5, 30),
        (_, false) => new(150, 134, 36, 18, 13.5, 24),
        ("xsmall", true) => new(79, 80, 16, 16, 12, 18, Compact: true),
        ("small", true) => new(108, 100, 18, 18, 13, 20),
        ("large", true) => new(168, 132, 36, 18, 14, 26),
        (_, true) => new(108, 112, 18, 18, 13.5, 24),
    };

    public static void SetTiles(ResourceDictionary r, TileMetrics t)
    {
        r["TileWidth"] = t.Width;
        r["TileHeight"] = t.Height;
        r["TileNameHeight"] = t.NameHeight;
        r["TileNameLine"] = t.NameLine;
        r["TileNameSize"] = t.NameSize;
        r["TileIconSize"] = t.IconSize;
        r["TilePadding"] = t.Compact ? new Thickness(8, 8, 8, 7) : new Thickness(12, 12, 12, 10);
        r["TileToggleMargin"] = t.Compact ? new Thickness(0, 6, 6, 0) : new Thickness(0, 9, 9, 0);
        r["TileStarMargin"] = t.Compact ? new Thickness(0, 15, 40, 0) : new Thickness(0, 18, 46, 0);
        r["TileDetailHeight"] = t.Compact ? 0.0 : double.PositiveInfinity;
    }

    public static void Apply()
    {
        var s = App.Settings;
        IsDark = s.Theme == "dark" || (s.Theme == "system" && SystemIsDark());
        Scale = Math.Clamp(s.TextScale, 0.8, 1.4);
        var r = Application.Current.Resources;
        var dark = IsDark;
        // The look of the Homey energy dashboard: flat background, solid cards, its own colours
        var dash = s.Backdrop == "dashboard";
        var accent = dash ? Rgb(0x0A84FF) : SystemAccent();

        void Brush(string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            r[key] = b;
        }

        Brush("AccentBrush", accent);
        Brush("OnAccentBrush", Luminance(accent) > 0.6 ? Colors.Black : Colors.White);
        Brush("AccentSoftBrush", WithAlpha(accent, (byte)(dark ? 0x50 : 0x2A)));
        // Accent for text and glyphs, light enough to read on dark cards
        Brush("AccentTextBrush", dark ? Blend(accent, Colors.White, 0.5) : accent);
        if (dash)
        {
            Brush("TextBrush", dark ? Rgb(0xF5F5F7) : Rgb(0x1C1C1E));
            Brush("SubTextBrush", dark ? Rgb(0x98989F) : Rgb(0x8A8A8E));
            Brush("FaintTextBrush", dark ? Color.FromArgb(0x80, 0x98, 0x98, 0x9F) : Color.FromArgb(0x90, 0x8A, 0x8A, 0x8E));
            Brush("DividerBrush", dark ? Color.FromArgb(0x1F, 0xEB, 0xEB, 0xF5) : Color.FromArgb(0x1F, 0x3C, 0x3C, 0x43));
            Brush("WarnBrush", Rgb(0xFF9F0A));
            Brush("AlarmBrush", Rgb(0xFF4D4F));
            Brush("GoodBrush", Rgb(0x20C07C));
            Brush("SolarBrush", Rgb(0xFFB400));
            Brush("GridBrush", dark ? Rgb(0x4C9BFF) : Rgb(0x3D8BFD));
            var card = dark ? Rgb(0x1C1C1E) : Colors.White;
            Brush("CardBrush", card);
            Brush("CardHoverBrush", dark ? Rgb(0x2C2C2E) : Rgb(0xF5F5F8));
            Brush("CardBorderBrush", dark ? Color.FromArgb(0x1F, 0xEB, 0xEB, 0xF5) : Color.FromArgb(0x1F, 0x3C, 0x3C, 0x43));
            Brush("CardEdgeBrush", Colors.Transparent);
            Brush("ButtonEdgeBrush", Colors.Transparent);
            // Buttons like the dashboard's segmented control: a grey fill instead of a card
            Brush("ButtonBrush", dark ? Color.FromArgb(0x3D, 0x76, 0x76, 0x80) : Color.FromArgb(0x1F, 0x76, 0x76, 0x80));
            Brush("ActiveCardBrush", Blend(card, accent, dark ? 0.28 : 0.12));
            // --track: the text colour at 9 %
            Brush("IconBgBrush", dark ? Color.FromArgb(0x17, 0xF5, 0xF5, 0xF7) : Color.FromArgb(0x17, 0x1C, 0x1C, 0x1E));
            Brush("InputBrush", dark ? Rgb(0x2C2C2E) : Rgb(0xF5F5F8));
            Brush("SurfaceBrush", dark ? Color.FromArgb(0x99, 0x1C, 0x1C, 0x1E) : Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));
            Brush("TrackBrush", dark ? Color.FromArgb(0x3D, 0x76, 0x76, 0x80) : Color.FromArgb(0x1F, 0x76, 0x76, 0x80));
            Brush("PopupBrush", dark ? Rgb(0x2C2C2E) : Colors.White);
        }
        else
        {
            Brush("TextBrush", dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
            Brush("SubTextBrush", dark ? Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA0, 0x00, 0x00, 0x00));
            Brush("FaintTextBrush", dark ? Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x70, 0x00, 0x00, 0x00));
            Brush("DividerBrush", dark ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00));
            Brush("WarnBrush", dark ? Color.FromRgb(0xFF, 0xB0, 0x40) : Color.FromRgb(0xC2, 0x6A, 0x00));
            Brush("AlarmBrush", dark ? Color.FromRgb(0xFF, 0x6B, 0x6B) : Color.FromRgb(0xC4, 0x2B, 0x1C));
            Brush("GoodBrush", dark ? Color.FromRgb(0x5E, 0xD3, 0x8A) : Color.FromRgb(0x10, 0x7C, 0x41));
            Brush("SolarBrush", dark ? Color.FromRgb(0xFF, 0xC8, 0x3D) : Color.FromRgb(0xE0, 0x9B, 0x00));
            Brush("GridBrush", dark ? Color.FromRgb(0x7A, 0xA7, 0xFF) : Color.FromRgb(0x2F, 0x6B, 0xE0));

            // Cards: a little see-through
            const double o = 0.6;
            var glass = s.Backdrop == "glass";
            Brush("CardBrush", dark
                ? Color.FromArgb((byte)(0x0E + o * 0x2A), 0xFF, 0xFF, 0xFF)
                : Color.FromArgb((byte)(0x70 + o * 0x8F), 0xFF, 0xFF, 0xFF));
            Brush("CardHoverBrush", dark ? Color.FromArgb((byte)(0x22 + o * 0x30), 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xFF, 0xF7, 0xF7, 0xF7));
            Brush("CardBorderBrush", dark ? Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00));
            r["CardEdgeBrush"] = r["CardBorderBrush"];
            r["ButtonBrush"] = r["CardBrush"];
            r["ButtonEdgeBrush"] = r["CardBorderBrush"];
            Brush("ActiveCardBrush", dark ? Blend(Color.FromArgb(0xFF, 0x30, 0x30, 0x30), accent, 0.35) : Blend(Colors.White, accent, 0.16));
            Brush("IconBgBrush", dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0x00, 0x00, 0x00));
            Brush("InputBrush", dark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF));
            Brush("SurfaceBrush", dark ? Color.FromArgb(glass ? (byte)0x18 : (byte)0x40, 0x00, 0x00, 0x00) : Color.FromArgb(glass ? (byte)0x30 : (byte)0x60, 0xFF, 0xFF, 0xFF));
            Brush("TrackBrush", dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0x00, 0x00, 0x00));
            Brush("PopupBrush", dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xFB, 0xFB, 0xFB));
        }
        r["CardRadius"] = new CornerRadius(dash ? 20 : 12);

        SetTiles(r, Tiles(s.TileSize, flyout: false));

        r["BackdropBrush"] = dash ? MoodBrush(Mood, Math.Clamp(s.Intensity, 0, 1), dark) : BackdropBrush(s.Backdrop, Math.Clamp(s.Intensity, 0, 1), dark);

        // The dashboard has a soft shadow in light mode and none in dark mode
        if (s.Shadows && !(dash && dark))
        {
            var shadow = dash
                ? new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.07, Color = Colors.Black }
                : new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = dark ? 0.35 : 0.10, Color = Colors.Black };
            shadow.Freeze();
            r["CardShadow"] = shadow;
        }
        else r["CardShadow"] = null;

        Changed?.Invoke();
    }

    // Where the power comes from right now: "solar", "grid", "battery" or null
    public static string? Mood { get; private set; }

    // Like the dashboard: a soft glow behind everything in the colour of the power source
    public static void SetMood(string? mood)
    {
        if (Application.Current is not { } app) return;
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => SetMood(mood));
            return;
        }
        if (mood == Mood) return;
        Mood = mood;
        var s = App.Settings;
        if (s.Backdrop == "dashboard") Application.Current.Resources["BackdropBrush"] = MoodBrush(mood, Math.Clamp(s.Intensity, 0, 1), IsDark);
    }

    static Brush MoodBrush(string? mood, double intensity, bool dark)
    {
        var bg = dark ? Colors.Black : Rgb(0xF2F2F7);
        Color? tint = mood switch
        {
            "solar" => Rgb(0xFFB400),
            "grid" => dark ? Rgb(0x4C9BFF) : Rgb(0x3D8BFD),
            "battery" => Rgb(0x14B8A6),
            _ => null,
        };
        if (tint is not { } t || intensity <= 0.01)
        {
            var flat = new SolidColorBrush(bg);
            flat.Freeze();
            return flat;
        }
        // The dashboard mixes 16 % (light) or 26 % (dark) of the colour in; the intensity slider scales that
        var strength = (dark ? 0.26 : 0.16) * intensity / 0.6;
        var glow = new RadialGradientBrush
        {
            Center = new Point(0.85, -0.12), GradientOrigin = new Point(0.85, -0.12), RadiusX = 0.75, RadiusY = 0.7,
            GradientStops = { new GradientStop(Blend(bg, t, Math.Min(strength, 0.5)), 0), new GradientStop(bg, 1) },
        };
        glow.Freeze();
        return glow;
    }

    static Brush BackdropBrush(string name, double intensity, bool dark)
    {
        var baseColor = dark ? Color.FromRgb(0x1C, 0x1C, 0x1E) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        if (name == "glass" && !HasSystemBackdrop) name = "paper";
        if (name == "glass")
        {
            var tint = new SolidColorBrush(dark ? Color.FromArgb((byte)(0x20 + intensity * 0x70), 0x10, 0x10, 0x14) : Color.FromArgb((byte)(0x10 + intensity * 0x70), 0xFA, 0xFA, 0xFA));
            tint.Freeze();
            return tint;
        }
        (Color a, Color b, Color c) = name switch
        {
            "aurora" => dark ? (Rgb(0x0B2E2A), Rgb(0x16203F), Rgb(0x2B1A44)) : (Rgb(0xD9F6EC), Rgb(0xDDE8FB), Rgb(0xEADFFB)),
            "dusk" => dark ? (Rgb(0x3B1F1C), Rgb(0x2E1A33), Rgb(0x1B1736)) : (Rgb(0xFDE4D2), Rgb(0xF7DCE6), Rgb(0xE6DDF6)),
            "ocean" => dark ? (Rgb(0x0B2237), Rgb(0x0D2A3A), Rgb(0x0C3233)) : (Rgb(0xD5EAFB), Rgb(0xD6F0F6), Rgb(0xD9F5EE)),
            _ => dark ? (Rgb(0x23211E), Rgb(0x22201D), Rgb(0x1F1D1A)) : (Rgb(0xF7F3EC), Rgb(0xF4EFE6), Rgb(0xF1EBE1)),
        };
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(Blend(baseColor, a, intensity), 0));
        brush.GradientStops.Add(new GradientStop(Blend(baseColor, b, intensity), 0.55));
        brush.GradientStops.Add(new GradientStop(Blend(baseColor, c, intensity), 1));
        brush.Freeze();
        return brush;
    }

    // Window chrome: dark title bar, rounded corners and the Windows 11 backdrop behind the content
    public static void ApplyToWindow(Window window, bool transient)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        Native.SetAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, IsDark ? 1 : 0);
        Native.SetAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, Native.DWMWCP_ROUND);
        if (Environment.OSVersion.Version.Build >= 22621)
        {
            if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target }) target.BackgroundColor = Colors.Transparent;
            var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
            Native.SetAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, transient ? Native.DWMSBT_TRANSIENTWINDOW : Native.DWMSBT_MAINWINDOW);
        }
        if (window.Content is FrameworkElement root)
            root.LayoutTransform = Math.Abs(Scale - 1) < 0.01 ? Transform.Identity : new ScaleTransform(Scale, Scale);
    }

    // Without the Windows 11 backdrop the window needs a solid background
    public static bool HasSystemBackdrop => Environment.OSVersion.Version.Build >= 22621;

    public static bool SystemIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    // The taskbar follows the Windows mode, which can differ from the app mode
    public static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    static Color SystemAccent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        if (key?.GetValue("AccentColor") is int abgr)
        {
            var c = Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
            // Very dark or very light accents read badly on cards
            var l = Luminance(c);
            if (l < 0.12) return Blend(c, Colors.White, 0.35);
            if (l > 0.85) return Blend(c, Colors.Black, 0.3);
            return c;
        }
        return Color.FromRgb(0x00, 0x78, 0xD4);
    }

    static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
    static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
