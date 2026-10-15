using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CutoutEqD.Core;

namespace CutoutEqD.App
{
    public sealed class MainForm : Form
    {
        private readonly ImageCanvas _canvas = new() { Dock = DockStyle.Fill };

        // Image data
        private string _imageName = "";
        private GrayImage? _gray;
        private GrayImage? _blurred;
        private double _blurredSigma = -1;

        // Scale
        private double _knownDistanceMm = 100;
        private double? _mmPerPixel;

        // Detection state
        private Pt? _lastSeed;
        private bool _outlineFromDetection;
        private bool _suppressOutlineEvent;
        private bool _suppressThresholdEvent;

        // Controls
        private readonly Label _scaleInfo = new() { AutoSize = false, Size = new Size(240, 64) };
        private readonly CheckBox _autoThreshold = new() { Text = "Automatic threshold", Checked = true, AutoSize = true };
        private readonly TrackBar _threshold = new() { Minimum = 1, Maximum = 255, Value = 128, TickFrequency = 16, Width = 200, Enabled = false };
        private readonly Label _thresholdValue = new() { AutoSize = true, Text = "128" };
        private readonly NumericUpDown _blur = new() { Minimum = 0, Maximum = 5, DecimalPlaces = 1, Increment = 0.5m, Value = 1.0m, Width = 60 };
        private readonly CheckBox _softDrag = new() { Text = "Soft drag (moves neighbours)", AutoSize = true };
        private readonly CheckBox _loupe = new() { Text = "Show magnifier", Checked = true, AutoSize = true };
        private readonly Label _eqd = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont.FontFamily, 20f, FontStyle.Bold), Text = "—" };
        private readonly Label _details = new() { AutoSize = true, MaximumSize = new Size(260, 0) };
        private readonly Label _help = new() { AutoSize = true, MaximumSize = new Size(260, 0), ForeColor = SystemColors.GrayText };
        private readonly ToolStripStatusLabel _statusMsg = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel _statusPos = new() { AutoSize = false, Width = 380, TextAlign = ContentAlignment.MiddleRight };
        private readonly ToolStripStatusLabel _statusZoom = new() { AutoSize = false, Width = 90 };
        private readonly Dictionary<CanvasMode, ToolStripButton> _modeButtons = new();

        public MainForm()
        {
            Text = "Cutout Equivalent Diameter";
            Width = 1300; Height = 900;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildToolStrip(out var tools);
            var side = BuildSidePanel();
            var status = new StatusStrip();
            status.Items.AddRange(new ToolStripItem[] { _statusMsg, _statusZoom, _statusPos });

            Controls.Add(_canvas);
            Controls.Add(side);
            Controls.Add(tools);
            Controls.Add(status);

            _canvas.DetectRequested += (_, p) => RunDetection(p, userClick: true);
            _canvas.OutlineChanged += (_, _) => OnOutlineChanged();
            _canvas.ScaleLineChanged += (_, _) => UpdateScaleFromLine();
            _canvas.ScaleLineCompleted += (_, _) => PromptForDistance();
            _canvas.StatusMessage += (_, m) => _statusMsg.Text = m;
            _canvas.ModeChanged += (_, _) => UpdateModeUi();
            _canvas.MouseImagePositionChanged += (_, p) => UpdateMousePos(p);
            _canvas.Paint += (_, _) => _statusZoom.Text = $"Zoom {_canvas.Zoom * 100:F0}%";

            _autoThreshold.CheckedChanged += (_, _) => { _threshold.Enabled = !_autoThreshold.Checked; RedetectIfUnedited(); };
            _threshold.ValueChanged += (_, _) =>
            {
                _thresholdValue.Text = _threshold.Value.ToString(CultureInfo.InvariantCulture);
                if (!_suppressThresholdEvent) RedetectIfUnedited();
            };
            _blur.ValueChanged += (_, _) => RedetectIfUnedited();
            _softDrag.CheckedChanged += (_, _) => _canvas.SoftDrag = _softDrag.Checked;
            _loupe.CheckedChanged += (_, _) => { _canvas.ShowLoupe = _loupe.Checked; _canvas.Invalidate(); };

            DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
            DragDrop += (_, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) LoadImageFile(files[0]);
            };

            UpdateModeUi();
            UpdateResults();
            UpdateScaleInfo();
            _statusMsg.Text = "Open or paste a scanned image of the cutout with a ruler alongside.";
        }

        // ---------------- UI construction ----------------

        private void BuildToolStrip(out ToolStrip tools)
        {
            tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(20, 20), Padding = new Padding(4, 2, 4, 2) };
            tools.Items.Add(new ToolStripButton("Open…", null, (_, _) => OpenImage()) { ToolTipText = "Open image (Ctrl+O)" });
            tools.Items.Add(new ToolStripButton("Paste", null, (_, _) => PasteImage()) { ToolTipText = "Paste image from clipboard (Ctrl+V)" });
            tools.Items.Add(new ToolStripSeparator());

            AddMode(tools, CanvasMode.Scale, "1  Set scale", "S", "Click two points on the ruler, then enter the distance between them");
            AddMode(tools, CanvasMode.Detect, "2  Auto-detect", "A", "Click inside the light aperture to find its edge");
            AddMode(tools, CanvasMode.Draw, "Draw outline", "D", "Click around the edge to draw an outline manually");
            AddMode(tools, CanvasMode.Edit, "Edit outline", "E", "Drag vertices; click an edge to add one; right-click to delete");
            AddMode(tools, CanvasMode.Pan, "Pan", "H", "Drag to move the image (middle-drag or Space+drag works in any mode)");
            tools.Items.Add(new ToolStripSeparator());

            tools.Items.Add(new ToolStripButton("Undo", null, (_, _) => _canvas.Undo()) { ToolTipText = "Undo outline change (Ctrl+Z)" });
            tools.Items.Add(new ToolStripButton("Redo", null, (_, _) => _canvas.Redo()) { ToolTipText = "Redo (Ctrl+Y)" });
            tools.Items.Add(new ToolStripButton("Clear outline", null, (_, _) => { _lastSeed = null; _outlineFromDetection = false; _canvas.SetOutline(null); }));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripButton("Fit", null, (_, _) => _canvas.ZoomToFit()) { ToolTipText = "Zoom to fit (F)" });
            tools.Items.Add(new ToolStripButton("+", null, (_, _) => _canvas.ZoomBy(1.25)) { ToolTipText = "Zoom in (mouse wheel)" });
            tools.Items.Add(new ToolStripButton("−", null, (_, _) => _canvas.ZoomBy(1 / 1.25)) { ToolTipText = "Zoom out (mouse wheel)" });
        }

        private void AddMode(ToolStrip tools, CanvasMode mode, string text, string key, string tip)
        {
            var b = new ToolStripButton(text, null, (_, _) => _canvas.Mode = mode) { ToolTipText = $"{tip} ({key})", CheckOnClick = false };
            _modeButtons[mode] = b;
            tools.Items.Add(b);
        }

        private Control BuildSidePanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, Width = 290, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Padding = new Padding(10), BackColor = SystemColors.Control
            };

            GroupBox Group(string title, params Control[] items)
            {
                // Size the group explicitly from its content (avoids AutoSize/Dock feedback loops).
                var flow = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(8, 20), Margin = Padding.Empty
                };
                flow.Controls.AddRange(items);
                var gb = new GroupBox { Text = title, Width = 262, Margin = new Padding(0, 0, 0, 10) };
                gb.Controls.Add(flow);
                void Fit() => gb.Height = flow.Top + flow.GetPreferredSize(new Size(gb.Width - 16, 0)).Height + 12;
                flow.SizeChanged += (_, _) => Fit();
                flow.Layout += (_, _) => Fit();
                Fit();
                return gb;
            }
            Button Btn(string text, EventHandler onClick) { var b = new Button { Text = text, Width = 230, Height = 28 }; b.Click += onClick; return b; }

            var setScale = Btn("Set scale on ruler…", (_, _) => _canvas.Mode = CanvasMode.Scale);
            var editDist = Btn("Change known distance…", (_, _) => PromptForDistance());
            panel.Controls.Add(Group("1. Scale", _scaleInfo, setScale, editDist));

            var thrRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            thrRow.Controls.Add(_threshold); thrRow.Controls.Add(_thresholdValue);
            var blurRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            blurRow.Controls.Add(new Label { Text = "Noise smoothing (px):", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            blurRow.Controls.Add(_blur);
            panel.Controls.Add(Group("2. Outline",
                Btn("Auto-detect (click in aperture)", (_, _) => _canvas.Mode = CanvasMode.Detect),
                Btn("Draw outline manually", (_, _) => _canvas.Mode = CanvasMode.Draw),
                Btn("Edit outline", (_, _) => _canvas.Mode = CanvasMode.Edit),
                _autoThreshold, thrRow, blurRow, _softDrag, _loupe));

            panel.Controls.Add(Group("3. Result",
                new Label { Text = "Equivalent diameter", AutoSize = true },
                _eqd, _details,
                Btn("Copy results", (_, _) => CopyResults()),
                Btn("Save annotated image…", (_, _) => SaveAnnotated())));

            panel.Controls.Add(_help);
            return panel;
        }

        // ---------------- Keyboard shortcuts ----------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.O: OpenImage(); return true;
                case Keys.Control | Keys.V: PasteImage(); return true;
                case Keys.Control | Keys.Z: _canvas.Undo(); return true;
                case Keys.Control | Keys.Y: case Keys.Control | Keys.Shift | Keys.Z: _canvas.Redo(); return true;
                case Keys.Control | Keys.C: CopyResults(); return true;
                case Keys.Control | Keys.S: SaveAnnotated(); return true;
            }
            if (_canvas.Focused && (keyData & (Keys.Control | Keys.Alt)) == 0)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.S: _canvas.Mode = CanvasMode.Scale; return true;
                    case Keys.A: _canvas.Mode = CanvasMode.Detect; return true;
                    case Keys.D: _canvas.Mode = CanvasMode.Draw; return true;
                    case Keys.E: _canvas.Mode = CanvasMode.Edit; return true;
                    case Keys.H: _canvas.Mode = CanvasMode.Pan; return true;
                    case Keys.F: _canvas.ZoomToFit(); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------- Image loading ----------------

        private void OpenImage()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Open cutout image",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif|All files|*.*"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) LoadImageFile(dlg.FileName);
        }

        public void OpenFromCommandLine(string path) => LoadImageFile(path);

        private void LoadImageFile(string path)
        {
            try
            {
                SetImage(BitmapUtil.Load(path), Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open the image:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void PasteImage()
        {
            try
            {
                if (Clipboard.ContainsImage() && Clipboard.GetImage() is System.Drawing.Image img)
                {
                    using (img) SetImage(BitmapUtil.To32bpp(img), "Pasted image");
                }
                else if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Count > 0)
                    LoadImageFile(Clipboard.GetFileDropList()[0]!);
                else _statusMsg.Text = "The clipboard doesn't contain an image.";
            }
            catch (Exception ex) { _statusMsg.Text = "Paste failed: " + ex.Message; }
        }

        private void SetImage(Bitmap bmp, string name)
        {
            var old = _canvas.Image;
            Cursor = Cursors.WaitCursor;
            try
            {
                _gray = BitmapUtil.ToGray(bmp);
                _blurred = null; _blurredSigma = -1;
                _imageName = name;
                _mmPerPixel = null; _lastSeed = null; _outlineFromDetection = false;
                _canvas.Image = bmp;
                old?.Dispose();
                Text = $"Cutout Equivalent Diameter — {name}";
                _canvas.Mode = CanvasMode.Scale;
                _statusMsg.Text = $"Loaded {bmp.Width}×{bmp.Height}. Step 1: click two points on the ruler.";
            }
            finally { Cursor = Cursors.Default; }
            UpdateScaleInfo(); UpdateResults();
        }

        // ---------------- Scale ----------------

        private void PromptForDistance()
        {
            if (_canvas.ScaleA == null || _canvas.ScaleB == null)
            {
                _statusMsg.Text = "Draw the scale line on the ruler first.";
                _canvas.Mode = CanvasMode.Scale;
                return;
            }
            double? v = NumberPrompt.Show(this, "Known distance",
                "Distance between the two points on the ruler (mm):", _knownDistanceMm, 0.1, 10000);
            if (v is double d)
            {
                _knownDistanceMm = d;
                UpdateScaleFromLine();
                _statusMsg.Text = "Scale set. Step 2: click inside the light aperture to detect its edge.";
                _canvas.Mode = CanvasMode.Detect;
            }
            else if (_mmPerPixel == null)
            {
                _canvas.ClearScale();
            }
        }

        private void UpdateScaleFromLine()
        {
            if (_canvas.ScaleA is Pt a && _canvas.ScaleB is Pt b && Pt.Distance(a, b) > 1)
                _mmPerPixel = _knownDistanceMm / Pt.Distance(a, b);
            else
                _mmPerPixel = null;
            _canvas.ScaleLabel = _mmPerPixel != null ? $"{_knownDistanceMm:0.##} mm" : "";
            UpdateScaleInfo(); UpdateResults();
        }

        private void UpdateScaleInfo()
        {
            if (_mmPerPixel is double s && _canvas.ScaleA is Pt a && _canvas.ScaleB is Pt b)
                _scaleInfo.Text = $"{_knownDistanceMm:0.##} mm over {Pt.Distance(a, b):F1} px\n" +
                                  $"= {1 / s:F3} px/mm  ({25.4 / s:F0} dpi)\n" +
                                  "Drag the magenta end-marks (in Set scale mode) to refine.";
            else
                _scaleInfo.Text = "Not set. Click two well-separated ticks on the ruler (e.g. 0 and 100 mm), zooming in for accuracy.";
        }

        // ---------------- Detection ----------------

        private void RunDetection(Pt p, bool userClick)
        {
            if (_gray == null) return;
            int sx = (int)Math.Floor(p.X), sy = (int)Math.Floor(p.Y);
            double sigma = (double)_blur.Value;
            Cursor = Cursors.WaitCursor;
            try
            {
                if (_blurred == null || Math.Abs(_blurredSigma - sigma) > 1e-9)
                {
                    _blurred = Imaging.GaussianBlur(_gray, sigma);
                    _blurredSigma = sigma;
                }
                var opts = new DetectionOptions
                {
                    Threshold = _autoThreshold.Checked ? null : _threshold.Value,
                    BlurSigma = sigma,
                };
                var res = CutoutDetector.Detect(_gray, sx, sy, opts, _blurred);
                if (!res.Success)
                {
                    _statusMsg.Text = res.Message;
                    if (userClick) System.Media.SystemSounds.Beep.Play();
                    return;
                }
                _lastSeed = p;
                _suppressOutlineEvent = true;
                if (userClick) _canvas.SetOutline(res.Outline, p);
                else _canvas.ReplaceOutlineSilently(res.Outline);
                _suppressOutlineEvent = false;
                _outlineFromDetection = true;

                if (_autoThreshold.Checked)
                {
                    _suppressThresholdEvent = true;
                    _threshold.Value = Math.Max(_threshold.Minimum, Math.Min(_threshold.Maximum, res.ThresholdUsed));
                    _suppressThresholdEvent = false;
                }
                string levels = res.LightLevel > 0 ? $" Light {res.LightLevel}, dark {res.DarkLevel}." : "";
                _statusMsg.Text = res.Message + levels + " Use Edit outline (E) to adjust.";
                UpdateResults();
            }
            finally
            {
                _suppressOutlineEvent = false;
                Cursor = Cursors.Default;
            }
        }

        /// <summary>Live update when detection settings change, unless the user has hand-edited the outline.</summary>
        private void RedetectIfUnedited()
        {
            if (_lastSeed is Pt seed && _outlineFromDetection) RunDetection(seed, userClick: false);
            else if (_canvas.Outline != null) _statusMsg.Text = "Settings changed — click in the aperture again to re-detect.";
        }

        private void OnOutlineChanged()
        {
            if (!_suppressOutlineEvent) _outlineFromDetection = false;   // a manual change
            UpdateResults();
        }

        // ---------------- Results ----------------

        private void UpdateResults()
        {
            var o = _canvas.Outline;
            if (o == null || o.Count < 3)
            {
                _eqd.Text = "—";
                _details.Text = "No outline yet.";
                return;
            }
            double areaPx = Polygon.Area(o);
            if (_mmPerPixel is double s)
            {
                var m = FieldMetrics.From(o, s);
                _eqd.Text = $"{m.EquivalentDiameterCm:F2} cm";
                _details.Text =
                    $"Area: {m.AreaCm2:F2} cm²\n" +
                    $"Perimeter: {m.PerimeterCm:F2} cm\n" +
                    $"4A/P: {m.FourAOverPCm:F2} cm\n" +
                    $"Vertices: {o.Count}\n\n" +
                    "EqD = 2√(A/π): diameter of the circle with the same area as the outline.";
            }
            else
            {
                _eqd.Text = "set scale";
                _details.Text = $"Area: {areaPx:F0} px²\nVertices: {o.Count}\n\nSet the scale on the ruler to get cm.";
            }
        }

        private string ResultsText()
        {
            var o = _canvas.Outline;
            if (o == null || o.Count < 3 || _mmPerPixel is not double s) return "";
            var m = FieldMetrics.From(o, s);
            var sb = new StringBuilder();
            sb.AppendLine($"Image: {_imageName}");
            sb.AppendLine($"Scale: {1 / s:F3} px/mm ({_knownDistanceMm:0.##} mm reference)");
            sb.AppendLine($"Area: {m.AreaCm2:F2} cm²");
            sb.AppendLine($"Perimeter: {m.PerimeterCm:F2} cm");
            sb.AppendLine($"Equivalent diameter (2√(A/π)): {m.EquivalentDiameterCm:F2} cm");
            sb.AppendLine($"4A/P: {m.FourAOverPCm:F2} cm");
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm}");
            return sb.ToString();
        }

        private void CopyResults()
        {
            string t = ResultsText();
            if (t.Length == 0) { _statusMsg.Text = "Set the scale and outline first."; return; }
            Clipboard.SetText(t);
            _statusMsg.Text = "Results copied to the clipboard.";
        }

        private void SaveAnnotated()
        {
            var img = _canvas.Image;
            string text = ResultsText();
            if (img == null || text.Length == 0) { _statusMsg.Text = "Set the scale and outline first."; return; }
            using var dlg = new SaveFileDialog
            {
                Title = "Save annotated image", Filter = "PNG image|*.png",
                FileName = Path.GetFileNameWithoutExtension(_imageName) + "_EqD.png"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            using var outBmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
            outBmp.SetResolution(img.HorizontalResolution, img.VerticalResolution);
            using (var g = Graphics.FromImage(outBmp))
            {
                g.DrawImage(img, 0, 0, img.Width, img.Height);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float lineScale = Math.Max(1f, Math.Min(img.Width, img.Height) / 700f);
                _canvas.DrawOverlays(g, p => new PointF((float)p.X, (float)p.Y), lineScale, false, false);

                using var f = new Font(SystemFonts.DefaultFont.FontFamily, 10f * lineScale);
                var sz = g.MeasureString(text, f);
                using var bg = new SolidBrush(Color.FromArgb(215, 255, 255, 255));
                g.FillRectangle(bg, 8, 8, sz.Width + 8, sz.Height + 8);
                g.DrawString(text, f, Brushes.Black, 12, 12);
            }
            outBmp.Save(dlg.FileName, ImageFormat.Png);
            _statusMsg.Text = "Saved " + dlg.FileName;
        }

        // ---------------- Misc UI ----------------

        private void UpdateModeUi()
        {
            foreach (var kv in _modeButtons) kv.Value.Checked = kv.Key == _canvas.Mode;
            _help.Text = _canvas.Mode switch
            {
                CanvasMode.Scale => "SET SCALE: click a tick on the ruler, then a second tick far away. Enter the distance in mm. " +
                                     "Zoom with the mouse wheel; the magnifier shows the exact pixel. Drag an end-mark to refine.",
                CanvasMode.Detect => "AUTO-DETECT: click anywhere inside the light aperture. The edge is placed at the 50% grey level " +
                                     "between aperture and cutout (or at the manual threshold).",
                CanvasMode.Draw => "DRAW: click around the edge. Right-click/Backspace removes the last point. " +
                                   "Click the first point, double-click or press Enter to close. Esc cancels.",
                CanvasMode.Edit => "EDIT: drag a vertex to move it; click on an edge to add a vertex; right-click or Delete removes one. " +
                                   "Arrow keys nudge the highlighted vertex (Shift = 1 px). Ctrl+Z undoes.",
                _ => "PAN: drag to move the image. Mouse wheel zooms. Middle-drag or Space+drag pans in any mode."
            };
        }

        private void UpdateMousePos(Pt? p)
        {
            if (p is not Pt q) { _statusPos.Text = ""; return; }
            string s = $"x {q.X:F1}, y {q.Y:F1} px";
            if (_mmPerPixel is double mm) s += $"  ({q.X * mm:F1}, {q.Y * mm:F1} mm)";
            if (_gray != null)
            {
                int ix = (int)q.X, iy = (int)q.Y;
                if (ix >= 0 && iy >= 0 && ix < _gray.Width && iy < _gray.Height) s += $"  grey {_gray[ix, iy]}";
            }
            _statusPos.Text = s;
        }
    }

    /// <summary>Minimal numeric input dialog.</summary>
    internal static class NumberPrompt
    {
        public static double? Show(IWin32Window owner, string title, string prompt, double value, double min, double max)
        {
            using var f = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12)
            };
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
            var label = new Label { Text = prompt, AutoSize = true };
            var num = new NumericUpDown
            {
                Minimum = (decimal)min, Maximum = (decimal)max, DecimalPlaces = 2, Increment = 1,
                Value = (decimal)Math.Max(min, Math.Min(max, value)), Width = 120
            };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            flow.Controls.Add(label); flow.Controls.Add(num); flow.Controls.Add(buttons);
            f.Controls.Add(flow);
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.Shown += (_, _) => { num.Select(0, num.Text.Length); num.Focus(); };
            return f.ShowDialog(owner) == DialogResult.OK ? (double)num.Value : null;
        }
    }
}
