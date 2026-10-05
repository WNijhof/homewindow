using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HomeWindow.Core;

namespace HomeWindow.Views;

// Covers a window while the PIN lock is on
public partial class LockOverlay : UserControl
{
    readonly DispatcherTimer countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    PinGate? gate;

    public LockOverlay()
    {
        InitializeComponent();
        countdown.Tick += (_, _) => ShowWait();
        Loaded += (_, _) =>
        {
            if (gate != null) return;
            gate = App.Gate;
            gate.Changed += Update;
            Update();
        };
        Unloaded += (_, _) => { if (gate != null) gate.Changed -= Update; gate = null; };
    }

    // Both windows hide instead of closing, so the overlay is told when its window opens again
    public void Focus_Pin()
    {
        if (Visibility == Visibility.Visible) Dispatcher.BeginInvoke(() => PinBox.Focus(), DispatcherPriority.Input);
    }

    void Update()
    {
        Dispatcher.Invoke(() =>
        {
            var locked = gate?.IsLocked == true;
            Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            PinBox.Clear();
            Message.Visibility = Visibility.Collapsed;
            if (locked) { ShowWait(); Focus_Pin(); }
        });
    }

    void ShowWait()
    {
        var wait = gate?.WaitSeconds ?? 0;
        if (wait > 0)
        {
            Say(Loc.F("Te vaak fout. Probeer het over {0} seconden opnieuw.", wait));
            countdown.Start();
        }
        else if (countdown.IsEnabled)
        {
            countdown.Stop();
            Message.Visibility = Visibility.Collapsed;
        }
        PinBox.IsEnabled = UnlockButton.IsEnabled = wait == 0;
        if (wait == 0 && IsVisible) PinBox.Focus();
    }

    void Say(string text)
    {
        Message.Text = text;
        Message.Visibility = Visibility.Visible;
    }

    void Pin_TextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsAsciiDigit);

    void Pin_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(typeof(string)) is not string text || !text.All(char.IsAsciiDigit)) e.CancelCommand();
    }

    void Pin_Changed(object sender, RoutedEventArgs e)
    {
        if (countdown.IsEnabled) return;
        Message.Visibility = Visibility.Collapsed;
    }

    void Pin_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Unlock_Click(sender, e);
        e.Handled = true;
    }

    void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (gate == null || PinBox.Password.Length == 0) return;
        switch (gate.TryUnlock(PinBox.Password))
        {
            case PinResult.Ok:
                break;
            case PinResult.Wrong:
                Say(Loc.T("Dat is niet de juiste pincode."));
                PinBox.Clear();
                PinBox.Focus();
                break;
            case PinResult.Wait:
                PinBox.Clear();
                ShowWait();
                break;
        }
    }
}
