using static CutoutEqD.Tests.TestImages;

namespace CutoutEqD.Tests;

public class FieldMetricsTests
{
    [Fact]
    public void Square_10cm()
    {
        // 100 px square at 1 mm/px = 10 cm × 10 cm.
        var m = FieldMetrics.From(Square(0, 0, 100), 1.0);
        Assert.Equal(100, m.AreaCm2, 9);
        Assert.Equal(40, m.PerimeterCm, 9);
        Assert.Equal(2 * Math.Sqrt(100 / Math.PI), m.EquivalentDiameterCm, 9);   // 11.284 cm
        Assert.Equal(10, m.FourAOverPCm, 9);                                      // 4A/P of a square = side
    }

    [Fact]
    public void Rectangle_4A_over_P()
    {
        // 20 × 12 cm: 4A/P = 4·240 / 64 = 15 cm.
        var rect = new List<Pt> { new(0, 0), new(200, 0), new(200, 120), new(0, 120) };
        Assert.Equal(15, FieldMetrics.From(rect, 1.0).FourAOverPCm, 9);
    }

    [Fact]
    public void Circle_EqD_equals_its_diameter()
    {
        var m = FieldMetrics.From(RegularPolygon(4000, 0, 0, 100), 0.5);   // r = 5 cm
        Assert.Equal(10, m.EquivalentDiameterCm, 3);
        Assert.Equal(10, m.FourAOverPCm, 3);                              // 4A/P of a circle = D
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Scale_applies_quadratically_to_area_and_linearly_to_lengths(double mmPerPx)
    {
        var sq = Square(0, 0, 40);
        var unit = FieldMetrics.From(sq, 1.0);
        var m = FieldMetrics.From(sq, mmPerPx);
        Assert.Equal(unit.AreaCm2 * mmPerPx * mmPerPx, m.AreaCm2, 9);
        Assert.Equal(unit.PerimeterCm * mmPerPx, m.PerimeterCm, 9);
        Assert.Equal(unit.EquivalentDiameterCm * mmPerPx, m.EquivalentDiameterCm, 9);
    }

    [Fact]
    public void Empty_outline_gives_zeros()
    {
        var m = FieldMetrics.From(new List<Pt>(), 1.0);
        Assert.Equal(new FieldMetrics(0, 0, 0, 0), m);
    }
}
