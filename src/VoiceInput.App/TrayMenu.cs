using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VoiceInput.Windows.Audio;

namespace VoiceInput.App;

/// <summary>What the first line of the tray menu reports.</summary>
internal enum TrayStatus
{
    /// <summary>Starting or switching: wait a moment.</summary>
    Busy,

    /// <summary>Dictation is available.</summary>
    Ready,

    /// <summary>The user has to do something first, usually to save an API key.</summary>
    Attention,
}

/// <summary>
/// The tray menu: its items and their wording. The application wires the actions,
/// so the same menu can be drawn by the UI preview tool without starting a dictation engine.
/// </summary>
internal sealed class TrayMenu : IDisposable
{
    private readonly TrayMenuRenderer? renderer;
    private readonly TrayStatusItem status;

    public TrayMenu(bool localEngineAvailable, float? scaleOverride = null)
    {
        Strip = new ContextMenuStrip();
        renderer = TrayMenuRenderer.Apply(Strip, scaleOverride);

        status = new TrayStatusItem(renderer, "Запускаю распознавание…")
        {
            Font = new Font(Strip.Font, FontStyle.Bold),
        };
        Add(Strip.Items, status);
        Strip.Items.Add(new ToolStripSeparator());

        Engine = new ToolStripMenuItem("Распознавание");
        OpenAi = new ToolStripMenuItem("OpenAI, через интернет");
        Local = new ToolStripMenuItem("На этом компьютере (GigaAM)");
        Add(Engine.DropDownItems, OpenAi);
        Add(Engine.DropDownItems, Local);
        Engine.Visible = localEngineAvailable;
        Add(Strip.Items, Engine);

        Microphone = new ToolStripMenuItem("Микрофон");
        Add(Microphone.DropDownItems, new ToolStripMenuItem("Загрузка…") { Enabled = false });
        Add(Strip.Items, Microphone);

        Settings = new ToolStripMenuItem("Настройки…");
        Add(Strip.Items, Settings);

        // Information, not commands: the rows are disabled, the shortcut sits in its own column.
        Hotkeys = new ToolStripMenuItem("Горячие клавиши");
        Add(Hotkeys.DropDownItems, new ToolStripMenuItem("Удерживать для записи") { ShortcutKeyDisplayString = "Ctrl + Shift + Space", Enabled = false });
        Add(Hotkeys.DropDownItems, new ToolStripMenuItem("Начать или завершить") { ShortcutKeyDisplayString = "Ctrl + Shift + K", Enabled = false });
        Add(Hotkeys.DropDownItems, new ToolStripMenuItem("Отменить диктовку") { ShortcutKeyDisplayString = "Esc", Enabled = false });
        Add(Strip.Items, Hotkeys);
        Strip.Items.Add(new ToolStripSeparator());

        Exit = new ToolStripMenuItem("Закрыть Voice Input");
        Add(Strip.Items, Exit);

        renderer?.Style(Engine.DropDown, checkMargin: true);
        renderer?.Style(Microphone.DropDown, checkMargin: true);
        renderer?.Style(Hotkeys.DropDown, checkMargin: false);
    }

    public ContextMenuStrip Strip { get; }

    public ToolStripMenuItem Engine { get; }

    public ToolStripMenuItem OpenAi { get; }

    public ToolStripMenuItem Local { get; }

    public ToolStripMenuItem Microphone { get; }

    public ToolStripMenuItem Settings { get; }

    public ToolStripMenuItem Hotkeys { get; }

    public ToolStripMenuItem Exit { get; }

    public void SetStatus(string text, TrayStatus kind)
    {
        status.Show(text, kind);
    }

    /// <summary>Rebuilds the microphone submenu; <paramref name="choose"/> receives the device id, or null for the Windows default.</summary>
    public void FillMicrophones(
        IEnumerable<MicrophoneChoice> microphones,
        string? currentId,
        Action<string?> choose)
    {
        Microphone.DropDownItems.Clear();
        var defaultItem = new ToolStripMenuItem("По умолчанию в Windows")
        {
            Checked = string.IsNullOrWhiteSpace(currentId),
        };
        defaultItem.Click += (_, _) => choose(null);
        Add(Microphone.DropDownItems, defaultItem);

        foreach (var microphone in microphones)
        {
            var item = new ToolStripMenuItem(microphone.Name)
            {
                Checked = string.Equals(microphone.Id, currentId, StringComparison.Ordinal),
            };
            var id = microphone.Id;
            item.Click += (_, _) => choose(id);
            Add(Microphone.DropDownItems, item);
        }
    }

    public void Dispose() => Strip.Dispose();

    private void Add(ToolStripItemCollection items, ToolStripItem item)
    {
        renderer?.Style(item);
        items.Add(item);
    }

    /// <summary>The first line of the menu: a coloured dot and what the application is doing.</summary>
    private sealed class TrayStatusItem(TrayMenuRenderer? renderer, string text) : ToolStripLabel(text)
    {
        private TrayStatus kind;

        public void Show(string statusText, TrayStatus statusKind)
        {
            kind = statusKind;
            Text = statusText;
            Invalidate();
        }

        private int DotSize => renderer?.Px(7) ?? 0;

        private int DotGap => renderer?.Px(8) ?? 0;

        public override Size GetPreferredSize(Size constrainingSize)
        {
            var size = base.GetPreferredSize(constrainingSize);
            return new Size(size.Width + DotSize + DotGap, size.Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (renderer is null)
            {
                base.OnPaint(e);
                return;
            }

            var palette = renderer.Palette;
            var colour = kind switch
            {
                TrayStatus.Ready => palette.Success,
                TrayStatus.Attention => palette.Warning,
                _ => palette.Neutral,
            };
            var left = Padding.Left + renderer.Px(2);
            using var brush = new SolidBrush(Theme.ToDrawing(colour));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillEllipse(brush, left, (Height - DotSize) / 2f, DotSize, DotSize);

            var textLeft = left + DotSize + DotGap;
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                new Rectangle(textLeft, 0, Width - textLeft, Height),
                renderer.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }
}
