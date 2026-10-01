using System.Drawing;
using System.Windows.Forms;

namespace VoiceInput.App;

/// <summary>Dark tray menu that matches the overlay. High-contrast mode keeps the system look.</summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    internal static readonly Color Surface = Color.FromArgb(255, 20, 22, 30);
    internal static readonly Color Selection = Color.FromArgb(255, 42, 45, 58);
    internal static readonly Color Border = Color.FromArgb(255, 69, 73, 91);
    internal static readonly Color Text = Color.FromArgb(255, 244, 244, 248);
    internal static readonly Color DisabledText = Color.FromArgb(255, 139, 142, 156);

    private TrayMenuRenderer()
        : base(new TrayColorTable())
    {
        RoundedEdges = false;
    }

    public static void Apply(ContextMenuStrip menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (SystemInformation.HighContrast)
        {
            return;
        }

        menu.Renderer = new TrayMenuRenderer();
        menu.BackColor = Surface;
        menu.ForeColor = Text;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = true;
        menu.DropShadowEnabled = true;
        menu.Padding = new Padding(6);
        menu.Font = new Font("Segoe UI Variable Text", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.ContentRectangle.Top + (e.Item.ContentRectangle.Height / 2);
        using var pen = new Pen(Border);
        e.Graphics.DrawLine(pen, e.Item.ContentRectangle.Left + 8, y, e.Item.ContentRectangle.Right - 8, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item is null || e.Item.Enabled ? Text : DisabledText;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, e.Item.Size);
        var size = 6;
        var x = e.ImageRectangle.Left + ((e.ImageRectangle.Width - size) / 2);
        var y = bounds.Top + ((bounds.Height - size) / 2);
        using var brush = new SolidBrush(Color.FromArgb(255, 169, 155, 255));
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.FillEllipse(brush, x, y, size, size);
    }

    private sealed class TrayColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;

        public override Color MenuBorder => Border;

        public override Color MenuItemBorder => Selection;

        public override Color MenuItemSelected => Selection;

        public override Color MenuItemSelectedGradientBegin => Selection;

        public override Color MenuItemSelectedGradientEnd => Selection;

        public override Color MenuItemPressedGradientBegin => Selection;

        public override Color MenuItemPressedGradientEnd => Selection;

        public override Color ImageMarginGradientBegin => Surface;

        public override Color ImageMarginGradientMiddle => Surface;

        public override Color ImageMarginGradientEnd => Surface;

        public override Color SeparatorDark => Border;

        public override Color SeparatorLight => Border;
    }
}
