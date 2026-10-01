namespace VoiceInput.Windows.Hotkeys;

/// <summary>Outcome of registering the two global hotkeys. A taken hotkey must not stop the application.</summary>
public readonly record struct HotkeyAvailability(
    bool HoldHotkeyAvailable,
    int HoldHotkeyError,
    bool ToggleHotkeyAvailable,
    int ToggleHotkeyError)
{
    public const int HoldHotkeyId = 0x5649;
    public const int ToggleHotkeyId = 0x564A;

    public static HotkeyAvailability RegisterAll(Func<int, bool> register, Func<int> getLastError)
    {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(getLastError);

        var hold = register(HoldHotkeyId);
        var holdError = hold ? 0 : getLastError();
        var toggle = register(ToggleHotkeyId);
        var toggleError = toggle ? 0 : getLastError();
        return new HotkeyAvailability(hold, holdError, toggle, toggleError);
    }

    /// <summary>A user-facing explanation, or null when both hotkeys are available.</summary>
    public string? CreateConflictMessage()
    {
        if (HoldHotkeyAvailable && ToggleHotkeyAvailable)
        {
            return null;
        }

        var taken = new List<string>(2);
        if (!HoldHotkeyAvailable)
        {
            taken.Add("Ctrl+Shift+Space");
        }

        if (!ToggleHotkeyAvailable)
        {
            taken.Add("Ctrl+Shift+K");
        }

        var verb = taken.Count == 1 ? "занято" : "заняты";
        var advice = HoldHotkeyAvailable || ToggleHotkeyAvailable
            ? "Остальные сочетания работают."
            : "Освободите сочетание и перезапустите Voice Input.";
        return $"{string.Join(" и ", taken)} {verb} другой программой. {advice}";
    }
}
