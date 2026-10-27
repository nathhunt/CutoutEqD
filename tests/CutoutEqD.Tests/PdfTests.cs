using System.Globalization;
using System.Text;
using static CutoutEqD.Tests.TestImages;

namespace CutoutEqD.Tests;

/// <summary>Structural checks shared by the PDF tests.</summary>
internal static class PdfAssert
{
    public static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    /// <summary>Header, trailer, and a cross-reference table whose every offset lands on its object.</summary>
    public static void WellFormed(byte[] pdf)
    {
        string text = Latin1(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF", text.TrimEnd());

        int sx = text.LastIndexOf("startxref", StringComparison.Ordinal);
        int xref = int.Parse(text.Substring(sx + 10).Split('\n')[0], CultureInfo.InvariantCulture);
        Assert.Equal("xref", text.Substring(xref, 4));

        var lines = text.Substring(xref).Split('\n');
        int count = int.Parse(lines[1].Split(' ')[1], CultureInfo.InvariantCulture);
        Assert.Equal("0000000000 65535 f ", lines[2]);
        for (int i = 1; i < count; i++)
        {
            int off = int.Parse(lines[2 + i].Substring(0, 10), CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i} 0 obj", text.Substring(off));
        }
        Assert.Contains($"/Size {count}", text);
    }
}

public class PdfPageTests
{
    [Fact]
    public void TextWidth_uses_Helvetica_metrics()
    {
        Assert.Equal(0.556 * 10, PdfPage.TextWidth(PdfFont.Helvetica, 10, "a"), 9);
        Assert.Equal(0.611 * 10, PdfPage.TextWidth(PdfFont.HelveticaBold, 10, "b"), 9);
        Assert.Equal(0, PdfPage.TextWidth(PdfFont.Helvetica, 12, ""));
    }

    [Fact]
    public void TextWidth_scales_with_size_and_adds_up()
    {
        double w1 = PdfPage.TextWidth(PdfFont.Helvetica, 10, "EqD");
        Assert.Equal(2 * w1, PdfPage.TextWidth(PdfFont.Helvetica, 20, "EqD"), 9);
        Assert.Equal(PdfPage.TextWidth(PdfFont.Helvetica, 10, "Eq") + PdfPage.TextWidth(PdfFont.Helvetica, 10, "D"), w1, 9);
    }

    [Fact]
    public void Wrap_keeps_lines_within_width_and_every_word()
    {
        const string text = "The edge is placed at the fifty percent grey level between the aperture and the plate.";
        var lines = PdfPage.Wrap(PdfFont.Helvetica, 9, text, 120);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(PdfPage.TextWidth(PdfFont.Helvetica, 9, l) <= 120, l));
        Assert.Equal(text, string.Join(" ", lines));
    }

    [Fact]
    public void Wrap_does_not_split_a_word_longer_than_the_line() =>
        Assert.Equal(new[] { "Supercalifragilistic" }, PdfPage.Wrap(PdfFont.Helvetica, 12, "Supercalifragilistic", 10));

    [Fact]
    public void Empty_page_is_well_formed()
    {
        var pdf = new PdfPage().ToBytes("Empty");
        PdfAssert.WellFormed(pdf);
        string text = PdfAssert.Latin1(pdf);
        Assert.Contains("/MediaBox [0 0 595.28 841.89]", text);
        Assert.DoesNotContain("/XObject", text);
    }

    [Fact]
    public void Text_is_escaped_and_encoded()
    {
        var pg = new PdfPage();
        pg.Text(10, 10, PdfFont.Helvetica, 10, @"a (b) \ c – 5 cm²");
        string text = PdfAssert.Latin1(pg.ToBytes("t"));
        Assert.Contains(@"(a \(b\) \\ c \226 5 cm\262) Tj", text);   // en dash → WinAnsi 0x96, ² → 0xB2
    }

    [Fact]
    public void Square_root_and_pi_use_the_Symbol_font()
    {
        var pg = new PdfPage();
        pg.Text(0, 0, PdfFont.Helvetica, 10, "2√(A/π)");
        string text = PdfAssert.Latin1(pg.ToBytes("t"));
        Assert.Contains("/F1 10 Tf (2) Tj", text);
        Assert.Contains(@"/F3 10 Tf (\326) Tj", text);   // √ = Symbol 0xD6
        Assert.Contains("/F3 10 Tf (p) Tj", text);       // π = Symbol 0x70
    }

    [Fact]
    public void Numbers_use_invariant_formatting()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var pg = new PdfPage();
            pg.MoveTo(1.5, 2.25).LineTo(3.125, 4);
            string text = PdfAssert.Latin1(pg.ToBytes("t"));
            Assert.Contains("1.5 2.25 m", text);
            Assert.Contains("3.125 4 l", text);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Alpha_adds_a_graphics_state()
    {
        var pg = new PdfPage();
        pg.Alpha(0.5);
        Assert.Contains("/ca 0.5 /CA 0.5", PdfAssert.Latin1(pg.ToBytes("t")));
    }

    [Fact]
    public void Image_is_embedded_as_DCT()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var pg = new PdfPage();
        pg.Image(jpeg, 40, 30, 1, 0, 0, 100, 75);
        var pdf = pg.ToBytes("t");
        PdfAssert.WellFormed(pdf);
        string text = PdfAssert.Latin1(pdf);
        Assert.Contains("/Width 40 /Height 30 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /DCTDecode /Length 4", text);
        Assert.Contains("/XObject << /Im0 9 0 R >>", text);
    }

    [Fact]
    public void Title_is_escaped() =>
        Assert.Contains(@"/Title (scan \(1\).png)", PdfAssert.Latin1(new PdfPage().ToBytes("scan (1).png")));
}

public class ReportBuilderTests
{
    private static ReportData Sample() => new()
    {
        ImageName = "scan (test).png", AppVersion = "1.2.3 (abc1234, 2026-10-27)",
        Date = new DateTime(2026, 10, 27, 14, 30, 0),
        Jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, JpegWidth = 400, JpegHeight = 300,
        ImageWidth = 400, ImageHeight = 300, Outline = RegularPolygon(64, 200, 150, 100),
        ScaleA = new Pt(20, 280), ScaleB = new Pt(380, 280), KnownDistanceMm = 100, MmPerPixel = 100 / 360.0,
        ThresholdDescription = "147 (automatic, 50%)", SmoothingDescription = "1.0 px",
    };

    private static string Build(ReportData d)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");
            var pdf = ReportBuilder.Build(d);
            PdfAssert.WellFormed(pdf);
            return PdfAssert.Latin1(pdf);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void Is_a_single_page_with_image_and_fonts()
    {
        string text = Build(Sample());
        Assert.Contains("/Count 1", text);
        Assert.Contains("/DCTDecode", text);
        Assert.Contains("/Helvetica-Bold", text);
    }

    [Fact]
    public void Prints_the_results()
    {
        var d = Sample();
        var m = FieldMetrics.From(d.Outline, d.MmPerPixel);
        string text = Build(d);
        Assert.Contains("(5.55 cm)", text);                           // EqD of a 64-gon, r = 100 px at 0.2778 mm/px
        Assert.Contains($"({m.AreaCm2:F2} cm\\262)", text);
        Assert.Contains($"({m.PerimeterCm:F2} cm)", text);
        Assert.Contains("(3.600 px/mm  \\(91 dpi\\))", text);
        Assert.Contains("(100 mm over 360.0 px)", text);
        Assert.Contains("(147 \\(automatic, 50%\\))", text);
        Assert.Contains("(64)", text);                                // outline vertices
    }

    [Fact]
    public void Header_and_footer_name_the_scan_date_and_version()
    {
        string text = Build(Sample());
        Assert.Contains("scan \\(test\\).png", text);
        Assert.Contains("27 October 2026, 14:30", text);
        Assert.Contains("Generated by Cutout Equivalent Diameter 1.2.3 \\(abc1234, 2026-10-27\\)", text);
        Assert.Contains("/Title (Cutout equivalent diameter \\227 scan \\(test\\).png)", text);
    }

    [Fact]
    public void Empty_settings_rows_are_left_out()
    {
        var d = Sample();
        d.SmoothingDescription = "";
        Assert.DoesNotContain("Noise smoothing", Build(d));
    }
}
