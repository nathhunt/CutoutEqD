using static CutoutEqD.Tests.TestImages;

namespace CutoutEqD.Tests;

public class ImageTransformTests
{
    private const int W = 40, H = 30;

    private static void AssertNear(Pt expected, Pt actual) => Assert.True(Pt.Distance(expected, actual) < 1e-9, $"expected {expected}, got {actual}");

    [Fact]
    public void Identity_leaves_points_alone()
    {
        Assert.True(ImageTransform.Identity.IsIdentity);
        Assert.Equal(new Pt(3, 4), ImageTransform.Identity.Apply(new Pt(3, 4)));
    }

    [Fact]
    public void RotateClockwise_maps_pixel_centres()
    {
        // Pixel (i, j) of a W×H image becomes pixel (H−1−j, i) of the H×W result.
        var cw = ImageTransform.RotateClockwise(W, H);
        AssertNear(new Pt(H - 1 - 7 + 0.5, 3.5), cw.Apply(new Pt(3.5, 7.5)));
        AssertNear(new Pt(H, 0), cw.Apply(new Pt(0, 0)));   // top-left corner → top-right
    }

    [Fact]
    public void RotateCounterClockwise_maps_pixel_centres()
    {
        // Pixel (i, j) becomes pixel (j, W−1−i).
        var ccw = ImageTransform.RotateCounterClockwise(W, H);
        AssertNear(new Pt(7.5, W - 1 - 3 + 0.5), ccw.Apply(new Pt(3.5, 7.5)));
        AssertNear(new Pt(0, W), ccw.Apply(new Pt(0, 0)));  // top-left corner → bottom-left
    }

    [Fact]
    public void Crop_shifts_by_the_crop_origin() =>
        AssertNear(new Pt(5, 8), ImageTransform.Crop(10, 2).Apply(new Pt(15, 10)));

    [Fact]
    public void Clockwise_then_counter_clockwise_is_identity() =>
        Assert.True(ImageTransform.RotateClockwise(W, H).Then(ImageTransform.RotateCounterClockwise(H, W)).IsIdentity);

    [Fact]
    public void Four_clockwise_turns_are_identity()
    {
        var t = ImageTransform.RotateClockwise(W, H)
            .Then(ImageTransform.RotateClockwise(H, W))
            .Then(ImageTransform.RotateClockwise(W, H))
            .Then(ImageTransform.RotateClockwise(H, W));
        Assert.True(t.IsIdentity);
    }

    [Fact]
    public void Then_applies_this_transform_first()
    {
        var crop = ImageTransform.Crop(5, 0);
        var cw = ImageTransform.RotateClockwise(W - 5, H);
        var p = new Pt(12, 3);
        AssertNear(cw.Apply(crop.Apply(p)), crop.Then(cw).Apply(p));
    }

    [Fact]
    public void Rotate_and_crop_preserve_area_and_invert_exactly()
    {
        var chain = ImageTransform.RotateClockwise(W, H)
            .Then(ImageTransform.Crop(5, 2))
            .Then(ImageTransform.RotateCounterClockwise(H - 5, W - 2));
        var sq = new List<Pt> { new(10, 10), new(20, 10), new(20, 18), new(10, 18) };
        var moved = sq.ConvertAll(chain.Apply);
        Assert.Equal(Polygon.Area(sq), Polygon.Area(moved), 9);   // so the mm/px scale stays valid
        var inv = chain.Inverse();
        Assert.All(sq, p => AssertNear(p, inv.Apply(chain.Apply(p))));
        Assert.True(chain.Then(inv).IsIdentity);
    }
}
