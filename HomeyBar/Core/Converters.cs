using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace HomeyBar.Core;

// Visible when the value is true, a non-empty text, a non-zero number or a non-empty list.
// ConverterParameter="not" turns it around.
public sealed class VisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var on = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i != 0,
            double d => d != 0,
            ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter as string == "not") on = !on;
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

// A fraction (0 to 1) as a star width, for simple bars: parameter "rest" gives the remainder
public sealed class StarConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var f = Math.Clamp(value is double d ? d : 0, 0, 1);
        if (parameter as string == "rest") f = 1 - f;
        return new GridLength(Math.Max(f, 0.0001), GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

// A number times the parameter, for pixel sizes from fractions
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is double d ? d : 0) * double.Parse(parameter as string ?? "1", CultureInfo.InvariantCulture);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

// Compares a value with the parameter, for radio buttons bound to a text setting
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter as string, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter! : Binding.DoNothing;
}

// Indents nested zones and folders
public sealed class DepthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness((value is int i ? i : 0) * 14, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CapTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Toggle { get; set; }
    public DataTemplate? Slider { get; set; }
    public DataTemplate? Enum { get; set; }
    public DataTemplate? Button { get; set; }
    public DataTemplate? Value { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is CapabilityVM c ? c.Kind switch
    {
        CapKind.Toggle => Toggle,
        CapKind.Slider => Slider,
        CapKind.Enum => Enum,
        CapKind.Button => Button,
        _ => Value,
    } : null;
}
