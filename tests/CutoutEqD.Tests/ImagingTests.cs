using static CutoutEqD.Tests.TestImages;

namespace CutoutEqD.Tests;

public class GrayImageTests
{
    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-1, 5)]
    public void Rejects_non_positive_size(int w, int h) => Assert.Throws<ArgumentException>(() => new GrayImage(w, h));

    [Fact]
    public void Rejects_data_of_wrong_length() => Assert.Throws<ArgumentException>(() => new GrayImage(4, 4, new byte[15]));

    [Fact]
    public void Indexer_is_row_major()
    {
        var img = new GrayImage(3, 2);
        img[2, 1] = 99;
        Assert.Equal(99, img.Data[1 * 3 + 2]);
        Assert.Equal(99, img[2, 1]);
    }
}

public class GaussianBlurTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.05)]
    public void Tiny_sigma_returns_an_independent_copy(double sigma)
    {
        var src = Binary(8, 8, (x, y) => x == y);
        var dst = Imaging.GaussianBlur(src, sigma);
        Assert.Equal(src.Data, dst.Data);
        Assert.NotSame(src.Data, dst.Data);
    }

    [Fact]
    public void Uniform_image_is_unchanged()
    {
        var src = new GrayImage(20, 15, Enumerable.Repeat((byte)137, 300).ToArray());
        Assert.All(Imaging.GaussianBlur(src, 2).Data, v => Assert.Equal(137, v));
    }

    [Fact]
    public void Spreads_a_point_symmetrically_and_keeps_its_peak_in_place()
    {
        var src = new GrayImage(21, 21);
        src[10, 10] = 255;
        var dst = Imaging.GaussianBlur(src, 1.5);
        Assert.True(dst[10, 10] < 255);
        Assert.Equal(dst[9, 10], dst[11, 10]);
        Assert.Equal(dst[10, 9], dst[10, 11]);
        Assert.Equal(dst[9, 10], dst[10, 9]);
        Assert.Equal(dst.Data.Max(), dst[10, 10]);
    }

    [Fact]
    public void Preserves_overall_brightness()
    {
        var src = Render(120, 120, Circle(60, 60, 30));
        var dst = Imaging.GaussianBlur(src, 1.0);
        Assert.Equal(src.Data.Average(b => b), dst.Data.Average(b => b), 0);
    }
}

public class ThresholdTests
{
    [Fact]
    public void Histogram_counts_pixels_in_the_rectangle()
    {
        var img = Binary(10, 10, (x, _) => x >= 5);
        var hist = Imaging.Histogram(img, 0, 0, 10, 10);
        Assert.Equal(50, hist[0]);
        Assert.Equal(50, hist[255]);
        Assert.Equal(100, hist.Sum());
    }

    [Fact]
    public void Histogram_clamps_the_rectangle_to_the_image() =>
        Assert.Equal(100, Imaging.Histogram(new GrayImage(10, 10), -5, -5, 50, 50).Sum());

    [Fact]
    public void Otsu_splits_two_levels()
    {
        var hist = new int[256];
        hist[70] = 1000; hist[225] = 400;
        int t = Imaging.OtsuThreshold(hist);
        Assert.InRange(t, 71, 225);   // light pixels are those >= t
    }

    [Fact]
    public void Otsu_on_synthetic_scan_separates_plate_from_aperture()
    {
        var img = Render(200, 200, Circle(100, 100, 50));
        int t = Imaging.OtsuThreshold(img);
        Assert.InRange(t, Dark + 1, Light);
    }

    [Fact]
    public void Otsu_of_empty_histogram_is_mid_grey() => Assert.Equal(128, Imaging.OtsuThreshold(new int[256]));
}

public class FloodFillTests
{
    [Fact]
    public void Fills_the_connected_light_region_only()
    {
        // Two light squares separated by a dark column.
        var img = Binary(12, 6, (x, y) => y is >= 1 and <= 4 && (x is >= 1 and <= 4 || x is >= 7 and <= 10));
        var mask = Imaging.FloodFillLight(img, 2, 2, 128, out bool border, out int count);
        Assert.False(border);
        Assert.Equal(16, count);
        Assert.True(mask[2 * 12 + 2]);
        Assert.False(mask[2 * 12 + 8]);
    }

    [Fact]
    public void Is_four_connected()
    {
        // Two pixels that only touch diagonally.
        var img = Binary(5, 5, (x, y) => (x, y) is (1, 1) or (2, 2));
        Imaging.FloodFillLight(img, 1, 1, 128, out _, out int count);
        Assert.Equal(1, count);
    }

    [Fact]
    public void Reports_touching_the_border()
    {
        var img = Binary(10, 10, (x, _) => x < 5);
        Imaging.FloodFillLight(img, 2, 5, 128, out bool border, out _);
        Assert.True(border);
    }

    [Theory]
    [InlineData(8, 5)]     // dark seed
    [InlineData(-1, 0)]    // outside the image
    [InlineData(10, 3)]
    public void Returns_empty_mask_for_a_bad_seed(int sx, int sy)
    {
        var img = Binary(10, 10, (x, _) => x < 5);
        var mask = Imaging.FloodFillLight(img, sx, sy, 128, out bool border, out int count);
        Assert.Equal(0, count);
        Assert.False(border);
        Assert.DoesNotContain(true, mask);
    }
}

public class FillHolesTests
{
    [Fact]
    public void Fills_an_enclosed_hole()
    {
        const int w = 9, h = 9;
        var mask = new bool[w * h];
        for (int y = 2; y <= 6; y++) for (int x = 2; x <= 6; x++) mask[y * w + x] = true;
        mask[4 * w + 4] = false;   // a speck
        var filled = Imaging.FillHoles(mask, w, h);
        Assert.True(filled[4 * w + 4]);
        Assert.Equal(25, filled.Count(b => b));
    }

    [Fact]
    public void Leaves_a_notch_open_to_the_outside()
    {
        const int w = 9, h = 9;
        var mask = new bool[w * h];
        for (int y = 2; y <= 6; y++) for (int x = 2; x <= 6; x++) mask[y * w + x] = true;
        for (int y = 2; y <= 4; y++) mask[y * w + 4] = false;   // slot cut in from the top edge
        var filled = Imaging.FillHoles(mask, w, h);
        Assert.False(filled[3 * w + 4]);
        Assert.Equal(22, filled.Count(b => b));
    }
}

public class TraceContourTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public void Crisp_square_traces_pixel_boundaries_with_chamfered_corners(int side)
    {
        // The 50% level falls halfway between pixel centres, i.e. on the pixel boundaries. Marching squares
        // cuts each corner diagonally (a right triangle with ½-px legs), so the area is side² − 4·⅛.
        int w = side + 6;
        var img = Binary(w, w, (x, y) => x >= 3 && x < 3 + side && y >= 3 && y < 3 + side);
        var region = Imaging.FloodFillLight(img, 3, 3, 128, out _, out _);
        var filled = Imaging.FillHoles(region, w, w);
        var contour = Imaging.TraceContour(img, region, filled, 128);
        Assert.Equal(side * side - 0.5, Polygon.Area(contour), 9);
        Assert.Equal(4.0 * (side - 1) + 4 * Math.Sqrt(0.5), Polygon.Perimeter(contour), 9);
        var c = Polygon.Centroid(contour);
        Assert.Equal(3 + side / 2.0, c.X, 9);
        Assert.Equal(3 + side / 2.0, c.Y, 9);
    }

    [Fact]
    public void Edge_moves_with_grey_level()
    {
        // A grey column at x = 8 (191) next to black at x = 9: the edge is interpolated between the two pixel
        // centres (8.5 and 9.5) at level 127.5, a third of the way across.
        const int w = 12, h = 12;
        var img = Binary(w, h, (x, y) => x >= 3 && x < 8 && y >= 3 && y < 8);
        for (int y = 3; y < 8; y++) img[8, y] = 191;
        var region = Imaging.FloodFillLight(img, 4, 4, 128, out _, out _);
        var filled = Imaging.FillHoles(region, w, h);
        var contour = Imaging.TraceContour(img, region, filled, 128);
        double maxX = contour.Max(p => p.X);
        Assert.Equal(8.5 + (191 - 127.5) / 191, maxX, 9);
    }

    [Fact]
    public void Filled_specks_do_not_create_inner_contours()
    {
        const int w = 30, h = 30;
        var img = Binary(w, h, (x, y) => x >= 5 && x < 25 && y >= 5 && y < 25);
        img[15, 15] = 0;
        var region = Imaging.FloodFillLight(img, 6, 6, 128, out _, out _);
        var filled = Imaging.FillHoles(region, w, h);
        var contour = Imaging.TraceContour(img, region, filled, 128);
        Assert.Equal(400 - 0.5, Polygon.Area(contour), 9);   // as for the plain square: chamfered corners only
    }

    [Fact]
    public void Empty_region_gives_empty_contour()
    {
        var img = new GrayImage(5, 5);
        var none = new bool[25];
        Assert.Empty(Imaging.TraceContour(img, none, none, 128));
    }
}
