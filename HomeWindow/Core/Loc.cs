using System.Globalization;
using System.Windows.Markup;

namespace HomeWindow.Core;

// Texts are written in Dutch in the code; English comes from the table in Loc.En.cs
public static partial class Loc
{
    public static string Lang { get; private set; } = "nl";
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("nl-NL");

    public static void Init(string? setting)
    {
        Lang = setting is "nl" or "en" ? setting : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "nl" ? "nl" : "en";
        Culture = Lang == "nl" ? CultureInfo.GetCultureInfo("nl-NL") : CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "nl" ? CultureInfo.GetCultureInfo("en-GB") : CultureInfo.CurrentCulture;
    }

    public static string T(string text) => Lang == "nl" || !En.TryGetValue(text, out var en) ? text : en;

    public static string F(string format, params object?[] args) => string.Format(Culture, T(format), args);
}

// {c:T 'Dutch text'} in XAML
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Text);
}
