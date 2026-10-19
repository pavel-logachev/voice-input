using System.Windows;
using System.Windows.Threading;
using VoiceInput.App;
using VoiceInput.Windows.Audio;
using VoiceInput.Windows.Settings;
using VoiceInput.Windows.Transcription;

namespace VoiceInput.Windows.Tests;

public sealed class CustomCheckUiRegressionTests
{
    private static readonly string[] Fields = ["url", "model", "key"];
    [STAThread]
    private static void Main()
    {
        Task.Run(EditingCore).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task EditingAnyCustomInputInvalidatesPendingResultAndInvalidKeyIsShownUnderKey()
    {
        // A separate STA process gives WPF its actual resource entry assembly, not testhost.dll.
        var start = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(CustomCheckUiRegressionTests).Assembly.Location);
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, process.ExitCode);
    }

    private static async Task EditingCore()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = new ResourceDictionary() };
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                foreach (var field in Fields)
                {
                    var host = new Host();
                    var window = new SettingsWindow(host);
                    var check = window.RunCustomCheckAsync();
                    if (field == "url") window.CustomUrlBox.Text = "http://localhost:8002/v1";
                    if (field == "model") window.CustomModelBox.Text = "other-model";
                    if (field == "key") window.CustomKeyBox.Password = "new-key";
                    host.Server.Pending.SetResult(new(CustomProviderValidationStatus.ModelVerified));
                    Pump(check);
                    Assert.Equal(string.Empty, window.CustomCheckResult.Text);
                    Assert.True(host.Server.CancelCalls > 0);
                    window.Close();
                }
                var keyHost = new Host();
                var keyWindow = new SettingsWindow(keyHost);
                keyWindow.CustomKeyBox.Password = "bad\nkey";
                Pump(keyWindow.RunCustomCheckAsync());
                Assert.Equal("В ключе недопустимые символы, например перевод строки. Вставьте ключ одной строкой.", keyWindow.CustomKeyNote.Text);
                Assert.Equal(0, keyHost.Server.Calls);
                keyWindow.Close();
                for (var action = 0; action < 3; action++)
                {
                    var save = action != 0;
                    var openAiHost = new Host { InitialEngine = TranscriptionEngine.OpenAi, SaveThrows = action == 2 };
                    var openAi = new SettingsWindow(openAiHost);
                    if (save) openAi.KeyBox.Password = "sk-test";
                    (save ? openAi.SaveKeyButton : openAi.CheckKeyButton).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Pump(Task.Delay(80));
                    Assert.Equal(action == 2 ? "Не удалось сохранить ключ. Проверьте доступ к папке профиля." : "Не удалось прочитать защищенный API-ключ OpenAI.", openAi.KeyResult.Text);
                    Assert.Equal(Visibility.Visible, openAi.KeyResult.Visibility);
                    Assert.Equal(string.Empty, openAi.CustomCheckResult.Text);
                    Assert.True(openAi.CheckKeyButton.IsEnabled);
                    openAi.Close();
                }
                var shared = new SettingsWindow(new Host { InitialEngine = TranscriptionEngine.OpenAi, ApplyThrows = true });
                shared.AutostartBox.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(Task.Delay(80));
                Assert.Equal(Visibility.Visible, shared.Message.Visibility);
                Assert.Equal(string.Empty, shared.CustomCheckResult.Text);
                Assert.True(shared.IsEnabled);
                shared.Close();
                var local = new SettingsWindow(new Host { InitialEngine = TranscriptionEngine.Local, DeleteThrows = true });
                local.ConfirmDeleteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(Task.Delay(80));
                Assert.Equal("Нет доступа к файлу модели на диске.", local.LocalState.Text);
                Assert.Equal(string.Empty, local.CustomCheckResult.Text);
                Assert.True(local.LocalActionButton.IsEnabled);
                local.Close();
                completion.SetResult();
            }
            catch (Exception exception) { completion.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) => { if (task.IsCompleted) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        task.GetAwaiter().GetResult();
    }
    private sealed class Host : ISettingsHost
    {
        private AppSettings current = new() { Engine = TranscriptionEngine.CustomOpenAiCompatible, CustomBaseUrl = "http://localhost:8001/v1", CustomModel = "model-a" };
        public TranscriptionEngine InitialEngine { init => current = current with { Engine = value }; }
        public AppSettings Current => current;
        public bool DeleteThrows { init => model.DeleteThrows = value; }
        public bool HasApiKey => true;
        public bool LocalEngineAvailable => true;
        public string Version => "test";
        public IReadOnlyList<MicrophoneChoice> Microphones => [];
        private readonly Model model = new();
        public ILocalModelHost LocalModel => model;
        public Server Server { get; } = new();
        public ICustomServerHost CustomServer => Server;
        public bool ApplyThrows { get; init; }
        public Task<string?> ApplyAsync(AppSettings next) { if (ApplyThrows) throw new IOException("settings unavailable"); current = next; return Task.FromResult<string?>(null); }
        public bool SaveThrows { get; init; }
        public string? SaveApiKey(string apiKey) => SaveThrows ? throw new IOException("disk unavailable") : null;
        public void DeleteApiKey() { }
        public async Task<OpenAiKeyCheckResult> CheckApiKeyAsync(string? candidate) { await Task.Delay(10); throw new FormatException("invalid header"); }
    }
    private sealed class Model : ILocalModelHost
    {
        public LocalModelProgress Progress => new(LocalModelState.NotInstalled);
        public long ModelSizeBytes => 1;
        public event EventHandler<LocalModelProgress>? ProgressChanged { add { } remove { } }
        public void Start() { }
        public void Pause() { }
        public bool DeleteThrows { get; set; }
        public async Task<ModelDeleteResult> DeleteAsync() { await Task.Delay(10); if (DeleteThrows) throw new IOException("file locked"); return ModelDeleteResult.Deleted; }
    }
    private sealed class Server : ICustomServerHost
    {
        public TaskCompletionSource<CustomProviderValidationResult> Pending { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public int CancelCalls { get; private set; }
        public void CancelCheck() => CancelCalls++;
        public CustomKeyState KeyState => CustomKeyState.None;
        public string? SaveKey(string apiKey) => null;
        public void DeleteKey() { }
        public Task<CustomProviderValidationResult> CheckAsync(string baseUrl, string model, string? typedKey) { Calls++; return Pending.Task; }
    }
}
