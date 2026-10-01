using VoiceInput.Windows.Settings;

namespace VoiceInput.Windows.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "voiceinput-settings-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var settings = new SettingsStore(FilePath).Load();

        Assert.Equal(TranscriptionEngine.OpenAi, settings.Engine);
        Assert.False(settings.StartWithWindows);
        Assert.Null(settings.Language);
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var store = new SettingsStore(FilePath);

        store.Save(new AppSettings { Engine = TranscriptionEngine.Local, StartWithWindows = true, Language = "ru" });
        var loaded = store.Load();

        Assert.Equal(TranscriptionEngine.Local, loaded.Engine);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal("ru", loaded.Language);
        Assert.Contains("\"Local\"", File.ReadAllText(FilePath), StringComparison.Ordinal);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    public void DamagedFileFallsBackToDefaultsInsteadOfFailing(string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, content);

        var settings = new SettingsStore(FilePath).Load();

        Assert.Equal(TranscriptionEngine.OpenAi, settings.Engine);
    }

    [Fact]
    public void UnknownEngineAndOddLanguageAreNormalized()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, "{\"Engine\":\"Quantum\",\"Language\":\"  Russian  \"}");

        var settings = new SettingsStore(FilePath).Load();

        Assert.Equal(TranscriptionEngine.OpenAi, settings.Engine);
        Assert.Null(settings.Language);
    }

    [Fact]
    public void LanguageHintIsTrimmedAndLowercased()
    {
        var store = new SettingsStore(FilePath);

        store.Save(new AppSettings { Language = " RU " });

        Assert.Equal("ru", store.Load().Language);
    }

    [Fact]
    public void SaveOverwritesThePreviousFile()
    {
        var store = new SettingsStore(FilePath);
        store.Save(new AppSettings { Engine = TranscriptionEngine.Local });

        store.Save(new AppSettings { Engine = TranscriptionEngine.OpenAi });

        Assert.Equal(TranscriptionEngine.OpenAi, store.Load().Engine);
    }
}

public sealed class AutostartRegistrationTests
{
    private const string Executable = @"C:\Program Files\Voice Input\VoiceInput.App.exe";

    [Fact]
    public void EnablingWritesAQuotedCommand()
    {
        var store = new FakeStore();
        var registration = new AutostartRegistration(store, Executable);

        registration.Set(true);

        Assert.Equal($"\"{Executable}\"", store.Values[AutostartRegistration.EntryName]);
        Assert.True(registration.IsEnabled);
    }

    [Fact]
    public void DisablingRemovesTheEntry()
    {
        var store = new FakeStore();
        var registration = new AutostartRegistration(store, Executable);
        registration.Set(true);

        registration.Set(false);

        Assert.False(registration.IsEnabled);
        Assert.Empty(store.Values);
    }

    [Fact]
    public void AnEntryPointingToAnotherCopyDoesNotCountAsEnabled()
    {
        var store = new FakeStore();
        store.Values[AutostartRegistration.EntryName] = "\"D:\\Old\\VoiceInput.App.exe\"";

        Assert.False(new AutostartRegistration(store, Executable).IsEnabled);
    }

    [Fact]
    public void ComparisonIgnoresCaseAndQuotes()
    {
        var store = new FakeStore();
        store.Values[AutostartRegistration.EntryName] = Executable.ToUpperInvariant();

        Assert.True(new AutostartRegistration(store, Executable).IsEnabled);
    }

    [Fact]
    public void UnknownExecutablePathCannotBeRegistered()
    {
        var registration = new AutostartRegistration(new FakeStore(), string.Empty);

        Assert.Throws<InvalidOperationException>(() => registration.Set(true));
    }

    private sealed class FakeStore : IAutostartStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public string? Read(string name) => Values.GetValueOrDefault(name);

        public void Write(string name, string command) => Values[name] = command;

        public void Remove(string name) => Values.Remove(name);
    }
}
