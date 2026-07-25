using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using VoiceInput.Core.Activation;
using VoiceInput.Windows.Hotkeys;
using VoiceInput.Windows.Input;
using VoiceInput.Windows.Targeting;

namespace VoiceInput.App;

public partial class App : System.Windows.Application, IDisposable
{
    private const string TestPhrase = "Диктовка работает.";

    private readonly CancellationTokenSource lifetime = new();
    private MainWindow? overlay;
    private GlobalHotkeyRegistration? hotkey;
    private Forms.NotifyIcon? trayIcon;
    private FakeDictationWorkflow? workflow;
    private bool disposed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        overlay = new MainWindow();
        workflow = new FakeDictationWorkflow(
            new ForegroundTargetCapture(),
            overlay,
            new ModifierReleaseGate(),
            new WindowsUnicodeTextInserter(),
            new SystemAsyncDelay());

        hotkey = new GlobalHotkeyRegistration();
        hotkey.Activated += OnHotkeyActivated;

        trayIcon = BuildTrayIcon();
        trayIcon.Visible = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetime.Cancel();

        if (hotkey is not null)
        {
            hotkey.Activated -= OnHotkeyActivated;
            hotkey.Dispose();
            hotkey = null;
        }

        if (trayIcon is not null)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        overlay?.Close();
        overlay = null;
        lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private Forms.NotifyIcon BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Ctrl + Shift + Space — тестовая вставка")
        {
            Enabled = false,
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        var exitItem = new Forms.ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        return new Forms.NotifyIcon
        {
            Text = "Voice Input — Ctrl+Shift+Space",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
        };
    }

    private async void OnHotkeyActivated(object? sender, EventArgs e)
    {
        if (workflow is null || overlay is null)
        {
            return;
        }

        try
        {
            await workflow.TryActivateAsync(TestPhrase, lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (TargetFocusChangedException)
        {
            await ShowTransientErrorAsync("Активное окно изменилось — вставка отменена");
        }
        catch (Exception exception)
        {
            await ShowTransientErrorAsync(exception.Message);
        }
    }

    private async Task ShowTransientErrorAsync(string message)
    {
        if (overlay is null || lifetime.IsCancellationRequested)
        {
            return;
        }

        overlay.ShowError(message);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            overlay.Hide();
        }
    }
}
