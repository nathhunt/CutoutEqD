using static CutoutEqD.Tests.TestImages;

namespace CutoutEqD.Tests;

public class PolygonTests
{
    private static readonly List<Pt> Sq = Square(0, 0, 10);

    [Fact]
    public void Area_of_square() => Assert.Equal(100, Polygon.Area(Sq), 9);

    [Fact]
    public void Area_does_not_depend_on_winding_order()
    {
        var reversed = Enumerable.Reverse(Sq).ToList();
        Assert.Equal(Polygon.Area(Sq), Polygon.Area(reversed), 9);
    }

    [Fact]
    public void Area_of_triangle() =>
        Assert.Equal(6, Polygon.Area(new List<Pt> { new(0, 0), new(4, 0), new(0, 3) }), 9);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Area_is_zero_with_fewer_than_three_points(int n) =>
        Assert.Equal(0, Polygon.Area(Sq.Take(n).ToList()));

    [Fact]
    public void Perimeter_of_square() => Assert.Equal(40, Polygon.Perimeter(Sq), 9);

    [Fact]
    public void Perimeter_includes_closing_edge() =>
        Assert.Equal(12, Polygon.Perimeter(new List<Pt> { new(0, 0), new(4, 0), new(0, 3) }), 9);

    [Fact]
    public void Perimeter_of_single_point_is_zero() => Assert.Equal(0, Polygon.Perimeter(new List<Pt> { new(1, 1) }));

    [Fact]
    public void Centroid_of_square_is_its_centre()
    {
        var c = Polygon.Centroid(Square(10, 20, 4));
        Assert.Equal(12, c.X, 9);
        Assert.Equal(22, c.Y, 9);
    }

    [Fact]
    public void Centroid_of_L_shape_is_area_weighted()
    {
        // A 2×1 bar with a 1×1 block on its left end.
        var l = new List<Pt> { new(0, 0), new(2, 0), new(2, 1), new(1, 1), new(1, 2), new(0, 2) };
        var c = Polygon.Centroid(l);
        // Parts: [0,2]×[0,1] area 2 centroid (1, 0.5); [0,1]×[1,2] area 1 centroid (0.5, 1.5).
        Assert.Equal((2 * 1 + 1 * 0.5) / 3, c.X, 9);
        Assert.Equal((2 * 0.5 + 1 * 1.5) / 3, c.Y, 9);
    }

    [Fact]
    public void Centroid_of_degenerate_polygon_falls_back_to_mean_of_points()
    {
        var c = Polygon.Centroid(new List<Pt> { new(0, 0), new(2, 0), new(4, 0) });
        Assert.Equal(new Pt(2, 0), c);
    }

    [Theory]
    [InlineData(5, 5, true)]
    [InlineData(0.1, 9.9, true)]
    [InlineData(15, 5, false)]
    [InlineData(-1, 5, false)]
    [InlineData(5, 10.5, false)]
    public void Contains(double x, double y, bool expected) => Assert.Equal(expected, Polygon.Contains(Sq, new Pt(x, y)));

    [Fact]
    public void Contains_handles_concave_polygon()
    {
        var u = new List<Pt> { new(0, 0), new(3, 0), new(3, 3), new(2, 3), new(2, 1), new(1, 1), new(1, 3), new(0, 3) };
        Assert.True(Polygon.Contains(u, new Pt(0.5, 2)));
        Assert.False(Polygon.Contains(u, new Pt(1.5, 2)));   // inside the notch
    }

    [Fact]
    public void NearestVertex_finds_closest_within_range() =>
        Assert.Equal(2, Polygon.NearestVertex(Sq, new Pt(9, 9.5), 2));

    [Fact]
    public void NearestVertex_returns_minus_one_when_out_of_range() =>
        Assert.Equal(-1, Polygon.NearestVertex(Sq, new Pt(5, 5), 2));

    [Fact]
    public void NearestEdge_projects_onto_the_edge()
    {
        int e = Polygon.NearestEdge(Sq, new Pt(5, 0.5), 2, out var proj);
        Assert.Equal(0, e);
        Assert.Equal(new Pt(5, 0), proj);
    }

    [Fact]
    public void NearestEdge_includes_closing_edge()
    {
        int e = Polygon.NearestEdge(Sq, new Pt(-0.5, 5), 2, out var proj);
        Assert.Equal(3, e);               // edge 3 runs from (0,10) back to (0,0)
        Assert.Equal(new Pt(0, 5), proj);
    }

    [Fact]
    public void NearestEdge_clamps_projection_to_the_segment()
    {
        Polygon.NearestEdge(new List<Pt> { new(0, 0), new(10, 0), new(10, 10) }, new Pt(-1, -1), 5, out var proj);
        Assert.Equal(new Pt(0, 0), proj);
    }

    [Fact]
    public void NearestEdge_returns_minus_one_when_out_of_range()
    {
        int e = Polygon.NearestEdge(Sq, new Pt(5, 5), 2, out var proj);
        Assert.Equal(-1, e);
        Assert.Equal(new Pt(5, 5), proj);
    }

    [Fact]
    public void Simplify_removes_collinear_points()
    {
        var withMidpoints = new List<Pt> { new(0, 0), new(5, 0), new(10, 0), new(10, 5), new(10, 10), new(5, 10), new(0, 10), new(0, 5) };
        var s = Polygon.Simplify(withMidpoints, 0.1);
        Assert.Equal(4, s.Count);
        Assert.Equal(100, Polygon.Area(s), 9);
    }

    [Fact]
    public void Simplify_keeps_small_polygons_and_zero_tolerance_unchanged()
    {
        Assert.Equal(Sq, Polygon.Simplify(Sq, 5));
        var octagon = RegularPolygon(8, 0, 0, 10);
        Assert.Equal(octagon, Polygon.Simplify(octagon, 0));
    }

    [Fact]
    public void Simplify_keeps_circle_area_and_stays_within_tolerance()
    {
        var dense = RegularPolygon(2000, 0, 0, 100);
        var simp = Polygon.Simplify(dense, 0.2);
        Assert.InRange(simp.Count, 10, 200);
        Assert.True(RelErr(Polygon.Area(simp), Polygon.Area(dense)) < 0.003);
        // Every dropped point lies within the tolerance of the simplified outline.
        Assert.All(dense, p => Assert.NotEqual(-1, Polygon.NearestEdge(simp, p, 0.2 + 1e-9, out _)));
    }

    [Fact]
    public void Simplify_returns_vertices_in_original_order()
    {
        var dense = RegularPolygon(500, 0, 0, 50);
        var simp = Polygon.Simplify(dense, 0.5);
        var idx = simp.Select(p => dense.IndexOf(p)).ToList();
        Assert.Equal(idx.OrderBy(i => i), idx);
    }

    [Fact]
    public void Pt_operators_and_distance()
    {
        var a = new Pt(1, 2);
        var b = new Pt(4, 6);
        Assert.Equal(new Pt(5, 8), a + b);
        Assert.Equal(new Pt(3, 4), b - a);
        Assert.Equal(new Pt(2, 4), a * 2);
        Assert.Equal(5, Pt.Distance(a, b), 12);
        Assert.Equal(5, (b - a).Length, 12);
    }
}
