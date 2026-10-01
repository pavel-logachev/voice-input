using VoiceInput.Windows.Hotkeys;

namespace VoiceInput.Windows.Tests.Hotkeys;

public sealed class HotkeyAvailabilityTests
{
    [Fact]
    public void BothHotkeysAvailableProducesNoMessage()
    {
        var availability = HotkeyAvailability.RegisterAll(_ => true, () => 0);

        Assert.True(availability.HoldHotkeyAvailable);
        Assert.True(availability.ToggleHotkeyAvailable);
        Assert.Null(availability.CreateConflictMessage());
    }

    [Fact]
    public void ATakenHotkeyIsReportedWithItsErrorAndDoesNotStopTheOther()
    {
        var errors = new Queue<int>([1409]);
        var availability = HotkeyAvailability.RegisterAll(
            id => id == HotkeyAvailability.ToggleHotkeyId,
            () => errors.Dequeue());

        Assert.False(availability.HoldHotkeyAvailable);
        Assert.Equal(1409, availability.HoldHotkeyError);
        Assert.True(availability.ToggleHotkeyAvailable);
        Assert.Equal(0, availability.ToggleHotkeyError);
        Assert.Equal(
            "Ctrl+Shift+Space занято другой программой. Остальные сочетания работают.",
            availability.CreateConflictMessage());
    }

    [Fact]
    public void BothTakenAsksToFreeTheCombinationAndRestart()
    {
        var availability = HotkeyAvailability.RegisterAll(_ => false, () => 1409);

        Assert.Equal(
            "Ctrl+Shift+Space и Ctrl+Shift+K заняты другой программой. Освободите сочетание и перезапустите Voice Input.",
            availability.CreateConflictMessage());
    }

    [Fact]
    public void ToggleOnlyConflictNamesTheToggleHotkey()
    {
        var availability = HotkeyAvailability.RegisterAll(
            id => id == HotkeyAvailability.HoldHotkeyId,
            () => 1409);

        Assert.StartsWith("Ctrl+Shift+K занято", availability.CreateConflictMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationRequiresDelegates()
    {
        Assert.Throws<ArgumentNullException>(() => HotkeyAvailability.RegisterAll(null!, () => 0));
        Assert.Throws<ArgumentNullException>(() => HotkeyAvailability.RegisterAll(_ => true, null!));
    }
}
