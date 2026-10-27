using static CutoutEqD.Tests.TestImages;

namespace CutoutEqD.Tests;

public class CutoutDetectorTests
{
    [Fact]
    public void Circle_area_and_EqD()
    {
        double cx = 250.3, cy = 240.7, r = 100;
        var img = Render(500, 500, Circle(cx, cy, r));
        var res = CutoutDetector.Detect(img, 250, 240, new DetectionOptions { BlurSigma = 0 });

        Assert.True(res.Success, res.Message);
        Assert.True(RelErr(Polygon.Area(res.Outline), Math.PI * r * r) < 0.003);
        var m = FieldMetrics.From(res.Outline, 0.5);   // r = 5 cm
        Assert.Equal(10, m.EquivalentDiameterCm, 1);
        Assert.InRange(m.EquivalentDiameterCm, 9.98, 10.02);
        Assert.InRange(m.FourAOverPCm, 9.95, 10.05);
    }

    [Fact]
    public void Centroid_matches_the_aperture_centre()
    {
        var img = Render(400, 400, Circle(203.4, 187.9, 80));
        var res = CutoutDetector.Detect(img, 200, 190, new DetectionOptions());
        var c = Polygon.Centroid(res.Outline);
        Assert.InRange(c.X, 203.3, 203.5);
        Assert.InRange(c.Y, 187.8, 188.0);
    }

    [Fact]
    public void Noisy_rotated_ellipse_with_dust()
    {
        double cx = 300, cy = 260, a = 150, b = 90, th = 35 * Math.PI / 180;
        double c = Math.Cos(th), s = Math.Sin(th);
        bool Ell(double x, double y)
        {
            double dx = x - cx, dy = y - cy;
            double u = dx * c + dy * s, v = -dx * s + dy * c;
            return u * u / (a * a) + v * v / (b * b) <= 1;
        }
        var img = Render(620, 540, Ell, noise: 10, seed: 7);
        foreach (var (sx, sy) in new[] { (300, 260), (330, 240), (260, 290) }) Speck(img, sx, sy);

        var res = CutoutDetector.Detect(img, 280, 250, new DetectionOptions { BlurSigma = 1.0 });

        Assert.True(res.Success, res.Message);
        double err = RelErr(Polygon.Area(res.Outline), Math.PI * a * b);
        Assert.True(err < 0.005, $"area error {err:P3}, T={res.ThresholdUsed}, verts={res.Outline.Count}");
    }

    [Fact]
    public void Rectangle_with_sub_pixel_edges()
    {
        double x0 = 100.25, y0 = 80.5, x1 = 300.25, y1 = 200.5;   // 200 × 120
        var img = Render(420, 300, (x, y) => x >= x0 && x < x1 && y >= y0 && y < y1);
        var res = CutoutDetector.Detect(img, 200, 140, new DetectionOptions { BlurSigma = 0.8 });

        Assert.True(res.Success, res.Message);
        Assert.True(RelErr(Polygon.Area(res.Outline), 24000) < 0.005);
        Assert.InRange(FieldMetrics.From(res.Outline, 1.0).FourAOverPCm, 14.9, 15.1);
        Assert.True(res.Outline.Count <= 20, $"{res.Outline.Count} vertices");
    }

    [Theory]
    [InlineData(225, 70)]
    [InlineData(250, 30)]
    [InlineData(160, 90)]
    [InlineData(120, 20)]
    public void Automatic_threshold_does_not_depend_on_brightness(byte light, byte dark)
    {
        var img = Render(300, 300, Circle(150.5, 149.5, 70), light, dark);
        var res = CutoutDetector.Detect(img, 150, 150, new DetectionOptions());

        Assert.True(res.Success, res.Message);
        Assert.True(RelErr(Polygon.Area(res.Outline), Math.PI * 70 * 70) < 0.003);
        Assert.InRange(res.ThresholdUsed, (light + dark) / 2 - 2, (light + dark) / 2 + 2);   // the 50% level
        Assert.InRange(res.LightLevel, light - 2, light);
        Assert.InRange(res.DarkLevel, dark, dark + 2);
    }

    [Fact]
    public void Manual_threshold_is_used_and_moves_the_edge()
    {
        var img = Render(300, 300, Circle(150, 150, 70));
        var lo = CutoutDetector.Detect(img, 150, 150, new DetectionOptions { Threshold = 90 });
        var hi = CutoutDetector.Detect(img, 150, 150, new DetectionOptions { Threshold = 210 });

        Assert.Equal(90, lo.ThresholdUsed);
        Assert.Equal(210, hi.ThresholdUsed);
        Assert.True(Polygon.Area(lo.Outline) > Polygon.Area(hi.Outline));   // a lower threshold takes in more of the edge
    }

    [Theory]
    [InlineData(-50, 0)]
    [InlineData(400, 255)]
    public void Manual_threshold_is_clamped(int requested, int expected)
    {
        var img = Render(100, 100, Circle(50, 50, 20));
        var res = CutoutDetector.Detect(img, 50, 50, new DetectionOptions { Threshold = requested });
        Assert.Equal(Math.Max(expected, 1), res.ThresholdUsed);
    }

    [Fact]
    public void Dust_specks_are_counted_as_aperture()
    {
        var clean = Render(300, 300, Circle(150, 150, 70));
        var dusty = Render(300, 300, Circle(150, 150, 70));
        Speck(dusty, 150, 150, 3);
        Speck(dusty, 120, 160, 2);

        var a = CutoutDetector.Detect(clean, 140, 140, new DetectionOptions());
        var b = CutoutDetector.Detect(dusty, 140, 140, new DetectionOptions());

        Assert.True(b.Success, b.Message);
        Assert.Equal(Polygon.Area(a.Outline), Polygon.Area(b.Outline), 0);
        Assert.InRange(b.PixelCount, a.PixelCount - 5, a.PixelCount + 5);
    }

    [Fact]
    public void Pre_blurred_image_gives_the_same_result()
    {
        var img = Render(300, 300, Circle(150, 150, 70), noise: 6);
        var opts = new DetectionOptions { BlurSigma = 1.5 };
        var direct = CutoutDetector.Detect(img, 150, 150, opts);
        var cached = CutoutDetector.Detect(img, 150, 150, opts, Imaging.GaussianBlur(img, 1.5));
        Assert.Equal(direct.Outline, cached.Outline);
        Assert.Equal(direct.ThresholdUsed, cached.ThresholdUsed);
    }

    [Fact]
    public void Zero_simplify_tolerance_keeps_every_contour_point()
    {
        var img = Render(300, 300, Circle(150, 150, 70));
        var full = CutoutDetector.Detect(img, 150, 150, new DetectionOptions { SimplifyTolerance = 0 });
        var simp = CutoutDetector.Detect(img, 150, 150, new DetectionOptions());
        Assert.True(full.Outline.Count > simp.Outline.Count);
    }

    [Fact]
    public void Click_on_the_scanner_bed_is_rejected()
    {
        var img = Render(300, 300, Circle(150, 150, 60));
        var res = CutoutDetector.Detect(img, 5, 5, new DetectionOptions());
        Assert.False(res.Success);
        Assert.Contains("image edge", res.Message);
        Assert.Empty(res.Outline);
    }

    [Fact]
    public void Click_on_the_plate_is_rejected()
    {
        var img = Render(300, 300, Circle(150, 150, 60));
        var res = CutoutDetector.Detect(img, 150, 40, new DetectionOptions());
        Assert.False(res.Success);
        Assert.Contains("darker than the threshold", res.Message);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    [InlineData(300, 10)]
    [InlineData(10, 300)]
    public void Click_outside_the_image_is_rejected(int x, int y)
    {
        var img = Render(300, 300, Circle(150, 150, 60));
        var res = CutoutDetector.Detect(img, x, y, new DetectionOptions());
        Assert.False(res.Success);
        Assert.Equal("Click inside the image.", res.Message);
    }

    [Fact]
    public void Tiny_region_is_rejected()
    {
        var img = Render(200, 200, Circle(100, 100, 1.5));
        var res = CutoutDetector.Detect(img, 100, 100, new DetectionOptions { BlurSigma = 0, Threshold = 128 });
        Assert.False(res.Success);
        Assert.Contains("too small", res.Message);
    }

    [Fact]
    public void Implausibly_large_region_is_rejected()
    {
        var img = Render(200, 200, (x, y) => x >= 27 && x < 173 && y >= 27 && y < 173);
        var res = CutoutDetector.Detect(img, 100, 100, new DetectionOptions { MaxAreaFraction = 0.25 });
        Assert.False(res.Success);
        Assert.Contains("implausibly large", res.Message);
    }

    [Fact]
    public void BandLevels_reads_inside_and_outside_medians()
    {
        var img = Render(200, 200, Circle(100, 100, 50));
        var region = Imaging.FloodFillLight(img, 100, 100, 148, out _, out _);
        Assert.True(CutoutDetector.BandLevels(img, region, 3, 10, out int inside, out int outside));
        Assert.Equal(Light, inside);
        Assert.Equal(Dark, outside);
    }
}
