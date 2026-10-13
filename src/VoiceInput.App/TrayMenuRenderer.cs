using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoiceInput.App;

/// <summary>
/// Draws the tray menu and its submenus in the colours of the overlay and the settings window.
/// High-contrast mode keeps the system look.
/// </summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmBorderColor = 34;
    private const int DwmCornerRound = 2;

    private readonly UiPalette palette;
    private readonly float scale;
    private readonly HashSet<ToolStrip> roundedBySystem = [];

    private TrayMenuRenderer(UiPalette palette, float scale)
        : base(new TrayColorTable(palette))
    {
        this.palette = palette;
        this.scale = scale;
        RoundedEdges = false;
    }

    public UiPalette Palette => palette;

    public Color Text => Theme.ToDrawing(palette.Text);

    public Color MutedText => Theme.ToDrawing(palette.TextMuted);

    /// <summary>Styles the menu; returns null in high-contrast mode, where the system draws it.</summary>
    public static TrayMenuRenderer? Apply(ContextMenuStrip menu, float? scaleOverride = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        var palette = Theme.Current;
        if (palette.HighContrast)
        {
            return null;
        }

        var deviceScale = menu.DeviceDpi / 96f;
        var renderer = new TrayMenuRenderer(palette, scaleOverride ?? deviceScale);
        // A font in points already follows the system scale; only an overridden scale needs a correction.
        menu.Font = new Font(FontFamilyName(), 9.25f * renderer.scale / deviceScale, FontStyle.Regular, GraphicsUnit.Point);
        renderer.Style(menu, checkMargin: false);
        return renderer;
    }

    /// <summary>Applies the look to one drop-down: the menu itself or a submenu.</summary>
    public void Style(ToolStripDropDown dropDown, bool checkMargin)
    {
        dropDown.Renderer = this;
        dropDown.BackColor = Theme.ToDrawing(palette.MenuSurface);
        dropDown.ForeColor = Text;
        dropDown.Padding = new Padding(Px(5));
        // Windows 11 rounds the corners and casts its own shadow; the classic rectangular one would stick out.
        dropDown.DropShadowEnabled = !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
        if (dropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = checkMargin;
        }

        dropDown.HandleCreated += (_, _) => RoundCorners(dropDown);
    }

    /// <summary>Gives an item the menu's row height.</summary>
    public void Style(ToolStripItem item)
    {
        item.Padding = new Padding(Px(6), Px(5), Px(6), Px(5));
        item.Margin = Padding.Empty;
    }

    /// <summary>Layout units to device pixels: paddings, marks and corner radii all go through it.</summary>
    public int Px(float value) => (int)Math.Round(value * scale);

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.ToDrawing(palette.MenuSurface));
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (roundedBySystem.Contains(e.ToolStrip))
        {
            // The system draws the border along the rounded corners in the colour it was given.
            return;
        }

        using var pen = new Pen(Theme.ToDrawing(palette.MenuBorder));
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || !(e.Item.Selected || e.Item.Pressed))
        {
            return;
        }

        var bounds = new Rectangle(0, 0, e.Item.Width - 1, e.Item.Height - 1);
        using var path = RoundedRectangle(bounds, Px(5));
        using var brush = new SolidBrush(Theme.ToDrawing(palette.MenuSelection));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Rows that only inform (shortcuts) are disabled items: readable, just quieter. So is the shortcut column.
        var secondary = !e.Item.Enabled || !string.Equals(e.Text, e.Item.Text, StringComparison.Ordinal);
        // The row is taller than the text line the layout reserves: centre the text on the whole row.
        var bounds = new Rectangle(e.TextRectangle.Left, 0, e.TextRectangle.Width, e.Item.Height);
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds, secondary ? MutedText : Text, e.TextFormat | TextFormatFlags.VerticalCenter);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(Theme.ToDrawing(palette.MenuBorder));
        e.Graphics.DrawLine(pen, Px(6), y, e.Item.Width - Px(6) - 1, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        var centerX = e.ArrowRectangle.Left + (e.ArrowRectangle.Width / 2f);
        var centerY = e.ArrowRectangle.Top + (e.ArrowRectangle.Height / 2f);
        var half = 3.5f * scale;
        using var pen = new Pen(MutedText, 1.4f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(
            pen,
            [
                new PointF(centerX - (half / 2), centerY - half),
                new PointF(centerX + (half / 2), centerY),
                new PointF(centerX - (half / 2), centerY + half),
            ]);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var centerX = e.ImageRectangle.Left + (e.ImageRectangle.Width / 2f);
        var centerY = e.Item.Height / 2f;
        using var pen = new Pen(Theme.ToDrawing(palette.Accent), 1.7f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(
            pen,
            [
                new PointF(centerX - (4.4f * scale), centerY + (0.2f * scale)),
                new PointF(centerX - (1.4f * scale), centerY + (3.2f * scale)),
                new PointF(centerX + (4.4f * scale), centerY - (3.2f * scale)),
            ]);
    }

    private static string FontFamilyName()
    {
        // Windows 10 has no Segoe UI Variable; asking GDI+ for a missing family silently gives a wrong font.
        const string preferred = "Segoe UI Variable Text";
        using var probe = new Font(preferred, 9f);
        return string.Equals(probe.Name, preferred, StringComparison.OrdinalIgnoreCase) ? preferred : "Segoe UI";
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(1, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void RoundCorners(ToolStripDropDown dropDown)
    {
        var preference = DwmCornerRound;
        if (NativeMethods.DwmSetWindowAttribute(dropDown.Handle, DwmWindowCornerPreference, ref preference, sizeof(int)) != 0)
        {
            return;
        }

        var border = palette.MenuBorder;
        var colour = border.R | (border.G << 8) | (border.B << 16);
        if (NativeMethods.DwmSetWindowAttribute(dropDown.Handle, DwmBorderColor, ref colour, sizeof(int)) == 0)
        {
            roundedBySystem.Add(dropDown);
            dropDown.Disposed += (_, _) => roundedBySystem.Remove(dropDown);
        }
    }

    private sealed class TrayColorTable(UiPalette palette) : ProfessionalColorTable
    {
        private readonly Color surface = Theme.ToDrawing(palette.MenuSurface);
        private readonly Color border = Theme.ToDrawing(palette.MenuBorder);

        public override Color ToolStripDropDownBackground => surface;

        public override Color MenuBorder => border;

        public override Color ImageMarginGradientBegin => surface;

        public override Color ImageMarginGradientMiddle => surface;

        public override Color ImageMarginGradientEnd => surface;

        public override Color SeparatorDark => border;

        public override Color SeparatorLight => border;
    }

    private static class NativeMethods
    {
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int size);
    }
}
