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
        private Bitmap? _originalImage;                          // as loaded, for "Reset image"
        private ImageTransform _transform = ImageTransform.Identity;   // original → current pixel coordinates
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
        private readonly Label _scaleHint = Hint("");
        private readonly MetricRow _scaleRef = new("Reference");
        private readonly MetricRow _scaleRes = new("Resolution");
        private readonly ToggleSwitch _autoThreshold = new("Automatic threshold", on: true);
        private readonly TrackBar _threshold = new() { Minimum = 1, Maximum = 255, Value = 128, TickFrequency = 16, TickStyle = TickStyle.None, Width = 210, Enabled = false, Margin = new Padding(0, 0, 0, 0) };
        private readonly Label _thresholdValue = new() { AutoSize = false, Size = new Size(36, 26), Text = "128", TextAlign = ContentAlignment.MiddleRight, Font = Theme.UiFont, ForeColor = Theme.Text };
        private readonly NumericUpDown _blur = new() { Minimum = 0, Maximum = 5, DecimalPlaces = 1, Increment = 0.5m, Value = 1.0m, Width = 64, Font = Theme.UiFont, BorderStyle = BorderStyle.FixedSingle };
        private readonly ToggleSwitch _softDrag = new("Soft drag (moves neighbours)");
        private readonly TrackBar _softRadius = new() { Minimum = 4, Maximum = 200, Value = 40, TickStyle = TickStyle.None, Width = 150, Enabled = false, Margin = Padding.Empty, SmallChange = 2, LargeChange = 10 };
        private readonly Label _softRadiusValue = new() { AutoSize = false, Size = new Size(Card.ContentWidth - 150, 26), TextAlign = ContentAlignment.MiddleRight, Font = Theme.UiFont, ForeColor = Theme.Text };
        private readonly ToggleSwitch _loupe = new("Show magnifier", on: true);
        private readonly Label _eqd = new() { AutoSize = true, Font = Theme.ResultFont, ForeColor = Theme.Accent, Text = "—", Margin = new Padding(0, 0, 0, 6) };
        private readonly MetricRow _area = new("Area");
        private readonly MetricRow _perimeter = new("Perimeter");
        private readonly MetricRow _fourAP = new("4A/P");
        private readonly MetricRow _vertices = new("Vertices");
        private readonly Label _resultNote = Hint("");
        private readonly Callout _help = new();
        private Card _scaleCard = null!, _outlineCard = null!, _resultCard = null!;
        private readonly ToolStripStatusLabel _statusMsg = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel _statusPos = new() { AutoSize = false, Width = 380, TextAlign = ContentAlignment.MiddleRight };
        private readonly ToolStripStatusLabel _statusZoom = new() { AutoSize = false, Width = 90 };
        private readonly ToolStripStatusLabel _statusVersion = new() { ForeColor = Theme.TextMuted, Text = "v" + BuildInfo.Full, ToolTipText = "Build version" };
        private readonly Dictionary<CanvasMode, ToolStripButton> _modeButtons = new();
        private readonly Dictionary<CanvasMode, (Image? normal, Image? active)> _modeIcons = new();
        private readonly Dictionary<CanvasMode, ModernButton> _sideModeButtons = new();
        private ToolStripButton _undoButton = null!, _redoButton = null!, _resetToolButton = null!;
        private ModernButton _resetButton = null!;

        public MainForm()
        {
            SuspendLayout();
            // Layout below is in 96-dpi pixels; WinForms scales it to the monitor's DPI.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = $"Cutout Equivalent Diameter {BuildInfo.Version}";
            Font = Theme.UiFont;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Width = 1320; Height = 900;
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;

            var renderer = new ModernToolStripRenderer();
            BuildToolStrip(out var tools);
            tools.Renderer = renderer;
            var side = BuildSidePanel();
            var status = new StatusStrip { Renderer = renderer, SizingGrip = false, Font = Theme.SmallFont, Padding = new Padding(8, 2, 8, 2) };
            status.Items.AddRange(new ToolStripItem[] { _statusMsg, _statusZoom, _statusPos, _statusVersion });

            Controls.Add(_canvas);
            Controls.Add(side);
            Controls.Add(tools);
            Controls.Add(status);

            _canvas.DetectRequested += (_, p) => RunDetection(p, userClick: true);
            _canvas.CropRequested += (_, r) => CropImage(r);
            _canvas.OutlineChanged += (_, _) => OnOutlineChanged();
            _canvas.ScaleLineChanged += (_, _) => UpdateScaleFromLine();
            _canvas.ScaleLineCompleted += (_, _) => PromptForDistance();
            _canvas.StatusMessage += (_, m) => _statusMsg.Text = m;
            _canvas.ModeChanged += (_, _) => UpdateModeUi();
            _canvas.MouseImagePositionChanged += (_, p) => UpdateMousePos(p);
            _canvas.Paint += (_, _) =>
            {
                _statusZoom.Text = $"Zoom {_canvas.Zoom * 100:F0}%";
                _undoButton.Enabled = _canvas.CanUndo;
                _redoButton.Enabled = _canvas.CanRedo;
            };

            _autoThreshold.CheckedChanged += (_, _) => { _threshold.Enabled = !_autoThreshold.Checked; RedetectIfUnedited(); };
            _threshold.ValueChanged += (_, _) =>
            {
                _thresholdValue.Text = _threshold.Value.ToString(CultureInfo.InvariantCulture);
                if (!_suppressThresholdEvent) RedetectIfUnedited();
            };
            _blur.ValueChanged += (_, _) => RedetectIfUnedited();
            _softDrag.CheckedChanged += (_, _) => { _canvas.SoftDrag = _softRadius.Enabled = _softDrag.Checked; UpdateSoftRadiusLabel(); };
            _softRadius.ValueChanged += (_, _) => { _canvas.SoftDragRadius = _softRadius.Value; UpdateSoftRadiusLabel(); };
            _canvas.SoftDragRadius = _softRadius.Value;
            _loupe.CheckedChanged += (_, _) => { _canvas.ShowLoupe = _loupe.Checked; _canvas.Invalidate(); };

            DragEnter += (_, e) =>
            {
                if (e.Data?.GetDataPresent(DataFormats.FileDrop) != true) return;
                e.Effect = DragDropEffects.Copy;
                _canvas.DropHighlight = true;
            };
            DragLeave += (_, _) => _canvas.DropHighlight = false;
            DragDrop += (_, e) =>
            {
                _canvas.DropHighlight = false;
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) LoadImageFile(files[0]);
            };

            UpdateModeUi();
            UpdateResults();
            UpdateScaleInfo();
            UpdateResetButtons();
            _statusMsg.Text = "Open or paste a scanned image of the cutout with a ruler alongside.";
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // After DPI scaling the default size can exceed small or high-scale screens.
            var wa = Screen.FromControl(this).WorkingArea;
            if (Width > wa.Width || Height > wa.Height)
            {
                Size = new Size(Math.Min(Width, wa.Width), Math.Min(Height, wa.Height));
                Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
            }
        }

        // ---------------- UI construction ----------------

        private static Label Hint(string text) => new()
        {
            Text = text, AutoSize = true, MaximumSize = new Size(Card.ContentWidth, 0), ForeColor = Theme.TextMuted,
            Font = Theme.SmallFont, Margin = new Padding(0, 2, 0, 8)
        };

        private static Label Section(string text) => new()
        {
            Text = text.ToUpperInvariant(), AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.SectionFont,
            Margin = new Padding(0, 10, 0, 4)
        };

        private Control SoftRadiusRow()
        {
            var caption = new Label
            {
                Text = "Soft drag reach", AutoSize = true, Font = Theme.SmallFont, ForeColor = Theme.TextMuted, Margin = new Padding(0, 2, 0, 0)
            };
            var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            row.Controls.Add(_softRadius); row.Controls.Add(_softRadiusValue);
            var box = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            box.Controls.Add(caption); box.Controls.Add(row);
            return box;
        }

        /// <summary>Reach in image pixels, plus millimetres once the scale is set.</summary>
        private void UpdateSoftRadiusLabel()
        {
            int px = _softRadius.Value;
            _softRadiusValue.Text = _mmPerPixel is double mm ? $"{px} px · {px * mm:0.#} mm" : $"{px} px";
            _softRadiusValue.ForeColor = _softRadius.Enabled ? Theme.Text : Theme.TextDisabled;
        }

        private int IconPx => (int)Math.Round(18 * DeviceDpi / 96.0);

        private ToolStripButton ToolButton(string text, string glyph, string tip, Action onClick, bool iconOnly = false)
        {
            var img = Theme.Glyph(glyph, Theme.Text, IconPx);
            var b = new ToolStripButton(text, img, (_, _) => onClick())
            {
                ToolTipText = tip,
                DisplayStyle = img == null ? ToolStripItemDisplayStyle.Text
                             : iconOnly ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                Padding = new Padding(6, 4, 6, 4),
                Margin = new Padding(1, 0, 1, 0),
            };
            return b;
        }

        private void BuildToolStrip(out ToolStrip tools)
        {
            tools = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden, ImageScalingSize = new Size(18, 18), Padding = new Padding(8, 6, 8, 6),
                Font = Theme.UiFont, BackColor = Theme.Surface
            };
            tools.Items.Add(ToolButton("Open…", Theme.Icons.Open, "Open image (Ctrl+O)", OpenImage));
            tools.Items.Add(ToolButton("Paste", Theme.Icons.Paste, "Paste image from clipboard (Ctrl+V)", PasteImage));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(ToolButton("Rotate left", Theme.Icons.RotateLeft, "Rotate 90° counter-clockwise (Shift+R)", () => RotateImage(clockwise: false), iconOnly: true));
            tools.Items.Add(ToolButton("Rotate right", Theme.Icons.RotateRight, "Rotate 90° clockwise (R)", () => RotateImage(clockwise: true), iconOnly: true));
            AddMode(tools, CanvasMode.Crop, "Crop", Theme.Icons.Crop, "C", "Drag a box around the area to keep, then press Enter");
            _resetToolButton = ToolButton("Reset image", Theme.Icons.Reset, "Undo all rotations and crops", ResetImage, iconOnly: true);
            tools.Items.Add(_resetToolButton);
            tools.Items.Add(new ToolStripSeparator());

            AddMode(tools, CanvasMode.Scale, "Set scale", Theme.Icons.Ruler, "S", "Click two points on the ruler, then enter the distance between them");
            AddMode(tools, CanvasMode.Detect, "Auto-detect", Theme.Icons.Detect, "A", "Click inside the light aperture to find its edge");
            AddMode(tools, CanvasMode.Draw, "Draw outline", Theme.Icons.Draw, "D", "Click around the edge to draw an outline manually");
            AddMode(tools, CanvasMode.Edit, "Edit outline", Theme.Icons.Edit, "E", "Drag vertices; click an edge to add one; right-click to delete");
            AddMode(tools, CanvasMode.Pan, "Pan", Theme.Icons.Pan, "H", "Drag to move the image (middle-drag or Space+drag works in any mode)");
            tools.Items.Add(new ToolStripSeparator());

            _undoButton = ToolButton("Undo", Theme.Icons.Undo, "Undo outline change (Ctrl+Z)", () => _canvas.Undo(), iconOnly: true);
            _redoButton = ToolButton("Redo", Theme.Icons.Redo, "Redo (Ctrl+Y)", () => _canvas.Redo(), iconOnly: true);
            tools.Items.Add(_undoButton);
            tools.Items.Add(_redoButton);
            tools.Items.Add(ToolButton("Clear outline", Theme.Icons.Clear, "Remove the outline",
                () => { _lastSeed = null; _outlineFromDetection = false; _canvas.SetOutline(null); }));

            // Right-aligned view controls (added right to left).
            foreach (var b in new[]
            {
                ToolButton("Fit", Theme.Icons.Fit, "Zoom to fit (F)", () => _canvas.ZoomToFit()),
                ToolButton("+", Theme.Icons.ZoomIn, "Zoom in (mouse wheel)", () => _canvas.ZoomBy(1.25), iconOnly: true),
                ToolButton("−", Theme.Icons.ZoomOut, "Zoom out (mouse wheel)", () => _canvas.ZoomBy(1 / 1.25), iconOnly: true),
            })
            {
                b.Alignment = ToolStripItemAlignment.Right;
                tools.Items.Add(b);
            }
        }

        private void AddMode(ToolStrip tools, CanvasMode mode, string text, string glyph, string key, string tip)
        {
            var b = ToolButton(text, glyph, $"{tip} ({key})", () => _canvas.Mode = mode);
            _modeIcons[mode] = (b.Image, Theme.Glyph(glyph, Theme.Accent, IconPx));
            _modeButtons[mode] = b;
            tools.Items.Add(b);
        }

        private Control BuildSidePanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, Width = 316, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, Padding = new Padding(12, 12, 4, 12), BackColor = Theme.Window
            };

            ModernButton Btn(string text, string glyph, EventHandler onClick, bool primary = false)
            {
                var b = new ModernButton(text, glyph, primary) { Width = Card.ContentWidth };
                b.Click += onClick;
                return b;
            }
            ModernButton ModeBtn(CanvasMode mode, string text, string glyph)
            {
                var b = Btn(text, glyph, (_, _) => _canvas.Mode = mode);
                _sideModeButtons[mode] = b;
                return b;
            }

            const int half = (Card.ContentWidth - 6) / 2;
            var rotateRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
            var rotL = Btn("Rotate left", Theme.Icons.RotateLeft, (_, _) => RotateImage(clockwise: false));
            var rotR = Btn("Rotate right", Theme.Icons.RotateRight, (_, _) => RotateImage(clockwise: true));
            rotL.Width = rotR.Width = half;
            rotL.Margin = new Padding(0, 0, 6, 6);
            rotateRow.Controls.Add(rotL); rotateRow.Controls.Add(rotR);
            _resetButton = Btn("Reset to original", Theme.Icons.Reset, (_, _) => ResetImage());
            panel.Controls.Add(new Card("Image", null,
                rotateRow,
                ModeBtn(CanvasMode.Crop, "Crop…", Theme.Icons.Crop),
                _resetButton));

            _scaleRef.Margin = new Padding(0, 0, 0, 0);
            _scaleRes.Margin = new Padding(0, 0, 0, 8);
            _scaleCard = new Card("Scale", "1",
                _scaleRef, _scaleRes, _scaleHint,
                ModeBtn(CanvasMode.Scale, "Set scale on ruler", Theme.Icons.Ruler),
                Btn("Change known distance…", Theme.Icons.Number, (_, _) => PromptForDistance()));
            panel.Controls.Add(_scaleCard);

            var thrRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            thrRow.Controls.Add(_threshold); thrRow.Controls.Add(_thresholdValue);
            var blurRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
            blurRow.Controls.Add(new Label
            {
                Text = "Noise smoothing (px)", AutoSize = false, Size = new Size(Card.ContentWidth - 70, 26),
                TextAlign = ContentAlignment.MiddleLeft, Font = Theme.UiFont, ForeColor = Theme.Text, Margin = Padding.Empty
            });
            blurRow.Controls.Add(_blur);
            _outlineCard = new Card("Outline", "2",
                ModeBtn(CanvasMode.Detect, "Auto-detect (click in aperture)", Theme.Icons.Detect),
                ModeBtn(CanvasMode.Draw, "Draw outline manually", Theme.Icons.Draw),
                ModeBtn(CanvasMode.Edit, "Edit outline", Theme.Icons.Edit),
                Section("Detection"), _autoThreshold, thrRow, blurRow,
                Section("Editing"), _softDrag, SoftRadiusRow(), _loupe);
            panel.Controls.Add(_outlineCard);

            var caption = new Label { Text = "Equivalent diameter", AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.UiFont, Margin = Padding.Empty };
            _vertices.Margin = new Padding(0, 0, 0, 8);
            _resultCard = new Card("Result", "3",
                caption, _eqd, _area, _perimeter, _fourAP, _vertices, _resultNote,
                Btn("Copy results", Theme.Icons.Copy, (_, _) => CopyResults(), primary: true),
                Btn("Save PDF report…", Theme.Icons.Pdf, (_, _) => SaveReport()));
            panel.Controls.Add(_resultCard);

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
                case Keys.Control | Keys.S: SaveReport(); return true;
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
                    case Keys.C: _canvas.Mode = CanvasMode.Crop; return true;
                    case Keys.R: RotateImage(clockwise: (keyData & Keys.Shift) == 0); return true;
                    case Keys.OemOpenBrackets when _canvas.Mode == CanvasMode.Edit && _softDrag.Checked:
                        _softRadius.Value = Math.Max(_softRadius.Minimum, _softRadius.Value - SoftRadiusStep()); return true;
                    case Keys.OemCloseBrackets when _canvas.Mode == CanvasMode.Edit && _softDrag.Checked:
                        _softRadius.Value = Math.Min(_softRadius.Maximum, _softRadius.Value + SoftRadiusStep()); return true;
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
                _originalImage?.Dispose();
                _originalImage = (Bitmap)bmp.Clone();
                _transform = ImageTransform.Identity;
                UpdateResetButtons();
                Text = $"Cutout Equivalent Diameter {BuildInfo.Version} — {name}";
                _canvas.Mode = CanvasMode.Scale;
                _statusMsg.Text = $"Loaded {bmp.Width}×{bmp.Height}. Step 1: click two points on the ruler.";
            }
            finally { Cursor = Cursors.Default; }
            UpdateScaleInfo(); UpdateResults();
        }

        // ---------------- Rotate / crop ----------------

        private void RotateImage(bool clockwise)
        {
            if (_canvas.Image is not Bitmap img) return;
            var rotated = (Bitmap)img.Clone();
            rotated.RotateFlip(clockwise ? RotateFlipType.Rotate90FlipNone : RotateFlipType.Rotate270FlipNone);
            ApplyImageChange(rotated, clockwise ? ImageTransform.RotateClockwise(img.Width, img.Height)
                                                : ImageTransform.RotateCounterClockwise(img.Width, img.Height));
            _statusMsg.Text = $"Rotated 90° {(clockwise ? "clockwise" : "counter-clockwise")}. The outline and scale line moved with the image.";
        }

        private void CropImage(Rectangle r)
        {
            if (_canvas.Image is not Bitmap img) return;
            r.Intersect(new Rectangle(0, 0, img.Width, img.Height));
            if (r.Width < 4 || r.Height < 4) return;
            var cropped = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            cropped.SetResolution(img.HorizontalResolution, img.VerticalResolution);
            using (var g = Graphics.FromImage(cropped))
                g.DrawImage(img, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
            ApplyImageChange(cropped, ImageTransform.Crop(r.X, r.Y));
            _canvas.Mode = _mmPerPixel == null ? CanvasMode.Scale : CanvasMode.Detect;
            _statusMsg.Text = $"Cropped to {r.Width}×{r.Height} px. " +
                              (_mmPerPixel == null ? "Next: set the scale on the ruler." : "The scale is unchanged.");
        }

        private void ResetImage()
        {
            if (_originalImage == null || _transform.IsIdentity) return;
            ApplyImageChange((Bitmap)_originalImage.Clone(), _transform.Inverse());
            _statusMsg.Text = "Back to the original image.";
        }

        /// <summary>Shows a rotated/cropped image and carries the outline, scale line and seed across.</summary>
        private void ApplyImageChange(Bitmap bmp, ImageTransform t)
        {
            var old = _canvas.Image;
            Cursor = Cursors.WaitCursor;
            try
            {
                _gray = BitmapUtil.ToGray(bmp);
                _blurred = null; _blurredSigma = -1;
                if (_lastSeed is Pt seed)
                {
                    var p = t.Apply(seed);
                    bool inside = p.X >= 0 && p.Y >= 0 && p.X < bmp.Width && p.Y < bmp.Height;
                    _lastSeed = inside ? p : null;
                }
                bool fromDetection = _outlineFromDetection && _lastSeed != null;
                _suppressOutlineEvent = true;
                try { _canvas.ReplaceImage(bmp, t); }
                finally { _suppressOutlineEvent = false; }
                _outlineFromDetection = fromDetection;
                old?.Dispose();
                _transform = _transform.Then(t);
                UpdateResetButtons();
            }
            finally { Cursor = Cursors.Default; }
            UpdateScaleInfo(); UpdateResults();
        }

        private void UpdateResetButtons() => _resetButton.Enabled = _resetToolButton.Enabled = !_transform.IsIdentity;

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

        /// <summary>[ and ] change the reach by about 10%, so small and large radii both adjust comfortably.</summary>
        private int SoftRadiusStep() => Math.Max(1, (int)Math.Round(_softRadius.Value * 0.1));

        private void UpdateScaleInfo()
        {
            bool set = _mmPerPixel is double && _canvas.ScaleA is Pt && _canvas.ScaleB is Pt;
            if (_mmPerPixel is double s && _canvas.ScaleA is Pt a && _canvas.ScaleB is Pt b)
            {
                _scaleRef.Value = $"{_knownDistanceMm:0.##} mm over {Pt.Distance(a, b):F1} px";
                _scaleRes.Value = $"{1 / s:F3} px/mm  ·  {25.4 / s:F0} dpi";
                _scaleHint.Text = "Drag the magenta end-marks (in Set scale mode) to refine.";
            }
            else
                _scaleHint.Text = "Not set. Click two well-separated ticks on the ruler (e.g. 0 and 100 mm), zooming in for accuracy.";
            _scaleRef.Visible = _scaleRes.Visible = set;
            _scaleCard.Completed = set;
            UpdateSoftRadiusLabel();
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
            bool hasOutline = o != null && o.Count >= 3;
            _outlineCard.Completed = hasOutline;
            _resultCard.Completed = hasOutline && _mmPerPixel != null;
            if (o == null || o.Count < 3)
            {
                _eqd.Text = "—";
                _area.Value = _perimeter.Value = _fourAP.Value = _vertices.Value = "—";
                _resultNote.Text = "No outline yet.";
                return;
            }
            double areaPx = Polygon.Area(o);
            _vertices.Value = o.Count.ToString(CultureInfo.CurrentCulture);
            if (_mmPerPixel is double s)
            {
                var m = FieldMetrics.From(o, s);
                _eqd.Text = $"{m.EquivalentDiameterCm:F2} cm";
                _area.Value = $"{m.AreaCm2:F2} cm²";
                _perimeter.Value = $"{m.PerimeterCm:F2} cm";
                _fourAP.Value = $"{m.FourAOverPCm:F2} cm";
                _resultNote.Text = "EqD = 2√(A/π): diameter of the circle with the same area as the outline.";
            }
            else
            {
                _eqd.Text = "—";
                _area.Value = $"{areaPx:F0} px²";
                _perimeter.Value = _fourAP.Value = "—";
                _resultNote.Text = "Set the scale on the ruler to get cm.";
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

        private void SaveReport()
        {
            var img = _canvas.Image;
            var o = _canvas.Outline;
            if (img == null || o == null || o.Count < 3 || _mmPerPixel is not double mm ||
                _canvas.ScaleA is not Pt a || _canvas.ScaleB is not Pt b)
            {
                _statusMsg.Text = "Set the scale and outline first.";
                return;
            }
            using var dlg = new SaveFileDialog
            {
                Title = "Save PDF report", Filter = "PDF document|*.pdf", DefaultExt = "pdf",
                FileName = Path.GetFileNameWithoutExtension(_imageName) + "_EqD.pdf"
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                var jpeg = BitmapUtil.ToJpeg(img, 3000, out int jw, out int jh);
                var pdf = ReportBuilder.Build(new ReportData
                {
                    ImageName = _imageName,
                    Date = DateTime.Now,
                    AppVersion = BuildInfo.Full,
                    Jpeg = jpeg, JpegWidth = jw, JpegHeight = jh,
                    ImageWidth = img.Width, ImageHeight = img.Height,
                    Outline = new List<Pt>(o),
                    ScaleA = a, ScaleB = b,
                    KnownDistanceMm = _knownDistanceMm, MmPerPixel = mm,
                    ThresholdDescription = ThresholdDescription(),
                    SmoothingDescription = _outlineFromDetection ? $"{_blur.Value:0.0} px" : "",
                });
                File.WriteAllBytes(dlg.FileName, pdf);
                _statusMsg.Text = "Saved " + dlg.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the report:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { Cursor = Cursors.Default; }
        }

        private string ThresholdDescription()
        {
            if (!_outlineFromDetection) return "n/a (outline drawn or edited)";
            return $"{_threshold.Value} ({(_autoThreshold.Checked ? "automatic, 50%" : "manual")})";
        }

        // ---------------- Misc UI ----------------

        private void UpdateModeUi()
        {
            foreach (var kv in _modeButtons)
            {
                bool active = kv.Key == _canvas.Mode;
                kv.Value.Checked = active;
                var (normal, activeIcon) = _modeIcons[kv.Key];
                kv.Value.Image = active ? activeIcon : normal;
            }
            foreach (var kv in _sideModeButtons) kv.Value.Selected = kv.Key == _canvas.Mode;
            var (heading, body) = _canvas.Mode switch
            {
                CanvasMode.Scale => ("Set scale", "Click a tick on the ruler, then a second tick far away. Enter the distance in mm. " +
                                     "Zoom with the mouse wheel; the magnifier shows the exact pixel. Drag an end-mark to refine."),
                CanvasMode.Detect => ("Auto-detect", "Click anywhere inside the light aperture. The edge is placed at the 50% grey level " +
                                      "between aperture and cutout (or at the manual threshold)."),
                CanvasMode.Draw => ("Draw outline", "Click around the edge. Right-click/Backspace removes the last point. " +
                                    "Click the first point, double-click or press Enter to close. Esc cancels."),
                CanvasMode.Edit => ("Edit outline", "Drag a vertex to move it; click on an edge to add a vertex; right-click or Delete removes one. " +
                                    "Arrow keys nudge the highlighted vertex (Shift = 1 px). With soft drag on, the blue circle shows its reach; [ and ] resize it. Ctrl+Z undoes."),
                CanvasMode.Crop => ("Crop", "Drag a box around the cutout and ruler. Press Enter or double-click inside the box to crop; " +
                                    "Esc cancels. The outline and scale line are kept. Reset to original undoes all crops and rotations."),
                _ => ("Pan", "Drag to move the image. Mouse wheel zooms. Middle-drag or Space+drag pans in any mode.")
            };
            _help.SetContent(heading, body);
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
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20, 16, 20, 16),
                AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi,
                Font = Theme.UiFont, BackColor = Theme.Surface, ForeColor = Theme.Text
            };
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            var label = new Label { Text = prompt, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
            var num = new NumericUpDown
            {
                Minimum = (decimal)min, Maximum = (decimal)max, DecimalPlaces = 2, Increment = 1,
                Value = (decimal)Math.Max(min, Math.Min(max, value)), Width = 140, BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.InputFont, Margin = new Padding(0, 0, 6, 0)
            };
            var unit = new Label { Text = "mm", AutoSize = true, ForeColor = Theme.TextMuted, Padding = new Padding(0, 6, 0, 0) };
            var input = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 16) };
            input.Controls.Add(num); input.Controls.Add(unit);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            var ok = new ModernButton("OK", primary: true) { DialogResult = DialogResult.OK, Width = 96, Margin = new Padding(0, 0, 8, 0) };
            var cancel = new ModernButton("Cancel") { DialogResult = DialogResult.Cancel, Width = 96, Margin = Padding.Empty };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            flow.Controls.Add(label); flow.Controls.Add(input); flow.Controls.Add(buttons);
            f.Controls.Add(flow);
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.Shown += (_, _) => { num.Select(0, num.Text.Length); num.Focus(); };
            return f.ShowDialog(owner) == DialogResult.OK ? (double)num.Value : null;
        }
    }
}
