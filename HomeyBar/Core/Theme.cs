using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace HomeyBar.Core;

// Colours and backdrops as dynamic resources, so a change in the settings shows at once
public static class Theme
{
    public static event Action? Changed;
    public static bool IsDark { get; private set; }
    public static double Scale { get; private set; } = 1;

    public static readonly string[] Backdrops = ["paper", "aurora", "dusk", "ocean", "glass"];

    public static void Apply()
    {
        var s = App.Settings;
        IsDark = s.Theme == "dark" || (s.Theme == "system" && SystemIsDark());
        Scale = Math.Clamp(s.TextScale, 0.8, 1.4);
        var r = Application.Current.Resources;
        var accent = SystemAccent();
        var dark = IsDark;

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
        Brush("TextBrush", dark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
        Brush("SubTextBrush", dark ? Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xA0, 0x00, 0x00, 0x00));
        Brush("FaintTextBrush", dark ? Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x70, 0x00, 0x00, 0x00));
        Brush("DividerBrush", dark ? Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x18, 0x00, 0x00, 0x00));
        Brush("WarnBrush", dark ? Color.FromRgb(0xFF, 0xB0, 0x40) : Color.FromRgb(0xC2, 0x6A, 0x00));
        Brush("AlarmBrush", dark ? Color.FromRgb(0xFF, 0x6B, 0x6B) : Color.FromRgb(0xC4, 0x2B, 0x1C));
        Brush("GoodBrush", dark ? Color.FromRgb(0x5E, 0xD3, 0x8A) : Color.FromRgb(0x10, 0x7C, 0x41));
        Brush("SolarBrush", dark ? Color.FromRgb(0xFF, 0xC8, 0x3D) : Color.FromRgb(0xE0, 0x9B, 0x00));
        Brush("GridBrush", dark ? Color.FromRgb(0x7A, 0xA7, 0xFF) : Color.FromRgb(0x2F, 0x6B, 0xE0));

        // Cards: more or less see-through, depending on the setting
        var o = Math.Clamp(s.CardOpacity, 0, 1);
        var glass = s.Backdrop == "glass";
        Brush("CardBrush", dark
            ? Color.FromArgb((byte)(0x0E + o * 0x2A), 0xFF, 0xFF, 0xFF)
            : Color.FromArgb((byte)(0x70 + o * 0x8F), 0xFF, 0xFF, 0xFF));
        Brush("CardHoverBrush", dark ? Color.FromArgb((byte)(0x22 + o * 0x30), 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xFF, 0xF7, 0xF7, 0xF7));
        Brush("CardBorderBrush", dark ? Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00));
        Brush("ActiveCardBrush", dark ? Blend(Color.FromArgb(0xFF, 0x30, 0x30, 0x30), accent, 0.35) : Blend(Colors.White, accent, 0.16));
        Brush("IconBgBrush", dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x10, 0x00, 0x00, 0x00));
        Brush("InputBrush", dark ? Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF));
        Brush("SurfaceBrush", dark ? Color.FromArgb(glass ? (byte)0x18 : (byte)0x40, 0x00, 0x00, 0x00) : Color.FromArgb(glass ? (byte)0x30 : (byte)0x60, 0xFF, 0xFF, 0xFF));
        Brush("TrackBrush", dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0x00, 0x00, 0x00));
        Brush("PopupBrush", dark ? Color.FromRgb(0x2C, 0x2C, 0x2C) : Color.FromRgb(0xFB, 0xFB, 0xFB));

        r["BackdropBrush"] = BackdropBrush(s.Backdrop, Math.Clamp(s.Intensity, 0, 1), dark);

        if (s.Shadows)
        {
            var shadow = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = dark ? 0.35 : 0.10, Color = Colors.Black };
            shadow.Freeze();
            r["CardShadow"] = shadow;
        }
        else r["CardShadow"] = null;

        Changed?.Invoke();
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
