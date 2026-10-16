using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CutoutEqD.App
{
    /// <summary>
    /// Colours, fonts and drawing helpers for the app's look: a light, flat Windows 11-style shell
    /// around the dark image canvas. Icons come from the Segoe Fluent Icons / Segoe MDL2 Assets fonts
    /// that ship with Windows 10/11; if neither is installed, buttons simply show text.
    /// </summary>
    internal static class Theme
    {
        // Shell
        public static readonly Color Window = Color.FromArgb(243, 243, 243);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(229, 229, 229);
        public static readonly Color BorderStrong = Color.FromArgb(204, 204, 204);
        public static readonly Color Text = Color.FromArgb(27, 27, 27);
        public static readonly Color TextMuted = Color.FromArgb(97, 97, 97);
        public static readonly Color TextDisabled = Color.FromArgb(160, 160, 160);
        public static readonly Color Hover = Color.FromArgb(240, 240, 240);
        public static readonly Color Pressed = Color.FromArgb(230, 230, 230);

        // Accent (Windows 11 default blue) and status
        public static readonly Color Accent = Color.FromArgb(0, 95, 184);
        public static readonly Color AccentHover = Color.FromArgb(25, 117, 197);
        public static readonly Color AccentPressed = Color.FromArgb(0, 78, 152);
        public static readonly Color AccentSoft = Color.FromArgb(226, 238, 250);
        public static readonly Color AccentSoftHover = Color.FromArgb(212, 229, 247);
        public static readonly Color Success = Color.FromArgb(15, 123, 15);

        // Canvas (dark, so the scan and overlays stand out)
        public static readonly Color CanvasBack = Color.FromArgb(32, 32, 32);
        public static readonly Color AccentOnDark = Color.FromArgb(76, 194, 255);

        // Fonts
        public static readonly string FontFamilyName = FirstInstalled("Segoe UI Variable Text", "Segoe UI") ?? SystemFonts.DefaultFont.FontFamily.Name;
        private static readonly string SemiboldFamilyName = FirstInstalled("Segoe UI Variable Display Semib", "Segoe UI Semibold") ?? FontFamilyName;
        public static readonly string? IconFamilyName = FirstInstalled("Segoe Fluent Icons", "Segoe MDL2 Assets");

        public static readonly Font UiFont = new(FontFamilyName, 9f);
        public static readonly Font InputFont = new(FontFamilyName, 11f);
        public static readonly Font SmallFont = new(FontFamilyName, 8.25f);
        public static readonly Font SectionFont = new(SemiboldFamilyName, 8.25f);
        public static readonly Font TitleFont = new(SemiboldFamilyName, 10.5f);
        public static readonly Font ResultFont = new(SemiboldFamilyName, 26f);
        public static readonly Font? IconFont = IconFamilyName == null ? null : new Font(IconFamilyName, 10f);
        public static readonly Font? LargeIconFont = IconFamilyName == null ? null : new Font(IconFamilyName, 28f);
        public static readonly Font EmptyTitleFont = new(SemiboldFamilyName, 13f);

        /// <summary>Segoe Fluent Icons / MDL2 code points (shared by both fonts).</summary>
        public static class Icons
        {
            public const string Open = "\uE8E5";
            public const string Paste = "\uE77F";
            public const string Ruler = "\uED5E";
            public const string Detect = "\uE945";     // lightning bolt
            public const string Draw = "\uE76D";       // inking pen
            public const string Edit = "\uE70F";
            public const string Pan = "\uE7C2";        // move
            public const string Undo = "\uE7A7";
            public const string Redo = "\uE7A6";
            public const string Clear = "\uE74D";
            public const string Fit = "\uE740";
            public const string ZoomIn = "\uE8A3";
            public const string ZoomOut = "\uE71F";
            public const string Copy = "\uE8C8";
            public const string Save = "\uE74E";
            public const string Check = "\uE73E";
            public const string Info = "\uE946";
            public const string Photo = "\uE91B";
            public const string Number = "\uE8EF";     // calculator
            public const string RotateLeft = "\uE80C";
            public const string RotateRight = "\uE80D";
            public const string Crop = "\uE7A8";
            public const string Reset = "\uE72C";     // refresh
            public const string Pdf = "\uEA90";       // PDF document
        }

        private static string? FirstInstalled(params string[] names)
        {
            foreach (var n in names)
            {
                try
                {
                    // GDI+ silently substitutes a fallback face for missing fonts, so compare the name.
                    using var f = new Font(n, 9f);
                    if (string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)) return n;
                }
                catch { /* not available */ }
            }
            return null;
        }

        /// <summary>Renders an icon-font glyph to a bitmap of <paramref name="px"/> device pixels, or null without the font.</summary>
        public static Bitmap? Glyph(string glyph, Color color, int px)
        {
            if (IconFamilyName == null) return null;
            var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var f = new Font(IconFamilyName, px * 0.8f, GraphicsUnit.Pixel);
            using var b = new SolidBrush(color);
            using var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, f, b, new RectangleF(0, 0, px, px), sf);
            return bmp;
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(2 * radius, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, RectangleF r, float radius, Color fill, Color? border = null)
        {
            using var path = RoundedRect(r, radius);
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (border is Color bc)
            {
                using var p = new Pen(bc, 1f);
                g.DrawPath(p, path);
            }
        }

        /// <summary>Background colour behind a control, for controls that paint their own rounded shapes.</summary>
        public static Color BackOf(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent)
                if (p.BackColor.A == 255) return p.BackColor;
            return Surface;
        }
    }

    /// <summary>Flat toolbar / status bar rendering with rounded hover and an accent for the active mode.</summary>
    internal sealed class ModernToolStripRenderer : ToolStripProfessionalRenderer
    {
        public ModernToolStripRenderer() { RoundedEdges = false; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var b = new SolidBrush(e.ToolStrip is StatusStrip ? Theme.Window : Theme.Surface);
            e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var r = e.ToolStrip.ClientRectangle;
            using var p = new Pen(Theme.Border);
            if (e.ToolStrip is StatusStrip) e.Graphics.DrawLine(p, 0, 0, r.Width, 0);
            else e.Graphics.DrawLine(p, 0, r.Height - 1, r.Width, r.Height - 1);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            bool isChecked = item is ToolStripButton { Checked: true };
            bool hot = item.Enabled && item.Selected;
            if (!isChecked && !hot && !item.Pressed) return;

            Color fill = isChecked ? (hot ? Theme.AccentSoftHover : Theme.AccentSoft)
                       : item.Pressed ? Theme.Pressed : Theme.Hover;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            var r = new RectangleF(0.5f, 1.5f, item.Width - 1f, item.Height - 3f);
            Theme.FillRounded(g, r, 4 * s, fill);
            if (isChecked)
            {
                // Short accent pill under the active mode, as in Windows 11 navigation.
                float w = Math.Min(16 * s, item.Width - 8 * s), h = 3 * s;
                Theme.FillRounded(g, new RectangleF((item.Width - w) / 2, item.Height - h - 1.5f * s, w, h), h / 2, Theme.Accent);
            }
            g.SmoothingMode = SmoothingMode.Default;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripButton { Checked: true }) e.TextColor = Theme.Accent;
            else if (!e.Item.Enabled) e.TextColor = Theme.TextDisabled;
            else if (e.ToolStrip is StatusStrip) e.TextColor = Theme.TextMuted;
            else e.TextColor = Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var r = e.Item.ContentRectangle;
            using var p = new Pen(Theme.Border);
            if (e.Vertical)
            {
                int x = r.X + r.Width / 2;
                e.Graphics.DrawLine(p, x, r.Top + 6, x, r.Bottom - 6);
            }
            else
            {
                int y = r.Y + r.Height / 2;
                e.Graphics.DrawLine(p, r.Left, y, r.Right, y);
            }
        }
    }
}
