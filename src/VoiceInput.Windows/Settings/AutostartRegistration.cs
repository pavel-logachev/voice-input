using Microsoft.Win32;

namespace VoiceInput.Windows.Settings;

/// <summary>Where the "start with Windows" entry lives. Abstracted so the rules can be tested without the registry.</summary>
public interface IAutostartStore
{
    string? Read(string name);

    void Write(string name, string command);

    void Remove(string name);
}

public sealed class RegistryAutostartStore : IAutostartStore
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(name) as string;
    }

    public void Write(string name, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(name, command, RegistryValueKind.String);
    }

    public void Remove(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

public sealed class AutostartRegistration(IAutostartStore store, string executablePath)
{
    // The same value name the installer writes, so installer and settings manage one entry.
    public const string EntryName = "Voice Input";

    public bool IsEnabled
    {
        get
        {
            var current = store.Read(EntryName);
            return current is not null && string.Equals(Unquote(current), executablePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Set(bool enabled)
    {
        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("Не удалось определить путь к программе для автозапуска.");
            }

            // Quoted, so a path with spaces ("Program Files", user names) starts reliably.
            store.Write(EntryName, $"\"{executablePath}\"");
        }
        else
        {
            store.Remove(EntryName);
        }
    }

    private static string Unquote(string command) => command.Trim().Trim('"');
}
