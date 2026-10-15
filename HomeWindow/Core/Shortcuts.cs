namespace HomeWindow.Core;

// Quick control of the favourites without the mouse: Ctrl+1..9 in the windows, and the notes of a MIDI keyboard.
// Slot 1 is the first favourite, in the order the Favourites page shows.
public static class Shortcuts
{
    public const int Slots = 9;

    // Presses the quick button of the favourite in this slot (1-based); false when there is none or HomeWindow is locked
    public static bool Press(int slot)
    {
        if (slot is < 1 or > Slots || App.Gate.IsLocked) return false;
        var device = HomeyStore.I.FavoriteDevices.ElementAtOrDefault(slot - 1);
        if (device is not { Available: true } || device.QuickGlyph == null) return false;
        device.QuickCommand.Execute(null);
        return true;
    }

    // The slot a Ctrl+digit key stands for, or 0
    public static int SlotOf(System.Windows.Input.Key key) => key switch
    {
        >= System.Windows.Input.Key.D1 and <= System.Windows.Input.Key.D9 => key - System.Windows.Input.Key.D1 + 1,
        >= System.Windows.Input.Key.NumPad1 and <= System.Windows.Input.Key.NumPad9 => key - System.Windows.Input.Key.NumPad1 + 1,
        _ => 0,
    };

    // The slot of a MIDI note: middle C (60) is the first favourite, C#4 the second, and so on
    public static int SlotOfNote(int note) => note is >= 60 and < 60 + Slots ? note - 59 : 0;
}
