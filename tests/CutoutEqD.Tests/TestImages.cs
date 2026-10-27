namespace CutoutEqD.Tests;

/// <summary>Synthetic scans and small helpers shared by the tests.</summary>
internal static class TestImages
{
    public const byte Light = 225, Dark = 70;

    /// <summary>Renders a synthetic scan: light border (the scanner bed), dark plate, and a light aperture where
    /// <paramref name="inside"/> is true. 4×4 supersampling gives realistic anti-aliased edges.</summary>
    public static GrayImage Render(int w, int h, Func<double, double, bool> inside, byte light = Light, byte dark = Dark,
                                   int border = 25, double noise = 0, int seed = 1)
    {
        var img = new GrayImage(w, h);
        var rng = new Random(seed);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double v;
            if (x < border || y < border || x >= w - border || y >= h - border) v = light;
            else
            {
                int n = 0;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                    if (inside(x + (sx + 0.5) / 4, y + (sy + 0.5) / 4)) n++;
                v = dark + (light - dark) * n / 16.0;
            }
            if (noise > 0)
            {
                double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble();
                v += noise * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
            }
            img[x, y] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
        }
        return img;
    }

    public static Func<double, double, bool> Circle(double cx, double cy, double r) =>
        (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

    /// <summary>Paints a (2·radius+1)² square of the given grey level centred on (cx, cy).</summary>
    public static void Speck(GrayImage img, int cx, int cy, int radius = 2, byte value = 40)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
            img[cx + dx, cy + dy] = value;
    }

    /// <summary>Binary image: 255 where <paramref name="on"/> is true, 0 elsewhere.</summary>
    public static GrayImage Binary(int w, int h, Func<int, int, bool> on)
    {
        var img = new GrayImage(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            img[x, y] = on(x, y) ? (byte)255 : (byte)0;
        return img;
    }

    public static List<Pt> RegularPolygon(int n, double cx, double cy, double r)
    {
        var p = new List<Pt>(n);
        for (int i = 0; i < n; i++)
        {
            double t = 2 * Math.PI * i / n;
            p.Add(new Pt(cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
        }
        return p;
    }

    public static List<Pt> Square(double x0, double y0, double side) =>
        new() { new(x0, y0), new(x0 + side, y0), new(x0 + side, y0 + side), new(x0, y0 + side) };

    public static double RelErr(double actual, double expected) => Math.Abs(actual - expected) / expected;

    /// <summary>Reads a binary (P5) 8-bit PGM file.</summary>
    public static GrayImage ReadPgm(string path)
    {
        var bytes = File.ReadAllBytes(path);
        int pos = 0;
        string Token()
        {
            while (char.IsWhiteSpace((char)bytes[pos])) pos++;
            int st = pos; while (!char.IsWhiteSpace((char)bytes[pos])) pos++;
            return System.Text.Encoding.ASCII.GetString(bytes, st, pos - st);
        }
        if (Token() != "P5") throw new InvalidDataException("Expected binary PGM");
        int w = int.Parse(Token()), h = int.Parse(Token()); Token();
        pos++;
        var data = new byte[w * h];
        Array.Copy(bytes, pos, data, 0, w * h);
        return new GrayImage(w, h, data);
    }
}
