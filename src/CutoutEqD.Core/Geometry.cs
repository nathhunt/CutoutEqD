using System;
using System.Collections.Generic;

namespace CutoutEqD.Core;

/// <summary>A point in image-pixel coordinates. Pixel (i, j) covers [i, i+1) x [j, j+1),
/// so its centre is at (i + 0.5, j + 0.5).</summary>
public readonly record struct Pt(double X, double Y)
{
    public static Pt operator +(Pt a, Pt b) => new(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(Pt a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public static double Distance(Pt a, Pt b) => (a - b).Length;
}

public static class Polygon
{
    /// <summary>Unsigned area by the shoelace formula (units: px²).</summary>
    public static double Area(IReadOnlyList<Pt> p)
    {
        if (p.Count < 3) return 0;
        double s = 0;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            s += p[j].X * p[i].Y - p[i].X * p[j].Y;
        return Math.Abs(s) * 0.5;
    }

    /// <summary>Perimeter of the closed polygon (units: px).</summary>
    public static double Perimeter(IReadOnlyList<Pt> p)
    {
        if (p.Count < 2) return 0;
        double s = 0;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            s += Pt.Distance(p[i], p[j]);
        return s;
    }

    public static Pt Centroid(IReadOnlyList<Pt> p)
    {
        double a = 0, cx = 0, cy = 0;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
        {
            double c = p[j].X * p[i].Y - p[i].X * p[j].Y;
            a += c; cx += (p[j].X + p[i].X) * c; cy += (p[j].Y + p[i].Y) * c;
        }
        if (Math.Abs(a) < 1e-12)
        {
            double sx = 0, sy = 0;
            foreach (var q in p) { sx += q.X; sy += q.Y; }
            return new Pt(sx / Math.Max(1, p.Count), sy / Math.Max(1, p.Count));
        }
        return new Pt(cx / (3 * a), cy / (3 * a));
    }

    public static bool Contains(IReadOnlyList<Pt> p, Pt q)
    {
        bool inside = false;
        for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
        {
            if ((p[i].Y > q.Y) != (p[j].Y > q.Y) &&
                q.X < (p[j].X - p[i].X) * (q.Y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Index of the vertex nearest to q, or -1 if none is within maxDist.</summary>
    public static int NearestVertex(IReadOnlyList<Pt> p, Pt q, double maxDist)
    {
        int best = -1; double bd = maxDist;
        for (int i = 0; i < p.Count; i++)
        {
            double d = Pt.Distance(p[i], q);
            if (d <= bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>Finds the closed-polygon edge nearest to q. Returns the index i of the edge
    /// (p[i] → p[i+1]) and the projected point, or -1 if none is within maxDist.</summary>
    public static int NearestEdge(IReadOnlyList<Pt> p, Pt q, double maxDist, out Pt projection)
    {
        int best = -1; double bd = maxDist; projection = q;
        for (int i = 0; i < p.Count; i++)
        {
            Pt a = p[i], b = p[(i + 1) % p.Count];
            Pt ab = b - a;
            double len2 = ab.X * ab.X + ab.Y * ab.Y;
            double t = len2 < 1e-12 ? 0 : ((q.X - a.X) * ab.X + (q.Y - a.Y) * ab.Y) / len2;
            t = Math.Clamp(t, 0, 1);
            Pt proj = a + ab * t;
            double d = Pt.Distance(proj, q);
            if (d <= bd) { bd = d; best = i; projection = proj; }
        }
        return best;
    }

    /// <summary>Douglas–Peucker simplification of a closed polygon.</summary>
    public static List<Pt> Simplify(IReadOnlyList<Pt> p, double tolerance)
    {
        int n = p.Count;
        if (n <= 4 || tolerance <= 0) return new List<Pt>(p);

        // Split the ring at vertex 0 and the vertex furthest from it.
        int far = 0; double fd = -1;
        for (int i = 1; i < n; i++)
        {
            double d = Pt.Distance(p[0], p[i]);
            if (d > fd) { fd = d; far = i; }
        }
        var keep = new bool[n];
        keep[0] = keep[far] = true;
        Dp(p, 0, far, tolerance, keep);
        // Second half: far → n (where index n wraps to 0).
        DpWrap(p, far, n, tolerance, keep);

        var result = new List<Pt>();
        for (int i = 0; i < n; i++) if (keep[i]) result.Add(p[i]);
        return result;
    }

    private static void Dp(IReadOnlyList<Pt> p, int i0, int i1, double tol, bool[] keep)
    {
        var stack = new Stack<(int, int)>();
        stack.Push((i0, i1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            if (b - a < 2) continue;
            int idx = -1; double dmax = tol;
            for (int i = a + 1; i < b; i++)
            {
                double d = SegDist(p[i], p[a], p[b]);
                if (d > dmax) { dmax = d; idx = i; }
            }
            if (idx >= 0) { keep[idx] = true; stack.Push((a, idx)); stack.Push((idx, b)); }
        }
    }

    private static void DpWrap(IReadOnlyList<Pt> p, int i0, int i1, double tol, bool[] keep)
    {
        int n = p.Count;
        var stack = new Stack<(int, int)>();
        stack.Push((i0, i1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            if (b - a < 2) continue;
            int idx = -1; double dmax = tol;
            for (int i = a + 1; i < b; i++)
            {
                double d = SegDist(p[i % n], p[a % n], p[b % n]);
                if (d > dmax) { dmax = d; idx = i; }
            }
            if (idx >= 0) { keep[idx % n] = true; stack.Push((a, idx)); stack.Push((idx, b)); }
        }
    }

    private static double SegDist(Pt q, Pt a, Pt b)
    {
        Pt ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 1e-12) return Pt.Distance(q, a);
        double t = Math.Clamp(((q.X - a.X) * ab.X + (q.Y - a.Y) * ab.Y) / len2, 0, 1);
        return Pt.Distance(q, a + ab * t);
    }
}

/// <summary>Field-size quantities for an outline, in physical units.</summary>
public sealed record FieldMetrics(double AreaCm2, double PerimeterCm, double EquivalentDiameterCm, double FourAOverPCm)
{
    /// <param name="outline">Polygon in pixel coordinates.</param>
    /// <param name="mmPerPixel">Scale from the ruler.</param>
    public static FieldMetrics From(IReadOnlyList<Pt> outline, double mmPerPixel)
    {
        double cmPerPx = mmPerPixel / 10.0;
        double area = Polygon.Area(outline) * cmPerPx * cmPerPx;
        double per = Polygon.Perimeter(outline) * cmPerPx;
        // Diameter of the circle having the same area as the field.
        double eqd = 2.0 * Math.Sqrt(area / Math.PI);
        double fourAP = per > 0 ? 4.0 * area / per : 0;
        return new FieldMetrics(area, per, eqd, fourAP);
    }
}
