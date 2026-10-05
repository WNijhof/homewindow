using System.Windows;
using System.Windows.Controls;
using HomeWindow.Core;

namespace HomeWindow.Views;

// The lock switch in the top bar of the panel and the main window: closed padlock in the accent colour when
// the PIN lock is on, open padlock when it is paused. Hidden when there is no PIN.
static class LockToggleButton
{
    public static void Update(Button button)
    {
        var gate = App.Gate;
        button.Dispatcher.Invoke(() =>
        {
            button.Visibility = gate.IsSet ? Visibility.Visible : Visibility.Collapsed;
            button.Content = gate.IsActive ? "\uE72E" : "\uE785";
            button.SetResourceReference(Control.ForegroundProperty, gate.IsActive ? "AccentBrush" : "SubTextBrush");
            button.ToolTip = Loc.T(gate.IsActive ? "Vergrendeling staat aan. Klik om uit te zetten." : "Vergrendeling staat uit. Klik om aan te zetten.");
        });
    }
}
