using System;
using System.Collections.Generic;
using System.IO;
using CutoutEqD.Core;

// Self-contained test harness (no NuGet packages needed). Exit code = number of failures.
int failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}  {detail}");
    if (!ok) failures++;
}

// Render a synthetic scan: light background border, dark plate, light aperture defined by `inside`.
// 4x4 supersampling gives realistic anti-aliased edges.
GrayImage Render(int w, int h, Func<double, double, bool> inside, byte light = 225, byte dark = 70,
                 int border = 25, double noise = 0, int seed = 1, int specks = 0)
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

double RelErr(double a, double b) => Math.Abs(a - b) / b;

// 1. Circle
{
    double cx = 250.3, cy = 240.7, r = 100;
    var img = Render(500, 500, (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r);
    var res = CutoutDetector.Detect(img, 250, 240, new DetectionOptions { BlurSigma = 0 });
    double area = Polygon.Area(res.Outline), exp = Math.PI * r * r;
    Check("circle detected", res.Success, res.Message);
    Check("circle area within 0.3%", RelErr(area, exp) < 0.003, $"area={area:F1} expected={exp:F1} err={RelErr(area, exp):P3}");
    var m = FieldMetrics.From(res.Outline, 0.5); // 0.5 mm/px → r = 5 cm, D = 10 cm
    Check("circle EqD ≈ 10.00 cm", Math.Abs(m.EquivalentDiameterCm - 10) < 0.02, $"EqD={m.EquivalentDiameterCm:F3}");
    Check("circle 4A/P ≈ D", Math.Abs(m.FourAOverPCm - 10) < 0.05, $"4A/P={m.FourAOverPCm:F3}");
}

// 2. Rotated ellipse with noise, blur and dust specks inside
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
    // Dust: a few dark specks inside the aperture, one sitting on the seed region's interior.
    foreach (var (sx, sy) in new[] { (300, 260), (330, 240), (260, 290) })
        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) img[sx + dx, sy + dy] = 40;
    var res = CutoutDetector.Detect(img, 280, 250, new DetectionOptions { BlurSigma = 1.0 });
    double area = Polygon.Area(res.Outline), exp = Math.PI * a * b;
    Check("noisy ellipse detected", res.Success, res.Message);
    Check("noisy ellipse area within 0.5% (specks included)", RelErr(area, exp) < 0.005,
          $"area={area:F1} expected={exp:F1} err={RelErr(area, exp):P3} T={res.ThresholdUsed} light={res.LightLevel} dark={res.DarkLevel} verts={res.Outline.Count}");
}

// 3. Rectangle (axis-aligned, non-integer edges)
{
    double x0 = 100.25, y0 = 80.5, x1 = 300.25, y1 = 200.5; // 200 x 120
    var img = Render(420, 300, (x, y) => x >= x0 && x < x1 && y >= y0 && y < y1);
    var res = CutoutDetector.Detect(img, 200, 140, new DetectionOptions { BlurSigma = 0.8 });
    double area = Polygon.Area(res.Outline);
    Check("rectangle area within 0.5%", RelErr(area, 24000) < 0.005, $"area={area:F1} expected=24000 err={RelErr(area, 24000):P3}");
    var m = FieldMetrics.From(res.Outline, 1.0); // 1 mm/px → 20 x 12 cm; 4A/P = 4*240/64 = 15
    Check("rectangle 4A/P ≈ 15 cm", Math.Abs(m.FourAOverPCm - 15) < 0.1, $"4A/P={m.FourAOverPCm:F3}");
    Check("rectangle simplified to few vertices", res.Outline.Count <= 20, $"verts={res.Outline.Count}");
}

// 4. Seed on the background (not enclosed) → must fail cleanly
{
    var img = Render(300, 300, (x, y) => (x - 150) * (x - 150) + (y - 150) * (y - 150) < 60 * 60);
    var res = CutoutDetector.Detect(img, 5, 5, new DetectionOptions());
    Check("background click rejected", !res.Success, res.Message);
    var res2 = CutoutDetector.Detect(img, 150, 40, new DetectionOptions());
    Check("plate click rejected", !res2.Success, res2.Message);
}

// 5. Polygon utilities
{
    var sq = new List<Pt> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
    Check("square area", Math.Abs(Polygon.Area(sq) - 100) < 1e-9);
    Check("square perimeter", Math.Abs(Polygon.Perimeter(sq) - 40) < 1e-9);
    Check("contains", Polygon.Contains(sq, new Pt(5, 5)) && !Polygon.Contains(sq, new Pt(15, 5)));
    var dense = new List<Pt>();
    for (int i = 0; i < 2000; i++) { double t = 2 * Math.PI * i / 2000; dense.Add(new Pt(100 * Math.Cos(t), 100 * Math.Sin(t))); }
    var simp = Polygon.Simplify(dense, 0.2);
    Check("DP (0.2 px) keeps r=100 circle area within 0.3%", RelErr(Polygon.Area(simp), Polygon.Area(dense)) < 0.003,
          $"{dense.Count}→{simp.Count} verts");
    int e = Polygon.NearestEdge(sq, new Pt(5, 0.5), 2, out var proj);
    Check("nearest edge", e == 0 && Math.Abs(proj.Y) < 1e-9);
}

// 6. Real scan supplied by the user (PGM converted from the PNG), if present.
string pgm = args.Length > 0 ? args[0] : "";
if (File.Exists(pgm))
{
    var img = ReadPgm(pgm);
    double mmPerPx = args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
    int sx = args.Length > 3 ? int.Parse(args[2]) : img.Width / 2, sy = args.Length > 3 ? int.Parse(args[3]) : img.Height / 2;
    var res = CutoutDetector.Detect(img, sx, sy, new DetectionOptions());
    var m = FieldMetrics.From(res.Outline, mmPerPx);
    Console.WriteLine($"REAL  success={res.Success} T={res.ThresholdUsed} light={res.LightLevel} dark={res.DarkLevel} verts={res.Outline.Count}");
    Console.WriteLine($"REAL  area={m.AreaCm2:F2} cm²  EqD={m.EquivalentDiameterCm:F2} cm  4A/P={m.FourAOverPCm:F2} cm  ({res.Message})");
    using var sw = new StreamWriter(Path.ChangeExtension(pgm, ".outline.txt"));
    foreach (var p in res.Outline) sw.WriteLine(FormattableString.Invariant($"{p.X:F3} {p.Y:F3}"));
}

Console.WriteLine(failures == 0 ? "\nAll tests passed." : $"\n{failures} test(s) failed.");
return failures;

static GrayImage ReadPgm(string path)
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
