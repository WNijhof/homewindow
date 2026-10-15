using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace HomeWindow.Core;

// Listens to the MIDI keyboards and pads on this PC (winmm) and presses a favourite on a note; see Shortcuts.
// Only HomeWindow's own Windows-side input; the controller needs no connection to Homey.
public static class Midi
{
    const int CALLBACK_FUNCTION = 0x30000, MIM_DATA = 0x3C3;
    delegate void MidiInProc(IntPtr handle, int msg, IntPtr instance, IntPtr param1, IntPtr param2);

    [DllImport("winmm.dll")] static extern int midiInGetNumDevs();
    [DllImport("winmm.dll")] static extern int midiInOpen(out IntPtr handle, int device, MidiInProc callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")] static extern int midiInStart(IntPtr handle);
    [DllImport("winmm.dll")] static extern int midiInStop(IntPtr handle);
    [DllImport("winmm.dll")] static extern int midiInClose(IntPtr handle);

    // Kept in a field: winmm calls it from its own thread for as long as a device is open
    static readonly MidiInProc callback = OnMessage;
    static readonly List<IntPtr> open = [];
    static Dispatcher? ui;

    public static int DeviceCount { get { try { return midiInGetNumDevs(); } catch { return 0; } } }

    // Opens every MIDI input when switched on, closes them when off; safe to call again after a settings change
    public static void Apply(bool enabled)
    {
        Stop();
        if (!enabled) return;
        try
        {
            ui = Dispatcher.CurrentDispatcher;
            for (var i = 0; i < midiInGetNumDevs(); i++)
                if (midiInOpen(out var handle, i, callback, IntPtr.Zero, CALLBACK_FUNCTION) == 0 && midiInStart(handle) == 0) open.Add(handle);
                else if (handle != IntPtr.Zero) midiInClose(handle);
        }
        catch (Exception e)
        {
            Log.Error("Midi", e);
        }
    }

    public static void Stop()
    {
        foreach (var handle in open)
        {
            try { midiInStop(handle); midiInClose(handle); } catch { }
        }
        open.Clear();
    }

    static void OnMessage(IntPtr handle, int msg, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        if (msg != MIM_DATA) return;
        var data = param1.ToInt64();
        var status = (int)(data & 0xFF);
        var note = (int)((data >> 8) & 0x7F);
        var velocity = (int)((data >> 16) & 0x7F);
        // Note on; many keyboards send note off as note on with velocity 0
        if ((status & 0xF0) != 0x90 || velocity == 0) return;
        var slot = Shortcuts.SlotOfNote(note);
        if (slot > 0) ui?.BeginInvoke(() => Shortcuts.Press(slot));
    }
}
