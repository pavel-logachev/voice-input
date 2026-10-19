using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using VoiceInput.App;
using VoiceInput.Windows.Settings;
using VoiceInput.Windows.Transcription;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace VoiceInput.UiPreview;

/// <summary>Pictures of the engine section in every state, drawn from fakes: no real profile, key or download is touched.</summary>
internal static class EnginePreview
{
    private const long ModelSize = 183_948_704;

    /// <summary>1920 × 1080 at 150 %: the working area under a 48 px taskbar, minus the margin the window keeps.</summary>
    private const double SmallScreenHeight = (1080 / 1.5) - 32 - 24;

    private static readonly AppSettings Base = new() { Language = "ru", MicrophoneId = "b" };

    public static IEnumerable<string> CaptureAll(string directory)
    {
        var results = new List<string>();

        // The three engines, as the user first sees each of them.
        results.Add(Shot(directory, "engine-openai", Base with { Engine = TranscriptionEngine.OpenAi }));
        results.Add(Shot(directory, "engine-local", Base with { Engine = TranscriptionEngine.Local }));
        results.Add(Shot(directory, "engine-custom", Base with { Engine = TranscriptionEngine.CustomOpenAiCompatible }));

        // The same three on a 1920 × 1080 screen at 150 %: they either fit or scroll.
        foreach (var (name, engine) in new[]
        {
            ("openai", TranscriptionEngine.OpenAi),
            ("local", TranscriptionEngine.Local),
            ("custom", TranscriptionEngine.CustomOpenAiCompatible),
        })
        {
            results.Add(Shot(directory, $"engine-{name}-1080p150", Base with { Engine = engine }, maxHeight: SmallScreenHeight));
        }

        results.Add(Shot(directory, "engine-custom-1080p150-bottom", Base with { Engine = TranscriptionEngine.CustomOpenAiCompatible },
            maxHeight: SmallScreenHeight, prepare: window => window.Scroller.ScrollToEnd()));

        // Local model: every state of the card.
        var local = Base with { Engine = TranscriptionEngine.Local };
        var states = new (string Name, LocalModelProgress Progress)[]
        {
            ("notinstalled", new(LocalModelState.NotInstalled)),
            ("downloading", new(LocalModelState.Downloading, 73_400_000, ModelSize, 5_200_000)),
            ("paused", new(LocalModelState.Paused, 73_400_000, ModelSize)),
            ("verifying", new(LocalModelState.Verifying, ModelSize, ModelSize)),
            ("ready", new(LocalModelState.Ready, ModelSize, ModelSize)),
            ("failed-network", new(LocalModelState.Failed, FailureReason: ModelFailureReason.Network)),
            ("failed-diskspace", new(LocalModelState.Failed, 0, ModelSize, FailureReason: ModelFailureReason.InsufficientDiskSpace)),
            ("failed-hashmismatch", new(LocalModelState.Failed, FailureReason: ModelFailureReason.HashMismatch)),
            ("failed-fileaccess", new(LocalModelState.Failed, FailureReason: ModelFailureReason.FileAccess)),
            ("failed-invalidresponse", new(LocalModelState.Failed, FailureReason: ModelFailureReason.InvalidResponse)),
        };
        foreach (var (name, progress) in states)
        {
            results.Add(Shot(directory, "local-" + name, local, new FakeLocalModel { Progress = progress }));
        }

        var ready = new LocalModelProgress(LocalModelState.Ready, ModelSize, ModelSize);
        results.Add(Shot(directory, "local-delete-confirm", local, new FakeLocalModel { Progress = ready },
            prepare: window => window.ShowDeleteConfirmation()));
        results.Add(Shot(directory, "local-delete-confirm-1080p150", local, new FakeLocalModel { Progress = ready },
            maxHeight: SmallScreenHeight, prepare: window => window.ShowDeleteConfirmation()));
        results.Add(Shot(directory, "local-delete-inuse", local, new FakeLocalModel { Progress = ready, DeleteResult = ModelDeleteResult.InUse },
            prepare: ConfirmDeletion));
        results.Add(Shot(directory, "local-delete-fileaccess", local, new FakeLocalModel { Progress = ready, DeleteResult = ModelDeleteResult.FileAccess },
            prepare: ConfirmDeletion));
        results.Add(Shot(directory, "local-delete-done", local, new FakeLocalModel { Progress = ready, DeleteResult = ModelDeleteResult.Deleted },
            prepare: ConfirmDeletion));

        // Own server: empty, wrong values, every check result, and the key.
        var custom = Base with { Engine = TranscriptionEngine.CustomOpenAiCompatible };
        var filled = custom with { CustomBaseUrl = "https://api.groq.com/openai/v1", CustomModel = "whisper-large-v3-turbo" };
        results.Add(Shot(directory, "custom-empty-errors", custom, prepare: window => _ = window.RunCustomCheckAsync()));
        results.Add(Shot(directory, "custom-url-invalid", custom with { CustomModel = "whisper-1" }, prepare: window =>
        {
            window.CustomUrlBox.Text = "api.groq.com/openai";
            _ = window.RunCustomCheckAsync();
        }));
        results.Add(Shot(directory, "custom-url-http", custom with { CustomModel = "whisper-1" }, prepare: window =>
        {
            window.CustomUrlBox.Text = "http://speech.example.com/v1";
            _ = window.RunCustomCheckAsync();
        }));
        results.Add(Shot(directory, "custom-filled-lan-http", custom with { CustomBaseUrl = "http://192.168.1.20:8000/v1", CustomModel = "Systran/faster-whisper-large-v3" }));

        var checks = new (string Name, CustomProviderValidationResult? Result)[]
        {
            ("running", null),
            ("modelverified", new(CustomProviderValidationStatus.ModelVerified)),
            ("notverified", new(CustomProviderValidationStatus.ReachableModelNotVerified)),
            ("unauthorized", new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.Unauthorized)),
            ("network", new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.Network)),
            ("timeout", new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.Timeout)),
            ("serviceunavailable", new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.ServiceUnavailable)),
            ("invalidresponse", new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.InvalidResponse)),
            ("ratelimited", new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.RateLimited)),
            ("invalidrequest", new(CustomProviderValidationStatus.Failed, TranscriptionErrorCode.InvalidRequest)),
        };
        foreach (var (name, result) in checks)
        {
            var server = new FakeCustomServer { KeyState = CustomKeyState.Saved, Result = result };
            results.Add(Shot(directory, "custom-check-" + name, filled, customServer: server, prepare: window => _ = window.RunCustomCheckAsync()));
        }

        results.Add(Shot(directory, "custom-key-saved", filled, customServer: new FakeCustomServer { KeyState = CustomKeyState.Saved }));
        results.Add(Shot(directory, "custom-key-none", filled, customServer: new FakeCustomServer { KeyState = CustomKeyState.None }));
        results.Add(Shot(directory, "custom-key-unreadable", filled, customServer: new FakeCustomServer { KeyState = CustomKeyState.Unreadable }));
        results.Add(Shot(directory, "custom-key-typed", filled, customServer: new FakeCustomServer { KeyState = CustomKeyState.None },
            prepare: window => window.CustomKeyBox.Password = "gsk_preview_key"));

        results.AddRange(CaptureFocus(directory));
        return results;
    }

    /// <summary>The real download path: model manager, settings services and window against a local HTTP server with a small file.</summary>
    public static IEnumerable<string> CaptureLiveDownload(string directory)
    {
        var results = new List<string>();
        var data = Path.Combine(directory, "live-data");
        if (Directory.Exists(data))
        {
            Directory.Delete(data, recursive: true);
        }

        var payload = new byte[24_000_000];
        new Random(12).NextBytes(payload);
        using var server = new ThrottledFileServer(payload, bytesPerSecond: 6_000_000);
        var definition = new LocalModelDefinition(
            LocalModelVariant.GigaAmV3E2eRnntQ4,
            "preview-model.gguf",
            server.Uri,
            Convert.ToHexStringLower(SHA256.HashData(payload)),
            payload.Length);
        var manager = new LocalModelManager(data, new HttpClient(), _ => long.MaxValue, definition);
        var services = new EngineSettingsServices(manager, new DpapiCustomApiKeyProvider(Path.Combine(data, "custom.key")));
        var host = new Program.FakeHost(Base with { Engine = TranscriptionEngine.Local }, hasKey: true, localModel: services);
        var window = Open(host);
        var log = new StringBuilder();
        void Note(string line)
        {
            log.AppendLine(line);
            Console.WriteLine("live: " + line);
        }

        Note($"start state={services.Progress.State}");
        results.Add(Program.CaptureWindow(window, directory, "live-1-notinstalled"));
        Click(window.LocalActionButton);
        WaitFor(window, () => services.Progress is { State: LocalModelState.Downloading, Bytes: > 6_000_000 }, TimeSpan.FromSeconds(20));
        Note($"downloading bytes={services.Progress.Bytes} speed={services.Progress.BytesPerSecond:0} text='{window.LocalState.Text}' button='{window.LocalActionButton.Content}'");
        results.Add(Program.CaptureWindow(window, directory, "live-2-downloading"));
        Click(window.LocalActionButton);
        WaitFor(window, () => services.Progress.State == LocalModelState.Paused, TimeSpan.FromSeconds(10));
        var partial = new FileInfo(manager.TemporaryPath).Length;
        Note($"paused bytes={services.Progress.Bytes} partial-file={partial} text='{window.LocalState.Text}' button='{window.LocalActionButton.Content}'");
        results.Add(Program.CaptureWindow(window, directory, "live-3-paused"));
        Click(window.LocalActionButton);
        WaitFor(window, () => services.Progress.State == LocalModelState.Ready, TimeSpan.FromSeconds(30));
        Note($"ready file={File.Exists(manager.ModelPath)} ranges='{string.Join(",", server.Ranges)}' text='{window.LocalState.Text}' button='{window.LocalActionButton.Content}'");
        results.Add(Program.CaptureWindow(window, directory, "live-4-ready"));
        Click(window.LocalActionButton);
        Program.PumpFor(window, TimeSpan.FromMilliseconds(200));
        Note($"confirm visible={window.ConfirmLayer.Visibility} focus={System.Windows.Input.Keyboard.FocusedElement?.GetType().Name}");
        Click(window.ConfirmDeleteButton);
        WaitFor(window, () => services.Progress.State == LocalModelState.NotInstalled, TimeSpan.FromSeconds(10));
        Note($"deleted file={File.Exists(manager.ModelPath)} text='{window.LocalState.Text}' button='{window.LocalActionButton.Content}'");
        results.Add(Program.CaptureWindow(window, directory, "live-5-deleted"));
        window.Close();
        Task.Run(async () => await services.DisposeAsync()).GetAwaiter().GetResult();

        // A file that fails its check after the download.
        using var corrupt = new ThrottledFileServer(payload, bytesPerSecond: 40_000_000);
        var wrongHash = definition with { DownloadUri = corrupt.Uri, Sha256 = new string('0', 64) };
        var badManager = new LocalModelManager(Path.Combine(data, "bad"), new HttpClient(), _ => long.MaxValue, wrongHash);
        var badServices = new EngineSettingsServices(badManager, new DpapiCustomApiKeyProvider(Path.Combine(data, "custom.key")));
        var badWindow = Open(new Program.FakeHost(Base with { Engine = TranscriptionEngine.Local }, hasKey: true, localModel: badServices));
        Click(badWindow.LocalActionButton);
        WaitFor(badWindow, () => badServices.Progress.State == LocalModelState.Failed, TimeSpan.FromSeconds(30));
        Note($"corrupt state={badServices.Progress.State} reason={badServices.Progress.FailureReason} text='{badWindow.LocalState.Text}' button='{badWindow.LocalActionButton.Content}'");
        results.Add(Program.CaptureWindow(badWindow, directory, "live-6-hashmismatch"));
        badWindow.Close();
        Task.Run(async () => await badServices.DisposeAsync()).GetAwaiter().GetResult();

        // A server that cannot be reached.
        var offline = definition with { DownloadUri = new Uri("http://127.0.0.1:9/model.gguf") };
        var offlineManager = new LocalModelManager(Path.Combine(data, "offline"), new HttpClient(), _ => long.MaxValue, offline);
        var offlineServices = new EngineSettingsServices(offlineManager, new DpapiCustomApiKeyProvider(Path.Combine(data, "custom.key")));
        var offlineWindow = Open(new Program.FakeHost(Base with { Engine = TranscriptionEngine.Local }, hasKey: true, localModel: offlineServices));
        Click(offlineWindow.LocalActionButton);
        WaitFor(offlineWindow, () => offlineServices.Progress.State == LocalModelState.Failed, TimeSpan.FromSeconds(30));
        Note($"offline state={offlineServices.Progress.State} reason={offlineServices.Progress.FailureReason} text='{offlineWindow.LocalState.Text}'");
        results.Add(Program.CaptureWindow(offlineWindow, directory, "live-7-network"));
        offlineWindow.Close();
        Task.Run(async () => await offlineServices.DisposeAsync()).GetAwaiter().GetResult();

        File.WriteAllText(Path.Combine(directory, "live-download.log"), log.ToString());
        return results;
    }

    private static List<string> CaptureFocus(string directory)
    {
        var results = new List<string>();
        var custom = Base with { Engine = TranscriptionEngine.CustomOpenAiCompatible, CustomBaseUrl = "https://api.groq.com/openai/v1", CustomModel = "whisper-large-v3-turbo" };
        var local = Base with { Engine = TranscriptionEngine.Local };
        var notInstalled = new FakeLocalModel { Progress = new(LocalModelState.NotInstalled) };
        results.Add(Shot(directory, "focus-engine-option", local, notInstalled, prepare: window => window.LocalRadio.Focus()));
        results.Add(Shot(directory, "focus-local-action", local, new FakeLocalModel { Progress = new(LocalModelState.NotInstalled) },
            prepare: window => window.LocalActionButton.Focus()));
        results.Add(Shot(directory, "focus-delete-cancel", local, new FakeLocalModel { Progress = new(LocalModelState.Ready, ModelSize, ModelSize) },
            prepare: window => window.ShowDeleteConfirmation()));
        results.Add(Shot(directory, "focus-custom-url", custom, prepare: window => window.CustomUrlBox.Focus()));
        results.Add(Shot(directory, "focus-custom-key", custom, prepare: window => window.CustomKeyBox.Focus()));
        results.Add(Shot(directory, "focus-custom-check", custom, prepare: window => window.CustomCheckButton.Focus()));
        return results;
    }

    private static void ConfirmDeletion(SettingsWindow window)
    {
        window.ShowDeleteConfirmation();
        Program.PumpFor(window, TimeSpan.FromMilliseconds(80));
        Click(window.ConfirmDeleteButton);
    }

    private static string Shot(
        string directory,
        string name,
        AppSettings settings,
        FakeLocalModel? localModel = null,
        FakeCustomServer? customServer = null,
        double? maxHeight = null,
        Action<SettingsWindow>? prepare = null)
    {
        var host = new Program.FakeHost(settings, hasKey: true, localModel: localModel, customServer: customServer);
        var window = Open(host, maxHeight);
        if (prepare is not null)
        {
            prepare(window);
            Program.PumpFor(window, TimeSpan.FromMilliseconds(120));
        }

        var path = Program.CaptureWindow(window, directory, name);
        var content = window.ContentPanel;
        Console.WriteLine($"size {name}: window-content={((FrameworkElement)VisualTreeHelper.GetChild(window, 0)).ActualHeight:0} panel={content.ActualHeight + content.Margin.Top + content.Margin.Bottom:0} scrollable={window.Scroller.ScrollableHeight:0}");
        window.Close();
        return path;
    }

    private static SettingsWindow Open(Program.FakeHost host, double? maxHeight = null)
    {
        var window = new SettingsWindow(host) { Left = -10_000, Top = -10_000, WindowStartupLocation = WindowStartupLocation.Manual };
        if (maxHeight is { } limit)
        {
            // The client area of a framed window: the title bar takes about 32 DIP of the height.
            window.MaxHeight = limit;
        }

        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static void Click(ButtonBase button)
    {
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static void WaitFor(Window window, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The preview state did not arrive in time.");
            }

            Program.PumpFor(window, TimeSpan.FromMilliseconds(50));
        }

        Program.PumpFor(window, TimeSpan.FromMilliseconds(150));
    }

    internal sealed class FakeLocalModel : ILocalModelHost
    {
        public event EventHandler<LocalModelProgress>? ProgressChanged;

        public LocalModelProgress Progress { get; set; } = new(LocalModelState.NotInstalled);

        public long ModelSizeBytes => ModelSize;

        public ModelDeleteResult DeleteResult { get; init; } = ModelDeleteResult.Deleted;

        public void Start() => Publish(new(LocalModelState.Downloading, 0, ModelSize));

        public void Pause() => Publish(Progress with { State = LocalModelState.Paused });

        public Task<ModelDeleteResult> DeleteAsync()
        {
            if (DeleteResult == ModelDeleteResult.Deleted)
            {
                Publish(new(LocalModelState.NotInstalled));
            }

            return Task.FromResult(DeleteResult);
        }

        private void Publish(LocalModelProgress next)
        {
            Progress = next;
            ProgressChanged?.Invoke(this, next);
        }
    }

    internal sealed class FakeCustomServer : ICustomServerHost
    {
        public CustomKeyState KeyState { get; set; } = CustomKeyState.None;

        /// <summary>Null keeps the check running forever, for the picture of the running state.</summary>
        public CustomProviderValidationResult? Result { get; init; } = new(CustomProviderValidationStatus.ModelVerified);

        public string? SaveKey(string apiKey)
        {
            KeyState = CustomKeyState.Saved;
            return null;
        }

        public void DeleteKey() => KeyState = CustomKeyState.None;

        public Task<CustomProviderValidationResult> CheckAsync(string baseUrl, string model, string? typedKey) =>
            Result is null ? new TaskCompletionSource<CustomProviderValidationResult>().Task : Task.FromResult(Result);
    }

    /// <summary>A minimal HTTP/1.1 file server on the loopback address, with Range support and a speed limit.</summary>
    private sealed class ThrottledFileServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly byte[] payload;
        private readonly int bytesPerSecond;
        private readonly CancellationTokenSource stop = new();

        public ThrottledFileServer(byte[] payload, int bytesPerSecond)
        {
            this.payload = payload;
            this.bytesPerSecond = bytesPerSecond;
            listener.Start();
            Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/model.gguf");
            _ = Task.Run(AcceptAsync);
        }

        public Uri Uri { get; }

        public List<string> Ranges { get; } = [];

        public void Dispose()
        {
            stop.Cancel();
            listener.Stop();
            stop.Dispose();
        }

        private async Task AcceptAsync()
        {
            while (!stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stop.Token);
                }
                catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    long offset = 0;
                    string? line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
                    {
                        if (line.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                        {
                            offset = long.Parse(line["Range: bytes=".Length..].TrimEnd('-').Split('-')[0], System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }

                    lock (Ranges)
                    {
                        Ranges.Add(offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    var length = payload.Length - offset;
                    var head = offset > 0
                        ? $"HTTP/1.1 206 Partial Content\r\nContent-Length: {length}\r\nContent-Range: bytes {offset}-{payload.Length - 1}/{payload.Length}\r\nConnection: close\r\n\r\n"
                        : $"HTTP/1.1 200 OK\r\nContent-Length: {length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                    const int chunk = 64 * 1024;
                    var delay = TimeSpan.FromSeconds(chunk / (double)bytesPerSecond);
                    for (var position = offset; position < payload.Length; position += chunk)
                    {
                        var count = (int)Math.Min(chunk, payload.Length - position);
                        await stream.WriteAsync(payload.AsMemory((int)position, count), stop.Token);
                        await Task.Delay(delay, stop.Token);
                    }
                }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    // The client paused or went away.
                }
            }
        }
    }
}
