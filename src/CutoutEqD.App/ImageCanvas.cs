using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CutoutEqD.Core;

namespace CutoutEqD.App
{
    public enum CanvasMode { Pan, Scale, Detect, Draw, Edit }

    /// <summary>
    /// Zoomable/pannable image view that handles the interactive parts: setting the scale line,
    /// clicking to auto-detect, drawing an outline vertex by vertex, and editing an outline.
    /// All geometry is kept in image-pixel coordinates (pixel (i,j) covers [i,i+1)×[j,j+1)).
    /// </summary>
    public sealed class ImageCanvas : Control
    {
        private Bitmap? _image;
        private double _zoom = 1;
        private PointF _offset;               // screen position of image origin
        private CanvasMode _mode = CanvasMode.Detect;

        private List<Pt>? _outline;            // closed outline
        private readonly List<Pt> _drawing = new();
        private readonly Stack<List<Pt>?> _undo = new();
        private readonly Stack<List<Pt>?> _redo = new();

        private Pt? _scaleA, _scaleB;
        private Pt? _pendingScaleA;
        private Pt? _seed;

        // Interaction state
        private bool _panning;
        private Point _panStart;
        private PointF _panOffsetStart;
        private bool _spaceDown;
        private int _dragVertex = -1;
        private Pt _dragStartMouse;
        private List<Pt>? _dragOriginal;
        private int _dragScaleEnd;             // 0 none, 1 = A, 2 = B
        private int _hoverVertex = -1;
        private Point _mouse;
        private bool _mouseInside;

        private const float HitRadius = 8f;    // screen pixels

        public event EventHandler? OutlineChanged;
        public event EventHandler? ScaleLineChanged;            // endpoints moved or newly completed
        public event EventHandler<Pt>? DetectRequested;
        public event EventHandler<Pt?>? MouseImagePositionChanged;
        public event EventHandler<string>? StatusMessage;
        public event EventHandler? ModeChanged;

        public ImageCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.CanvasBack;
            Cursor = Cursors.Cross;
            TabStop = true;
        }

        // ---------------- Public state ----------------

        public Bitmap? Image
        {
            get => _image;
            set
            {
                _image = value;
                _outline = null; _drawing.Clear(); _undo.Clear(); _redo.Clear();
                _scaleA = _scaleB = _pendingScaleA = null; _seed = null;
                ZoomToFit();
                OutlineChanged?.Invoke(this, EventArgs.Empty);
                ScaleLineChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public CanvasMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                _drawing.Clear(); _pendingScaleA = null; _dragVertex = -1; _dragScaleEnd = 0;
                Cursor = value == CanvasMode.Pan ? Cursors.Hand : Cursors.Cross;
                ModeChanged?.Invoke(this, EventArgs.Empty);
                Invalidate();
            }
        }

        public IReadOnlyList<Pt>? Outline => _outline;
        public Pt? ScaleA => _scaleA;
        public Pt? ScaleB => _scaleB;
        public string ScaleLabel { get; set; } = "";
        public bool ShowLoupe { get; set; } = true;
        public bool SoftDrag { get; set; }
        public double SoftDragRadiusScreen { get; set; } = 40;   // screen px → converted at drag time
        public double Zoom => _zoom;

        /// <summary>Highlights the empty-state drop zone while a file is dragged over the window.</summary>
        public bool DropHighlight
        {
            get => _dropHighlight;
            set { if (_dropHighlight != value) { _dropHighlight = value; Invalidate(); } }
        }
        private bool _dropHighlight;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Replace the outline (e.g. from auto-detection). Undoable.</summary>
        public void SetOutline(List<Pt>? outline, Pt? seed = null)
        {
            PushUndo();
            _outline = outline == null ? null : new List<Pt>(outline);
            _seed = seed;
            OutlineChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        /// <summary>Replace the outline without an undo step (used for live threshold re-detection).</summary>
        public void ReplaceOutlineSilently(List<Pt>? outline)
        {
            _outline = outline == null ? null : new List<Pt>(outline);
            OutlineChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void ClearScale()
        {
            _scaleA = _scaleB = _pendingScaleA = null;
            ScaleLineChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            _redo.Push(Copy(_outline));
            _outline = _undo.Pop();
            OutlineChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void Redo()
        {
            if (_redo.Count == 0) return;
            _undo.Push(Copy(_outline));
            _outline = _redo.Pop();
            OutlineChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        public void ZoomToFit()
        {
            if (_image == null || Width <= 0 || Height <= 0) { _zoom = 1; _offset = PointF.Empty; Invalidate(); return; }
            double z = Math.Min((Width - 20.0) / _image.Width, (Height - 20.0) / _image.Height);
            _zoom = Math.Max(0.02, z);
            _offset = new PointF((float)((Width - _image.Width * _zoom) / 2), (float)((Height - _image.Height * _zoom) / 2));
            Invalidate();
        }

        public void ZoomBy(double factor) => ZoomAt(factor, new Point(Width / 2, Height / 2));

        // ---------------- Coordinate transforms ----------------

        private PointF ToScreen(Pt p) => new((float)(p.X * _zoom + _offset.X), (float)(p.Y * _zoom + _offset.Y));
        private Pt ToImage(Point s) => new((s.X - _offset.X) / _zoom, (s.Y - _offset.Y) / _zoom);
        private double HitImage => HitRadius / _zoom;

        private void ZoomAt(double factor, Point anchor)
        {
            if (_image == null) return;
            double nz = Math.Max(0.02, Math.Min(64, _zoom * factor));
            Pt ip = ToImage(anchor);
            _zoom = nz;
            _offset = new PointF((float)(anchor.X - ip.X * _zoom), (float)(anchor.Y - ip.Y * _zoom));
            Invalidate();
        }

        // ---------------- Mouse & keyboard ----------------

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ZoomAt(e.Delta > 0 ? 1.25 : 1 / 1.25, e.Location);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (_image == null) return;
            Pt ip = ToImage(e.Location);

            bool panGesture = e.Button == MouseButtons.Middle ||
                              (e.Button == MouseButtons.Left && (_mode == CanvasMode.Pan || _spaceDown));
            if (panGesture)
            {
                _panning = true; _panStart = e.Location; _panOffsetStart = _offset;
                Cursor = Cursors.SizeAll;
                return;
            }

            switch (_mode)
            {
                case CanvasMode.Scale: ScaleMouseDown(e, ip); break;
                case CanvasMode.Detect:
                    if (e.Button == MouseButtons.Left) DetectRequested?.Invoke(this, ip);
                    break;
                case CanvasMode.Draw: DrawMouseDown(e, ip); break;
                case CanvasMode.Edit: EditMouseDown(e, ip); break;
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouse = e.Location;
            if (_image == null) return;
            Pt ip = ToImage(e.Location);
            bool onImage = ip.X >= 0 && ip.Y >= 0 && ip.X < _image.Width && ip.Y < _image.Height;
            MouseImagePositionChanged?.Invoke(this, onImage ? ip : null);

            if (_panning)
            {
                _offset = new PointF(_panOffsetStart.X + e.X - _panStart.X, _panOffsetStart.Y + e.Y - _panStart.Y);
                Invalidate();
                return;
            }

            if (_dragScaleEnd != 0)
            {
                if (_dragScaleEnd == 1) _scaleA = ip; else _scaleB = ip;
                ScaleLineChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (_dragVertex >= 0 && _outline != null && _dragOriginal != null)
            {
                ApplyVertexDrag(ip);
                OutlineChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (_mode == CanvasMode.Edit && _outline != null)
            {
                _hoverVertex = Polygon.NearestVertex(_outline, ip, HitImage);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_panning)
            {
                _panning = false;
                Cursor = _mode == CanvasMode.Pan ? Cursors.Hand : Cursors.Cross;
            }
            _dragVertex = -1; _dragOriginal = null; _dragScaleEnd = 0;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (_mode == CanvasMode.Draw && e.Button == MouseButtons.Left)
            {
                // The double-click's first click already added a point; drop the duplicate.
                if (_drawing.Count >= 2 && Pt.Distance(_drawing[_drawing.Count - 1], _drawing[_drawing.Count - 2]) < HitImage) _drawing.RemoveAt(_drawing.Count - 1);
                CloseDrawing();
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _mouseInside = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e); _mouseInside = false;
            MouseImagePositionChanged?.Invoke(this, null);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Enter: case Keys.Escape: case Keys.Back: case Keys.Delete: case Keys.Space:
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Space: _spaceDown = true; break;
                case Keys.Enter: if (_mode == CanvasMode.Draw) CloseDrawing(); break;
                case Keys.Escape:
                    _drawing.Clear(); _pendingScaleA = null;
                    Invalidate(); break;
                case Keys.Back:
                    if (_mode == CanvasMode.Draw && _drawing.Count > 0) { _drawing.RemoveAt(_drawing.Count - 1); Invalidate(); }
                    break;
                case Keys.Delete:
                    if (_mode == CanvasMode.Edit && _hoverVertex >= 0) DeleteVertex(_hoverVertex);
                    break;
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                    NudgeHoveredVertex(e); break;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space) _spaceDown = false;
        }

        // ---------------- Mode handlers ----------------

        private void ScaleMouseDown(MouseEventArgs e, Pt ip)
        {
            if (e.Button != MouseButtons.Left) { _pendingScaleA = null; return; }
            if (_pendingScaleA == null && _scaleA is Pt a && _scaleB is Pt b)
            {
                if (Pt.Distance(a, ip) <= HitImage) { _dragScaleEnd = 1; return; }
                if (Pt.Distance(b, ip) <= HitImage) { _dragScaleEnd = 2; return; }
            }
            if (_pendingScaleA == null)
            {
                _pendingScaleA = ip;
                StatusMessage?.Invoke(this, "Now click the second point on the ruler.");
            }
            else
            {
                if (Pt.Distance(_pendingScaleA.Value, ip) < 2) return;
                _scaleA = _pendingScaleA; _scaleB = ip; _pendingScaleA = null;
                ScaleLineChanged?.Invoke(this, EventArgs.Empty);
                ScaleLineCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Raised when the user finishes clicking a brand-new scale line (prompt for distance).</summary>
        public event EventHandler? ScaleLineCompleted;

        private void DrawMouseDown(MouseEventArgs e, Pt ip)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (_drawing.Count > 0) _drawing.RemoveAt(_drawing.Count - 1);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (_drawing.Count >= 3 && Pt.Distance(_drawing[0], ip) <= HitImage) { CloseDrawing(); return; }
            _drawing.Add(ip);
            if (_drawing.Count == 1) StatusMessage?.Invoke(this, "Keep clicking around the edge. Click the first point, double-click or press Enter to close.");
        }

        private void CloseDrawing()
        {
            if (_drawing.Count < 3) { StatusMessage?.Invoke(this, "An outline needs at least 3 points."); return; }
            SetOutline(new List<Pt>(_drawing));
            _drawing.Clear();
            StatusMessage?.Invoke(this, "Outline closed. Switch to Edit to adjust it.");
            Invalidate();
        }

        private void EditMouseDown(MouseEventArgs e, Pt ip)
        {
            if (_outline == null) { StatusMessage?.Invoke(this, "No outline yet — auto-detect or draw one first."); return; }
            int v = Polygon.NearestVertex(_outline, ip, HitImage);
            if (e.Button == MouseButtons.Right)
            {
                if (v >= 0) DeleteVertex(v);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (v < 0)
            {
                int edge = Polygon.NearestEdge(_outline, ip, HitImage, out Pt proj);
                if (edge < 0) return;
                PushUndo();
                _outline.Insert(edge + 1, proj);
                v = edge + 1;
            }
            else PushUndo();

            _dragVertex = v;
            _dragStartMouse = ip;
            _dragOriginal = new List<Pt>(_outline);
            _hoverVertex = v;
        }

        private void ApplyVertexDrag(Pt ip)
        {
            var orig = _dragOriginal!;
            Pt delta = ip - _dragStartMouse;
            int n = orig.Count;
            if (!SoftDrag)
            {
                _outline![_dragVertex] = orig[_dragVertex] + delta;
                return;
            }
            // Soft drag: neighbours move with a raised-cosine falloff by arc length along the outline.
            double radius = SoftDragRadiusScreen / _zoom;
            for (int i = 0; i < n; i++) _outline![i] = orig[i];
            _outline![_dragVertex] = orig[_dragVertex] + delta;
            foreach (int dir in new[] { 1, -1 })
            {
                double s = 0;
                int prev = _dragVertex;
                for (int k = 1; k < n / 2; k++)
                {
                    int i = ((_dragVertex + dir * k) % n + n) % n;
                    s += Pt.Distance(orig[i], orig[prev]);
                    if (s >= radius) break;
                    double wgt = 0.5 * (1 + Math.Cos(Math.PI * s / radius));
                    _outline[i] = orig[i] + delta * wgt;
                    prev = i;
                }
            }
        }

        private void DeleteVertex(int v)
        {
            if (_outline == null) return;
            if (_outline.Count <= 3) { StatusMessage?.Invoke(this, "An outline needs at least 3 vertices."); return; }
            PushUndo();
            _outline.RemoveAt(v);
            _hoverVertex = -1;
            OutlineChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        private void NudgeHoveredVertex(KeyEventArgs e)
        {
            if (_mode != CanvasMode.Edit || _outline == null || _hoverVertex < 0) return;
            double step = e.Shift ? 1.0 : 0.25;    // image pixels
            Pt d = e.KeyCode switch
            {
                Keys.Left => new Pt(-step, 0), Keys.Right => new Pt(step, 0),
                Keys.Up => new Pt(0, -step), _ => new Pt(0, step)
            };
            PushUndo();
            _outline[_hoverVertex] = _outline[_hoverVertex] + d;
            OutlineChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }

        private void PushUndo()
        {
            _undo.Push(Copy(_outline));
            _redo.Clear();
            if (_undo.Count > 200)
            {
                // Trim the oldest entries.
                var arr = _undo.ToArray();
                _undo.Clear();
                for (int i = Math.Min(arr.Length, 150) - 1; i >= 0; i--) _undo.Push(arr[i]);
            }
        }

        private static List<Pt>? Copy(List<Pt>? p) => p == null ? null : new List<Pt>(p);

        // ---------------- Painting ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_image == null)
            {
                DrawEmptyState(g);
                return;
            }

            g.InterpolationMode = _zoom >= 2 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_image, new RectangleF(_offset.X, _offset.Y, (float)(_image.Width * _zoom), (float)(_image.Height * _zoom)));

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            DrawOverlays(g, ToScreen, 1f, _mode == CanvasMode.Edit, true);

            if (ShowLoupe && _mouseInside && !_panning && (_mode == CanvasMode.Scale || _mode == CanvasMode.Draw || _mode == CanvasMode.Edit))
                DrawLoupe(g);
        }

        /// <summary>Dashed drop zone with an icon and short instructions, shown before an image is loaded.</summary>
        private void DrawEmptyState(Graphics g)
        {
            float s = DeviceDpi / 96f;
            var zone = new RectangleF(0, 0, Math.Min(Width - 48 * s, 520 * s), Math.Min(Height - 48 * s, 280 * s));
            if (zone.Width < 120 * s || zone.Height < 120 * s)
            {
                TextRenderer.DrawText(g, "Open (Ctrl+O), paste (Ctrl+V) or drop a scan here.", Theme.UiFont, ClientRectangle,
                    Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            zone.X = (Width - zone.Width) / 2;
            zone.Y = (Height - zone.Height) / 2;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.RoundedRect(zone, 12 * s))
            {
                using var fill = new SolidBrush(_dropHighlight ? Color.FromArgb(36, Theme.AccentOnDark) : Color.FromArgb(10, 255, 255, 255));
                g.FillPath(fill, path);
                using var pen = new Pen(_dropHighlight ? Theme.AccentOnDark : Color.FromArgb(80, 255, 255, 255), 1.5f * s) { DashStyle = DashStyle.Dash };
                g.DrawPath(pen, path);
            }
            g.SmoothingMode = SmoothingMode.Default;

            const string title = "Drop a scanned image here";
            const string sub = "or press Ctrl+O to open a file, Ctrl+V to paste a screenshot";
            const string tip = "Include a ruler lying in the same plane as the cutout.";
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            int w = (int)(zone.Width - 40 * s);
            int iconH = Theme.LargeIconFont != null ? TextRenderer.MeasureText(Theme.Icons.Photo, Theme.LargeIconFont).Height : 0;
            int titleH = TextRenderer.MeasureText(title, Theme.EmptyTitleFont, new Size(w, 0), flags).Height;
            int subH = TextRenderer.MeasureText(sub, Theme.UiFont, new Size(w, 0), flags).Height;
            int tipH = TextRenderer.MeasureText(tip, Theme.SmallFont, new Size(w, 0), flags).Height;
            int gap = (int)(10 * s);
            int total = iconH + (iconH > 0 ? gap : 0) + titleH + gap / 2 + subH + 2 * gap + tipH;
            int x = (int)(zone.X + 20 * s), y = (int)(zone.Y + (zone.Height - total) / 2);

            if (Theme.LargeIconFont != null)
            {
                TextRenderer.DrawText(g, Theme.Icons.Photo, Theme.LargeIconFont, new Rectangle(x, y, w, iconH),
                    _dropHighlight ? Theme.AccentOnDark : Color.FromArgb(170, 170, 170), flags);
                y += iconH + gap;
            }
            TextRenderer.DrawText(g, title, Theme.EmptyTitleFont, new Rectangle(x, y, w, titleH), Color.FromArgb(235, 235, 235), flags);
            y += titleH + gap / 2;
            TextRenderer.DrawText(g, sub, Theme.UiFont, new Rectangle(x, y, w, subH), Color.FromArgb(185, 185, 185), flags);
            y += subH + 2 * gap;
            TextRenderer.DrawText(g, tip, Theme.SmallFont, new Rectangle(x, y, w, tipH), Color.FromArgb(140, 140, 140), flags);
        }

        /// <summary>Draws outline, scale line and in-progress geometry using the supplied transform.
        /// Shared with the "save annotated image" export.</summary>
        public void DrawOverlays(Graphics g, Func<Pt, PointF> map, float lineScale, bool showVertices, bool interactive)
        {
            // Outline
            if (_outline != null && _outline.Count >= 3)
            {
                var pts = _outline.ConvertAll(p => map(p)).ToArray();
                using (var fill = new SolidBrush(Color.FromArgb(55, 255, 210, 0))) g.FillPolygon(fill, pts);
                using (var pen = new Pen(Color.FromArgb(255, 30, 220, 60), 2f * lineScale)) g.DrawPolygon(pen, pts);
                if (showVertices)
                {
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float r = i == _hoverVertex ? 5.5f : 3.5f;
                        using var vb = new SolidBrush(i == _hoverVertex ? Color.OrangeRed : Color.White);
                        g.FillEllipse(vb, pts[i].X - r, pts[i].Y - r, 2 * r, 2 * r);
                        using var vp = new Pen(Color.FromArgb(200, 0, 0, 0), 1.25f);
                        g.DrawEllipse(vp, pts[i].X - r, pts[i].Y - r, 2 * r, 2 * r);
                    }
                }
            }

            // In-progress manual outline
            if (interactive && _drawing.Count > 0)
            {
                var pts = _drawing.ConvertAll(p => map(p));
                using var pen = new Pen(Color.Orange, 2f);
                if (pts.Count >= 2) g.DrawLines(pen, pts.ToArray());
                using var dash = new Pen(Color.Orange, 1f) { DashStyle = DashStyle.Dash };
                g.DrawLine(dash, pts[pts.Count - 1], _mouse);
                foreach (var p in pts) g.FillEllipse(Brushes.Orange, p.X - 3, p.Y - 3, 6, 6);
                if (_drawing.Count >= 3) g.DrawEllipse(pen, pts[0].X - 7, pts[0].Y - 7, 14, 14);
            }

            // Seed marker
            if (interactive && _seed is Pt sd)
            {
                var s = map(sd);
                using var pen = new Pen(Color.DeepSkyBlue, 1.5f);
                g.DrawLine(pen, s.X - 6, s.Y, s.X + 6, s.Y); g.DrawLine(pen, s.X, s.Y - 6, s.X, s.Y + 6);
            }

            // Scale line
            if (_scaleA is Pt a && _scaleB is Pt b)
                DrawScaleLine(g, map(a), map(b), ScaleLabel, lineScale);
            if (interactive && _pendingScaleA is Pt pa)
                DrawScaleLine(g, map(pa), _mouse, "", 1f);
        }

        private static void DrawScaleLine(Graphics g, PointF a, PointF b, string label, float lineScale)
        {
            using var pen = new Pen(Color.Magenta, 1.5f * lineScale);
            g.DrawLine(pen, a, b);
            float t = 8 * lineScale;
            foreach (var p in new[] { a, b })
            {
                g.DrawLine(pen, p.X - t, p.Y, p.X + t, p.Y);
                g.DrawLine(pen, p.X, p.Y - t, p.X, p.Y + t);
            }
            if (!string.IsNullOrEmpty(label))
            {
                using var f = new Font(SystemFonts.DefaultFont.FontFamily, 9f * lineScale, FontStyle.Bold);
                var mid = new PointF((a.X + b.X) / 2, (a.Y + b.Y) / 2 - 18 * lineScale);
                var sz = g.MeasureString(label, f);
                var pill = new RectangleF(mid.X - sz.Width / 2 - 5 * lineScale, mid.Y - 1 * lineScale, sz.Width + 10 * lineScale, sz.Height + 2 * lineScale);
                var oldMode = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.FillRounded(g, pill, pill.Height / 2, Color.FromArgb(210, 20, 20, 20));
                g.SmoothingMode = oldMode;
                using var tb = new SolidBrush(Color.Magenta);
                g.DrawString(label, f, tb, mid.X - sz.Width / 2, mid.Y);
            }
        }

        /// <summary>Magnified view of the area under the cursor, for precise clicks on ruler ticks and edges.</summary>
        private void DrawLoupe(Graphics g)
        {
            const int size = 160;
            const double mag = 8;                       // loupe pixels per image pixel (relative to 1:1)
            double loupeZoom = Math.Max(_zoom * 3, mag);
            var rect = new Rectangle(10, 10, size, size);
            if (_mouse.X < size + 40 && _mouse.Y < size + 40) rect.X = Width - size - 10;  // keep out of the way

            Pt c = ToImage(_mouse);
            double half = size / 2.0 / loupeZoom;
            var src = new RectangleF((float)(c.X - half), (float)(c.Y - half), (float)(2 * half), (float)(2 * half));

            // Soft shadow, then clip everything to a rounded square.
            using var frame = Theme.RoundedRect(rect, 10);
            using (var shadow = Theme.RoundedRect(new RectangleF(rect.X + 2, rect.Y + 3, rect.Width, rect.Height), 10))
            using (var sb = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                g.FillPath(sb, shadow);

            var state = g.Save();
            g.SetClip(frame);
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_image!, rect, src, GraphicsUnit.Pixel);
            g.PixelOffsetMode = PixelOffsetMode.Default;

            // Overlays inside the loupe
            PointF LMap(Pt p) => new((float)(rect.X + (p.X - src.X) * loupeZoom), (float)(rect.Y + (p.Y - src.Y) * loupeZoom));
            if (_outline != null && _outline.Count >= 3)
            {
                using var pen = new Pen(Color.FromArgb(200, 30, 220, 60), 1.5f);
                g.DrawPolygon(pen, _outline.ConvertAll(p => LMap(p)).ToArray());
            }
            if (_drawing.Count >= 2)
            {
                using var pen = new Pen(Color.Orange, 1.5f);
                g.DrawLines(pen, _drawing.ConvertAll(p => LMap(p)).ToArray());
            }
            if (_scaleA is Pt a && _scaleB is Pt b) { using var pen = new Pen(Color.Magenta, 1f); g.DrawLine(pen, LMap(a), LMap(b)); }
            if (_pendingScaleA is Pt pa) { using var pen = new Pen(Color.Magenta, 1f); g.DrawLine(pen, LMap(pa), LMap(c)); }

            using (var cross = new Pen(Color.FromArgb(220, 255, 0, 0), 1f))
            {
                int cx = rect.X + size / 2, cy = rect.Y + size / 2;
                g.DrawLine(cross, rect.X, cy, cx - 4, cy); g.DrawLine(cross, cx + 4, cy, rect.Right, cy);
                g.DrawLine(cross, cx, rect.Y, cx, cy - 4); g.DrawLine(cross, cx, cy + 4, cx, rect.Bottom);
            }
            g.Restore(state);
            using var border = new Pen(Color.FromArgb(220, 255, 255, 255), 1.5f);
            g.DrawPath(border, frame);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }
    }
}
