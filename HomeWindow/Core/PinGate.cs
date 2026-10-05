using System.Security.Cryptography;

namespace HomeWindow.Core;

public enum PinResult { Ok, Wrong, Wait }

// The PIN lock: HomeWindow starts locked, locks again after a while or when Windows locks, and asks for the PIN.
// It keeps casual users out of the home; it is no protection against someone who owns the Windows account.
public sealed class PinGate
{
    public const int MinLength = 4, MaxLength = 12;
    const int Iterations = 100_000, MaxAttempts = 5;
    static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);

    readonly AppSettings settings;
    readonly Func<DateTime> now;
    int failures;
    DateTime waitUntil;
    DateTime lastUse;

    public PinGate(AppSettings settings, Func<DateTime>? now = null)
    {
        this.settings = settings;
        this.now = now ?? (() => DateTime.UtcNow);
        IsLocked = IsActive;
        lastUse = this.now();
    }

    public event Action? Changed;

    public bool IsSet => !string.IsNullOrEmpty(settings.PinHash);
    // A PIN is set and the lock is switched on
    public bool IsActive => IsSet && settings.PinActive;
    public bool IsLocked { get; private set; }

    // Seconds left before another try is allowed
    public int WaitSeconds => Math.Max(0, (int)Math.Ceiling((waitUntil - now()).TotalSeconds));

    public static bool IsValid(string? pin) =>
        pin is { Length: >= MinLength and <= MaxLength } && pin.All(char.IsAsciiDigit);

    public void Lock()
    {
        if (!IsActive || IsLocked) return;
        IsLocked = true;
        Changed?.Invoke();
    }

    public PinResult TryUnlock(string pin)
    {
        var result = Check(pin);
        if (result == PinResult.Ok && IsLocked)
        {
            IsLocked = false;
            lastUse = now();
            Changed?.Invoke();
        }
        return result;
    }

    // Checks a PIN without unlocking; wrong tries count, and too many make the next ones wait
    public PinResult Check(string pin)
    {
        if (!IsSet) return PinResult.Ok;
        if (WaitSeconds > 0) return PinResult.Wait;
        if (Verify(pin, settings.PinHash))
        {
            failures = 0;
            return PinResult.Ok;
        }
        if (++failures >= MaxAttempts)
        {
            failures = 0;
            waitUntil = now() + Delay;
            return PinResult.Wait;
        }
        return PinResult.Wrong;
    }

    // Set (or replace) the PIN. The caller has checked the old one. Switching it on leaves HomeWindow unlocked.
    public void SetPin(string pin)
    {
        settings.PinHash = Hash(pin);
        settings.PinActive = true;
        failures = 0;
        IsLocked = false;
        lastUse = now();
        settings.Save();
        Changed?.Invoke();
    }

    // The switch in the top bar. Switching on does not lock at once; the lock acts at the usual moments.
    // It can only be reached while unlocked, so switching off needs no PIN of its own.
    public void SetActive(bool active)
    {
        if (!IsSet || settings.PinActive == active) return;
        settings.PinActive = active;
        if (!active) IsLocked = false;
        lastUse = now();
        settings.Save();
        Changed?.Invoke();
    }

    public void ClearPin()
    {
        settings.PinHash = "";
        IsLocked = false;
        settings.Save();
        Changed?.Invoke();
    }

    // Called now and then with whether HomeWindow is in use; locks after the chosen minutes without use
    public void Tick(bool inUse)
    {
        if (inUse || IsLocked) { lastUse = now(); return; }
        if (IsActive && settings.PinAutoLockMinutes > 0 && now() - lastUse >= TimeSpan.FromMinutes(settings.PinAutoLockMinutes)) Lock();
    }

    // "iterations.salt.hash", all in Base64
    internal static string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(Derive(pin, salt, Iterations))}";
    }

    internal static bool Verify(string pin, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations < 1) return false;
        try
        {
            var expected = Convert.FromBase64String(parts[2]);
            return CryptographicOperations.FixedTimeEquals(Derive(pin, Convert.FromBase64String(parts[1]), iterations), expected);
        }
        catch (FormatException) { return false; }
    }

    static byte[] Derive(string pin, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, 32);
}
