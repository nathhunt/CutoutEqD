using System;
using System.Collections.Generic;

namespace CutoutEqD.Core;

/// <summary>8-bit greyscale image, row-major.</summary>
public sealed class GrayImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public GrayImage(int width, int height, byte[]? data = null)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Image must have a positive size.");
        Width = width; Height = height;
        Data = data ?? new byte[width * height];
        if (Data.Length != width * height) throw new ArgumentException("Data length does not match size.");
    }

    public byte this[int x, int y]
    {
        get => Data[y * Width + x];
        set => Data[y * Width + x] = value;
    }
}

public static class Imaging
{
    /// <summary>Separable Gaussian blur. sigma &lt;= 0 returns a copy.</summary>
    public static GrayImage GaussianBlur(GrayImage src, double sigma)
    {
        int w = src.Width, h = src.Height;
        if (sigma <= 0.05) return new GrayImage(w, h, (byte[])src.Data.Clone());

        int r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var k = new double[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++) { k[i + r] = Math.Exp(-(i * i) / (2 * sigma * sigma)); sum += k[i + r]; }
        for (int i = 0; i < k.Length; i++) k[i] /= sum;

        var tmp = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                double acc = 0;
                for (int i = -r; i <= r; i++)
                {
                    int xx = Math.Clamp(x + i, 0, w - 1);
                    acc += k[i + r] * src.Data[row + xx];
                }
                tmp[row + x] = (float)acc;
            }
        }
        var dst = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double acc = 0;
                for (int i = -r; i <= r; i++)
                {
                    int yy = Math.Clamp(y + i, 0, h - 1);
                    acc += k[i + r] * tmp[yy * w + x];
                }
                dst[y * w + x] = (byte)Math.Clamp((int)Math.Round(acc), 0, 255);
            }
        }
        return new GrayImage(w, h, dst);
    }

    /// <summary>Otsu's threshold over the whole image. Pixels &gt;= the result are "light".</summary>
    public static int OtsuThreshold(GrayImage img) => OtsuThreshold(Histogram(img, 0, 0, img.Width, img.Height));

    public static int[] Histogram(GrayImage img, int x0, int y0, int x1, int y1)
    {
        var hist = new int[256];
        x0 = Math.Clamp(x0, 0, img.Width); x1 = Math.Clamp(x1, 0, img.Width);
        y0 = Math.Clamp(y0, 0, img.Height); y1 = Math.Clamp(y1, 0, img.Height);
        for (int y = y0; y < y1; y++)
        {
            int row = y * img.Width;
            for (int x = x0; x < x1; x++) hist[img.Data[row + x]]++;
        }
        return hist;
    }

    public static int OtsuThreshold(int[] hist)
    {
        long total = 0; double sumAll = 0;
        for (int i = 0; i < 256; i++) { total += hist[i]; sumAll += (double)i * hist[i]; }
        if (total == 0) return 128;

        double sumB = 0, best = -1; long wB = 0; int t = 128;
        for (int i = 0; i < 255; i++)
        {
            wB += hist[i];
            if (wB == 0) continue;
            long wF = total - wB;
            if (wF == 0) break;
            sumB += (double)i * hist[i];
            double mB = sumB / wB, mF = (sumAll - sumB) / wF;
            double between = (double)wB * wF * (mB - mF) * (mB - mF);
            if (between > best) { best = between; t = i + 1; }
        }
        return t;
    }

    /// <summary>4-connected flood fill of pixels with value &gt;= threshold, starting at the seed.</summary>
    public static bool[] FloodFillLight(GrayImage img, int sx, int sy, int threshold,
                                        out bool touchesBorder, out int count)
    {
        int w = img.Width, h = img.Height;
        var mask = new bool[w * h];
        touchesBorder = false; count = 0;
        if (sx < 0 || sy < 0 || sx >= w || sy >= h) return mask;
        if (img.Data[sy * w + sx] < threshold) return mask;

        var stack = new Stack<int>();
        stack.Push(sy * w + sx);
        mask[sy * w + sx] = true;
        while (stack.Count > 0)
        {
            int idx = stack.Pop();
            count++;
            int x = idx % w, y = idx / w;
            if (x == 0 || y == 0 || x == w - 1 || y == h - 1) touchesBorder = true;
            if (x > 0) TryPush(idx - 1);
            if (x < w - 1) TryPush(idx + 1);
            if (y > 0) TryPush(idx - w);
            if (y < h - 1) TryPush(idx + w);
        }
        return mask;

        void TryPush(int i)
        {
            if (!mask[i] && img.Data[i] >= threshold) { mask[i] = true; stack.Push(i); }
        }
    }

    /// <summary>Fills enclosed holes (e.g. dust specks inside the aperture) in a binary mask.
    /// Background is everything 8-connected to the image border through unset pixels, which
    /// keeps the filled region 4-connected.</summary>
    public static bool[] FillHoles(bool[] mask, int w, int h)
    {
        var outside = new bool[w * h];
        var stack = new Stack<int>();
        void Seed(int i) { if (!mask[i] && !outside[i]) { outside[i] = true; stack.Push(i); } }
        for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
        while (stack.Count > 0)
        {
            int idx = stack.Pop();
            int x = idx % w, y = idx / w;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                Seed(ny * w + nx);
            }
        }
        var filled = new bool[w * h];
        for (int i = 0; i < filled.Length; i++) filled[i] = !outside[i];
        return filled;
    }

    /// <summary>
    /// Sub-pixel outline of a region using marching squares on the greyscale values at the
    /// threshold level. Pixels outside the region are forced dark and pixels in filled holes are
    /// forced light, so exactly one closed contour results. Saddle cells are resolved so that
    /// diagonally touching inside pixels stay separate (consistent with 4-connectivity).
    /// </summary>
    public static List<Pt> TraceContour(GrayImage img, bool[] region, bool[] filled, int threshold)
    {
        int w = img.Width, h = img.Height;
        double level = threshold - 0.5;

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (filled[y * w + x])
            {
                if (x < minX) minX = x; if (x > maxX) maxX = x;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
        if (maxX < 0) return new List<Pt>();

        // Field sampled at pixel centres, padded by 1 on each side (padding is "outside").
        int x0 = minX - 1, y0 = minY - 1, fw = maxX - minX + 3, fh = maxY - minY + 3;
        var f = new double[fw * fh];
        for (int j = 0; j < fh; j++)
        for (int i = 0; i < fw; i++)
        {
            int x = x0 + i, y = y0 + j;
            double v;
            if (x < 0 || y < 0 || x >= w || y >= h) v = 0;
            else
            {
                int k = y * w + x;
                byte g = img.Data[k];
                if (filled[k]) v = region[k] ? g : 255;      // filled specks count as inside
                else v = g >= threshold ? 0 : g;              // keep real grey for edge interpolation
            }
            f[j * fw + i] = v;
        }

        bool In(int i, int j) => f[j * fw + i] > level;

        // Edge keys: horizontal edge from corner (i,j) to (i+1,j) → 2*(j*fw+i); vertical (i,j)→(i,j+1) → +1.
        long HKey(int i, int j) => 2L * (j * fw + i);
        long VKey(int i, int j) => 2L * (j * fw + i) + 1;

        var adj = new Dictionary<long, (long A, long B)>();
        void Link(long a, long b)
        {
            Add(a, b); Add(b, a);
            void Add(long from, long to)
            {
                if (adj.TryGetValue(from, out var e)) adj[from] = (e.A, to);
                else adj[from] = (to, -1);
            }
        }

        for (int j = 0; j < fh - 1; j++)
        for (int i = 0; i < fw - 1; i++)
        {
            bool tl = In(i, j), tr = In(i + 1, j), br = In(i + 1, j + 1), bl = In(i, j + 1);
            int c = (tl ? 1 : 0) | (tr ? 2 : 0) | (br ? 4 : 0) | (bl ? 8 : 0);
            if (c == 0 || c == 15) continue;
            long T = HKey(i, j), B = HKey(i, j + 1), L = VKey(i, j), R = VKey(i + 1, j);
            if (c == 5) { Link(T, L); Link(R, B); continue; }      // tl & br inside: keep separate
            if (c == 10) { Link(T, R); Link(B, L); continue; }     // tr & bl inside: keep separate
            var e = new List<long>(2);
            if (tl != tr) e.Add(T);
            if (tr != br) e.Add(R);
            if (br != bl) e.Add(B);
            if (bl != tl) e.Add(L);
            Link(e[0], e[1]);
        }

        Pt PointOf(long key)
        {
            int cell = (int)(key >> 1);
            int i = cell % fw, j = cell / fw;
            bool vertical = (key & 1) == 1;
            int i2 = vertical ? i : i + 1, j2 = vertical ? j + 1 : j;
            double a = f[j * fw + i], b = f[j2 * fw + i2];
            double t = Math.Abs(b - a) < 1e-9 ? 0.5 : (level - a) / (b - a);
            double px = i + t * (i2 - i), py = j + t * (j2 - j);
            // Field index → image pixel centre.
            return new Pt(x0 + px + 0.5, y0 + py + 0.5);
        }

        // Walk all loops and keep the one enclosing the largest area.
        var visited = new HashSet<long>();
        List<Pt> best = new();
        double bestArea = -1;
        foreach (var start in adj.Keys)
        {
            if (visited.Contains(start)) continue;
            var loop = new List<Pt>();
            long prev = -1, cur = start;
            while (true)
            {
                visited.Add(cur);
                loop.Add(PointOf(cur));
                var (a, b) = adj[cur];
                long next = a != prev ? a : b;
                if (next < 0 || next == start) break;
                if (visited.Contains(next)) break;
                prev = cur; cur = next;
            }
            double area = Polygon.Area(loop);
            if (area > bestArea) { bestArea = area; best = loop; }
        }
        return best;
    }
}
