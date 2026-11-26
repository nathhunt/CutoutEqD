using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CutoutEqD.App
{
    /// <summary>Flat button with rounded corners, an optional icon glyph, and primary / selected styles.</summary>
    internal sealed class ModernButton : Button
    {
        private bool _hover, _pressed, _selected;

        public ModernButton(string text, string? glyph = null, bool primary = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Text = text;
            Glyph = glyph;
            Primary = primary;
            Font = Theme.UiFont;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Height = 34;
            Margin = new Padding(0, 0, 0, 6);
        }

        public string? Glyph { get; set; }
        public bool Primary { get; set; }

        /// <summary>Highlights the button as the active choice (e.g. the current canvas mode).</summary>
        public bool Selected
        {
            get => _selected;
            set { if (_selected != value) { _selected = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = _pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;

            Color fill, border, fore;
            if (!Enabled && Primary) { fill = border = Theme.AccentDisabled; fore = Color.White; }
            else if (!Enabled) { fill = Theme.Hover; border = Theme.Border; fore = Theme.TextDisabled; }
            else if (Primary)
            {
                fill = _pressed ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent;
                border = fill; fore = Color.White;
            }
            else if (_selected)
            {
                fill = _pressed ? Theme.AccentSoftHover : _hover ? Theme.AccentSoftHover : Theme.AccentSoft;
                border = Theme.Accent; fore = Theme.Accent;
            }
            else
            {
                fill = _pressed ? Theme.Pressed : _hover ? Theme.Hover : Theme.Surface;
                border = Theme.BorderStrong; fore = Theme.Text;
            }

            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRounded(g, r, 5 * s, fill, border);
            if (Focused && ShowFocusCues)
            {
                using var path = Theme.RoundedRect(RectangleF.Inflate(r, -2 * s, -2 * s), 3 * s);
                using var pen = new Pen(Primary ? Color.White : Theme.Accent, 1.5f * s);
                g.DrawPath(pen, path);
            }

            int x = (int)(12 * s);
            if (Glyph != null && Theme.IconFont != null)
            {
                var gr = new Rectangle(x, 0, (int)(16 * s), Height);
                TextRenderer.DrawText(g, Glyph, Theme.IconFont, gr, fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x = gr.Right + (int)(10 * s);
            }
            var tr = new Rectangle(x, 0, Width - x - (int)(8 * s), Height);
            TextRenderer.DrawText(g, Text, Font, tr, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>On/off switch drawn in the Windows 11 style; behaves exactly like a CheckBox.</summary>
    internal sealed class ToggleSwitch : CheckBox
    {
        private bool _hover;

        public ToggleSwitch(string text, bool on = false)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Text = text;
            Checked = on;
            Font = Theme.UiFont;
            AutoSize = true;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 2, 0, 2);
        }

        private float S => DeviceDpi / 96f;

        public override Size GetPreferredSize(Size proposedSize)
        {
            var t = TextRenderer.MeasureText(Text, Font);
            return new Size((int)(40 * S + 10 * S) + t.Width, Math.Max((int)(30 * S), t.Height));
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = S, w = 40 * s, h = 20 * s;
            var track = new RectangleF(1, (Height - h) / 2, w, h);

            Color fill, knob, border;
            if (!Enabled) { fill = Checked ? Theme.TextDisabled : Theme.Surface; border = Theme.TextDisabled; knob = Checked ? Theme.Surface : Theme.TextDisabled; }
            else if (Checked) { fill = _hover ? Theme.AccentHover : Theme.Accent; border = fill; knob = Color.White; }
            else { fill = _hover ? Theme.Hover : Theme.Surface; border = Theme.TextMuted; knob = Theme.TextMuted; }
            Theme.FillRounded(g, track, h / 2, fill, border);

            float k = (_hover && Enabled ? 14 : 12) * s;
            float kx = Checked ? track.Right - h / 2 - k / 2 : track.X + h / 2 - k / 2;
            using (var kb = new SolidBrush(knob)) g.FillEllipse(kb, kx, track.Y + (h - k) / 2, k, k);

            if (Focused && ShowFocusCues)
            {
                using var path = Theme.RoundedRect(RectangleF.Inflate(track, 2 * s, 2 * s), h / 2 + 2 * s);
                using var pen = new Pen(Theme.Accent, 1.5f * s);
                g.DrawPath(pen, path);
            }

            var tr = new Rectangle((int)(track.Right + 10 * s), 0, Width, Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Text : Theme.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    /// <summary>White rounded panel with a numbered step badge and title; stacks its content vertically.</summary>
    internal sealed class Card : Panel
    {
        private readonly FlowLayoutPanel _body;
        private readonly string _title;
        private readonly string? _step;
        private bool _completed;

        public const int ContentWidth = 252;

        public Card(string title, string? step, params Control[] items)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _title = title;
            _step = step;
            BackColor = Theme.Window;
            Width = ContentWidth + 28;
            Margin = new Padding(0, 0, 0, 12);

            // Size the card explicitly from its content (avoids AutoSize/Dock feedback loops).
            _body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(14, 48), Margin = Padding.Empty,
                BackColor = Theme.Surface
            };
            _body.Controls.AddRange(items);
            Controls.Add(_body);
            _body.SizeChanged += (_, _) => Fit();
            _body.Layout += (_, _) => Fit();
            Fit();
        }

        /// <summary>Shows a green tick in place of the step number once this step is done.</summary>
        public bool Completed
        {
            get => _completed;
            set { if (_completed != value) { _completed = value; Invalidate(); } }
        }

        private void Fit() => Height = _body.Top + _body.GetPreferredSize(new Size(Width - 2 * _body.Left, 0)).Height + _body.Left;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;
            Theme.FillRounded(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8 * s, Theme.Surface, Theme.Border);

            int pad = _body.Left;
            int cy = _body.Top / 2 + (int)(2 * s);
            int x = pad;
            if (_step != null)
            {
                float d = 24 * s;
                var badge = new RectangleF(pad, cy - d / 2, d, d);
                using (var b = new SolidBrush(_completed ? Theme.Success : Theme.AccentSoft)) g.FillEllipse(b, badge);
                var br = Rectangle.Round(badge);
                if (_completed && Theme.IconFont != null)
                    TextRenderer.DrawText(g, Theme.Icons.Check, Theme.IconFont, br, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                else
                    TextRenderer.DrawText(g, _completed ? "✓" : _step, Theme.SectionFont, br, _completed ? Color.White : Theme.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x = (int)(badge.Right + 10 * s);
            }
            var tr = new Rectangle(x, cy - (int)(14 * s), Width - x - pad, (int)(28 * s));
            TextRenderer.DrawText(g, _title, Theme.TitleFont, tr, Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>A selectable result: radio mark and caption, a large value and a formula line. Behaves like a radio
    /// button (click, Space or Enter selects it); the owner keeps the pair exclusive.</summary>
    internal sealed class ValueTile : Control
    {
        private string _value = "—";
        private bool _selected, _hover;

        public ValueTile(string caption, string formula)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Text = caption;
            Formula = formula;
            Font = Theme.UiFont;
            Size = new Size((Card.ContentWidth - 6) / 2, 78);
            Margin = Padding.Empty;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.RadioButton;
        }

        public string Formula { get; }

        public string Value
        {
            get => _value;
            set { if (_value != value) { _value = value; AccessibleDescription = value; Invalidate(); } }
        }

        public bool Selected
        {
            get => _selected;
            set { if (_selected != value) { _selected = value; AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left && Enabled) Focus(); }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

        protected override AccessibleObject CreateAccessibilityInstance() => new TileAccessibleObject(this);

        private sealed class TileAccessibleObject(ValueTile owner) : ControlAccessibleObject(owner)
        {
            public override AccessibleStates State => base.State | (owner.Selected ? AccessibleStates.Checked : AccessibleStates.None);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Space or Keys.Enter && Enabled) { OnClick(EventArgs.Empty); e.Handled = true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;

            bool on = _selected && Enabled;
            Color fill = on ? Theme.AccentSoft : _hover && Enabled ? Theme.Hover : Theme.Surface;
            Color border = on ? Theme.Accent : Theme.BorderStrong;
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            using (var path = Theme.RoundedRect(r, 6 * s))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using var p = new Pen(border, (on ? 2f : 1f) * s);
                g.DrawPath(p, path);
            }
            if (Focused && ShowFocusCues)
            {
                using var path = Theme.RoundedRect(RectangleF.Inflate(r, -3 * s, -3 * s), 4 * s);
                using var pen = new Pen(Theme.Accent, 1f * s) { DashStyle = DashStyle.Dot };
                g.DrawPath(pen, path);
            }

            // Radio mark and caption.
            int pad = (int)(9 * s);
            float d = 14 * s, cy = pad + 8 * s;
            var radio = new RectangleF(pad, cy - d / 2, d, d);
            using (var b = new SolidBrush(Theme.Surface)) g.FillEllipse(b, radio);
            using (var p = new Pen(on ? Theme.Accent : Enabled ? Theme.TextMuted : Theme.TextDisabled, 1.5f * s)) g.DrawEllipse(p, radio);
            if (on)
            {
                var dot = RectangleF.Inflate(radio, -3.5f * s, -3.5f * s);
                using var b = new SolidBrush(Theme.Accent);
                g.FillEllipse(b, dot);
            }
            Color muted = Enabled ? Theme.TextMuted : Theme.TextDisabled;
            int tx = (int)(radio.Right + 6 * s);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(tx, (int)(cy - 10 * s), Width - tx - pad, (int)(20 * s)), muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            // Large value, shrunk if it would not fit.
            var vr = new Rectangle(pad, (int)(26 * s), Width - 2 * pad, (int)(30 * s));
            Color valueColor = !Enabled ? Theme.TextDisabled : on ? Theme.Accent : Theme.Text;
            var font = Theme.TileValueFont;
            int needed = TextRenderer.MeasureText(_value, font, Size.Empty, TextFormatFlags.NoPadding).Width;
            Font? shrunk = needed > vr.Width ? new Font(font.FontFamily, Math.Max(9f, font.Size * vr.Width / needed), font.Style) : null;
            TextRenderer.DrawText(g, _value, shrunk ?? font, vr, valueColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            shrunk?.Dispose();

            TextRenderer.DrawText(g, Formula, Theme.SmallFont, new Rectangle(pad, (int)(56 * s), Width - 2 * pad, (int)(16 * s)), muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>One "caption ........ value" line in a card, with a hairline divider underneath.</summary>
    internal sealed class MetricRow : Control
    {
        private string _value = "—";

        public MetricRow(string caption)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = caption;
            Font = Theme.UiFont;
            Size = new Size(Card.ContentWidth, 28);
            Margin = Padding.Empty;
        }

        public string Value
        {
            get => _value;
            set { if (_value != value) { _value = value; Invalidate(); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            var r = ClientRectangle;
            TextRenderer.DrawText(g, Text, Font, r, Theme.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _value, Font, r, Theme.Text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using var p = new Pen(Theme.Border);
            g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }
    }

    /// <summary>Soft accent box with an info icon, a heading and wrapped body text; sizes its own height.</summary>
    internal sealed class Callout : Control
    {
        private string _heading = "";
        private string _body = "";

        public Callout()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.UiFont;
            Width = Card.ContentWidth + 28;
            Margin = new Padding(0, 0, 0, 12);
        }

        public void SetContent(string heading, string body)
        {
            _heading = heading; _body = body;
            UpdateHeight();
            Invalidate();
        }

        private float S => DeviceDpi / 96f;
        private int Pad => (int)(12 * S);
        private int TextLeft => Pad + (Theme.IconFont != null ? (int)(26 * S) : 0);

        private void UpdateHeight()
        {
            int w = Math.Max(10, Width - TextLeft - Pad);
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            int hh = TextRenderer.MeasureText(_heading, Theme.SectionFont, new Size(w, 0), flags).Height;
            int bh = TextRenderer.MeasureText(_body, Font, new Size(w, 0), flags).Height;
            Height = Pad + hh + (int)(4 * S) + bh + Pad;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (_body.Length > 0)
            {
                int before = Height;
                UpdateHeight();
                if (Height != before) Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = S;
            Theme.FillRounded(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8 * s, Theme.AccentSoft);

            if (Theme.IconFont != null)
                TextRenderer.DrawText(g, Theme.Icons.Info, Theme.IconFont, new Point(Pad, Pad + (int)(1 * s)), Theme.Accent, TextFormatFlags.NoPadding);

            int w = Width - TextLeft - Pad;
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            int hh = TextRenderer.MeasureText(_heading, Theme.SectionFont, new Size(w, 0), flags).Height;
            TextRenderer.DrawText(g, _heading, Theme.SectionFont, new Rectangle(TextLeft, Pad, w, hh), Theme.Accent, flags);
            int y = Pad + hh + (int)(4 * s);
            TextRenderer.DrawText(g, _body, Font, new Rectangle(TextLeft, y, w, Height - y), Theme.Text, flags);
        }
    }
}
