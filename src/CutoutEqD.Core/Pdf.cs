using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CutoutEqD.Core;

public enum PdfFont { Helvetica, HelveticaBold }

/// <summary>
/// Minimal single-page PDF writer: vector paths, text in the standard Helvetica fonts (no embedding
/// needed), one JPEG image, and fill transparency. Enough for the measurement report without
/// pulling in a PDF library. Coordinates are PDF points (1/72 in) with the origin at the bottom left.
/// </summary>
public sealed class PdfPage
{
    public const double A4Width = 595.28, A4Height = 841.89;

    private readonly StringBuilder _c = new();
    private readonly List<double> _alphas = new();
    private byte[]? _jpeg;
    private int _jpegWidth, _jpegHeight, _jpegComponents;

    public double Width { get; }
    public double Height { get; }

    public PdfPage(double width = A4Width, double height = A4Height) { Width = width; Height = height; }

    // ---------------- Graphics state ----------------

    public PdfPage Save() => Op("q");
    public PdfPage Restore() => Op("Q");
    public PdfPage Fill(byte r, byte g, byte b) => Op($"{C(r)} {C(g)} {C(b)} rg");
    public PdfPage Stroke(byte r, byte g, byte b) => Op($"{C(r)} {C(g)} {C(b)} RG");
    public PdfPage LineWidth(double w) => Op($"{N(w)} w");
    public PdfPage LineJoinRound() => Op("1 j 1 J");
    public PdfPage Dash(double on, double off) => Op($"[{N(on)} {N(off)}] 0 d");

    /// <summary>Sets fill and stroke opacity (0–1) for what follows, until <see cref="Restore"/>.</summary>
    public PdfPage Alpha(double alpha)
    {
        int i = _alphas.IndexOf(alpha);
        if (i < 0) { _alphas.Add(alpha); i = _alphas.Count - 1; }
        return Op($"/GS{i} gs");
    }

    // ---------------- Paths ----------------

    public PdfPage MoveTo(double x, double y) => Op($"{N(x)} {N(y)} m");
    public PdfPage LineTo(double x, double y) => Op($"{N(x)} {N(y)} l");
    public PdfPage Close() => Op("h");
    public PdfPage Rect(double x, double y, double w, double h) => Op($"{N(x)} {N(y)} {N(w)} {N(h)} re");

    /// <summary>Rectangle with rounded corners (Bézier approximation of quarter circles).</summary>
    public PdfPage RoundedRect(double x, double y, double w, double h, double r)
    {
        r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2));
        const double k = 0.5523;
        MoveTo(x + r, y);
        LineTo(x + w - r, y);
        Curve(x + w - r + k * r, y, x + w, y + r - k * r, x + w, y + r);
        LineTo(x + w, y + h - r);
        Curve(x + w, y + h - r + k * r, x + w - r + k * r, y + h, x + w - r, y + h);
        LineTo(x + r, y + h);
        Curve(x + r - k * r, y + h, x, y + h - r + k * r, x, y + h - r);
        LineTo(x, y + r);
        Curve(x, y + r - k * r, x + r - k * r, y, x + r, y);
        return Close();
    }

    public PdfPage Curve(double x1, double y1, double x2, double y2, double x3, double y3) =>
        Op($"{N(x1)} {N(y1)} {N(x2)} {N(y2)} {N(x3)} {N(y3)} c");

    public PdfPage FillPath() => Op("f");
    public PdfPage StrokePath() => Op("S");
    public PdfPage FillStrokePath() => Op("B");
    /// <summary>Intersects the clip region with the current path.</summary>
    public PdfPage Clip() => Op("W n");

    public PdfPage Line(double x0, double y0, double x1, double y1) => MoveTo(x0, y0).LineTo(x1, y1).StrokePath();

    // ---------------- Text ----------------

    /// <summary>Draws text with its baseline at y. √ and π are drawn from the Symbol font.</summary>
    public PdfPage Text(double x, double y, PdfFont font, double size, string text)
    {
        _c.Append("BT\n").Append(N(x)).Append(' ').Append(N(y)).Append(" Td\n");
        foreach (var (run, symbol) in Runs(text))
        {
            _c.Append(symbol ? "/F3" : font == PdfFont.HelveticaBold ? "/F2" : "/F1").Append(' ').Append(N(size)).Append(" Tf (");
            foreach (char ch in run) AppendEscaped(symbol ? SymbolCode(ch) : WinAnsi(ch));
            _c.Append(") Tj\n");
        }
        _c.Append("ET\n");
        return this;
    }

    public PdfPage TextRight(double right, double y, PdfFont font, double size, string text) =>
        Text(right - TextWidth(font, size, text), y, font, size, text);

    public PdfPage TextCentered(double cx, double y, PdfFont font, double size, string text) =>
        Text(cx - TextWidth(font, size, text) / 2, y, font, size, text);

    public static double TextWidth(PdfFont font, double size, string text)
    {
        double units = 0;
        foreach (char ch in text) units += CharWidth(font, ch);
        return units * size / 1000.0;
    }

    /// <summary>Splits text into lines no wider than maxWidth, breaking at spaces.</summary>
    public static List<string> Wrap(PdfFont font, double size, string text, double maxWidth)
    {
        var lines = new List<string>();
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            string candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && TextWidth(font, size, candidate) > maxWidth)
            {
                lines.Add(line.ToString());
                line.Clear().Append(word);
            }
            else line.Clear().Append(candidate);
        }
        if (line.Length > 0) lines.Add(line.ToString());
        return lines;
    }

    // ---------------- Image ----------------

    /// <summary>Draws a JPEG (baseline DCT, 1 or 3 components) into the given rectangle. One image per page.</summary>
    public PdfPage Image(byte[] jpeg, int pixelWidth, int pixelHeight, int components, double x, double y, double w, double h)
    {
        _jpeg = jpeg; _jpegWidth = pixelWidth; _jpegHeight = pixelHeight; _jpegComponents = components;
        return Op($"q {N(w)} 0 0 {N(h)} {N(x)} {N(y)} cm /Im0 Do Q");
    }

    // ---------------- Output ----------------

    public byte[] ToBytes(string title)
    {
        var objects = new List<byte[]>();
        int Add(string s) { objects.Add(Latin1(s)); return objects.Count; }

        const int catalog = 1, pages = 2, page = 3, contents = 4, f1 = 5, f2 = 6, f3 = 7, info = 8;
        Add($"<< /Type /Catalog /Pages {pages} 0 R >>");
        Add($"<< /Type /Pages /Kids [{page} 0 R] /Count 1 >>");

        var gs = new StringBuilder();
        for (int i = 0; i < _alphas.Count; i++) gs.Append($"/GS{i} << /Type /ExtGState /ca {N(_alphas[i])} /CA {N(_alphas[i])} >> ");
        int imageObj = _jpeg != null ? info + 1 : 0;
        Add($"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 {N(Width)} {N(Height)}] /Contents {contents} 0 R " +
            $"/Resources << /Font << /F1 {f1} 0 R /F2 {f2} 0 R /F3 {f3} 0 R >> /ExtGState << {gs}>>" +
            (imageObj > 0 ? $" /XObject << /Im0 {imageObj} 0 R >>" : "") + " >> >>");

        var content = Latin1(_c.ToString());
        objects.Add(Stream($"<< /Length {content.Length} >>", content));
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Add("<< /Type /Font /Subtype /Type1 /BaseFont /Symbol >>");
        Add($"<< /Title ({EscapeString(title)}) /Producer (Cutout Equivalent Diameter) " +
            $"/CreationDate (D:{DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}) >>");
        if (_jpeg != null)
        {
            string cs = _jpegComponents == 1 ? "/DeviceGray" : "/DeviceRGB";
            objects.Add(Stream($"<< /Type /XObject /Subtype /Image /Width {_jpegWidth} /Height {_jpegHeight} " +
                               $"/ColorSpace {cs} /BitsPerComponent 8 /Filter /DCTDecode /Length {_jpeg.Length} >>", _jpeg));
        }

        using var ms = new MemoryStream();
        void W(string s) { var b = Latin1(s); ms.Write(b, 0, b.Length); }
        W("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new long[objects.Count];
        for (int i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            W($"{i + 1} 0 obj\n");
            ms.Write(objects[i], 0, objects[i].Length);
            W("\nendobj\n");
        }
        long xref = ms.Position;
        W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (long o in offsets) W($"{o.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        W($"trailer\n<< /Size {objects.Count + 1} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    // ---------------- Helpers ----------------

    private PdfPage Op(string op) { _c.Append(op).Append('\n'); return this; }

    private static string N(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
    private static string C(byte v) => N(v / 255.0);

    private static byte[] Stream(string dict, byte[] data)
    {
        var head = Latin1(dict + "\nstream\n");
        var tail = Latin1("\nendstream");
        var all = new byte[head.Length + data.Length + tail.Length];
        Buffer.BlockCopy(head, 0, all, 0, head.Length);
        Buffer.BlockCopy(data, 0, all, head.Length, data.Length);
        Buffer.BlockCopy(tail, 0, all, head.Length + data.Length, tail.Length);
        return all;
    }

    private static byte[] Latin1(string s)
    {
        var b = new byte[s.Length];
        for (int i = 0; i < s.Length; i++) b[i] = (byte)(s[i] <= 0xFF ? s[i] : '?');
        return b;
    }

    private void AppendEscaped(byte b)
    {
        if (b == (byte)'(' || b == (byte)')' || b == (byte)'\\') _c.Append('\\').Append((char)b);
        else if (b < 32 || b > 126) _c.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
        else _c.Append((char)b);
    }

    private static string EscapeString(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s)
        {
            byte b = WinAnsi(ch);
            if (b == (byte)'(' || b == (byte)')' || b == (byte)'\\') sb.Append('\\').Append((char)b);
            else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append((char)b);
        }
        return sb.ToString();
    }

    private static bool IsSymbol(char ch) => ch == '√' || ch == 'π';
    private static byte SymbolCode(char ch) => ch == '√' ? (byte)0xD6 : (byte)0x70;

    private static IEnumerable<(string run, bool symbol)> Runs(string text)
    {
        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i == text.Length || IsSymbol(text[i]) != IsSymbol(text[start]))
            {
                if (i > start) yield return (text.Substring(start, i - start), IsSymbol(text[start]));
                start = i;
            }
        }
    }

    /// <summary>Unicode → WinAnsiEncoding (Windows-1252) for the characters this app uses.</summary>
    private static byte WinAnsi(char ch)
    {
        if (ch < 0x80 || (ch >= 0xA0 && ch <= 0xFF)) return (byte)ch;
        return ch switch
        {
            '€' => 0x80, '…' => 0x85, '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94,
            '•' => 0x95, '–' => 0x96, '—' => 0x97, '−' => (byte)'-', _ => (byte)'?',
        };
    }

    // Advance widths (1/1000 em) from the Adobe Helvetica / Helvetica-Bold AFM files, ASCII 32–126.
    private static readonly short[] HelveticaWidths =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    };

    private static readonly short[] HelveticaBoldWidths =
    {
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    };

    private static double CharWidth(PdfFont font, char ch)
    {
        if (IsSymbol(ch)) return 549;
        if (ch >= 32 && ch <= 126) return (font == PdfFont.HelveticaBold ? HelveticaBoldWidths : HelveticaWidths)[ch - 32];
        return ch switch
        {
            '²' => 333, '³' => 333, '·' => 278, '×' => 584, '°' => 400, '±' => 584, 'µ' => font == PdfFont.HelveticaBold ? 611 : 556,
            '—' => 1000, '…' => 1000, '–' => 556, '−' => 333, '‘' or '’' => font == PdfFont.HelveticaBold ? 278 : 222,
            '“' or '”' => font == PdfFont.HelveticaBold ? 500 : 333, '•' => 350,
            _ => 556,
        };
    }
}
