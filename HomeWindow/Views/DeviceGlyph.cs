using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HomeWindow.Views;

// The icon of a device: its icon from Homey when there is one, otherwise a glyph from the icon font.
// Both take the text colour, so Homey's black line icons also show on a dark theme or an active tile.
public sealed class DeviceGlyph : Control
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(DeviceGlyph));
    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(nameof(Image), typeof(ImageSource), typeof(DeviceGlyph));

    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public ImageSource? Image { get => (ImageSource?)GetValue(ImageProperty); set => SetValue(ImageProperty, value); }

    static DeviceGlyph() => DefaultStyleKeyProperty.OverrideMetadata(typeof(DeviceGlyph), new FrameworkPropertyMetadata(typeof(DeviceGlyph)));
}
