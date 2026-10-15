using System;
using System.Collections.Generic;

namespace CutoutEqD.Core;

public sealed class DetectionOptions
{
    /// <summary>Fixed threshold (0–255). Null = automatic (edge placed at 50% between local light and dark levels).</summary>
    public int? Threshold { get; set; }
    /// <summary>Gaussian pre-smoothing in pixels, to suppress scan noise. 0 = none.</summary>
    public double BlurSigma { get; set; } = 1.0;
    /// <summary>Douglas–Peucker tolerance (px) for the editable outline. 0 = keep every contour point.</summary>
    public double SimplifyTolerance { get; set; } = 0.2;
    /// <summary>Reject regions larger than this fraction of the image (seed was probably in the background).</summary>
    public double MaxAreaFraction { get; set; } = 0.9;
}

public sealed class DetectionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public List<Pt> Outline { get; init; } = new();
    public int ThresholdUsed { get; init; }
    public int LightLevel { get; init; }
    public int DarkLevel { get; init; }
    public int PixelCount { get; init; }
}

public static class CutoutDetector
{
    /// <param name="image">Greyscale image of the cutout.</param>
    /// <param name="blurred">Optional pre-blurred copy (cache it across clicks); computed if null.</param>
    public static DetectionResult Detect(GrayImage image, int seedX, int seedY, DetectionOptions options,
                                         GrayImage? blurred = null)
    {
        var img = blurred ?? Imaging.GaussianBlur(image, options.BlurSigma);
        int w = img.Width, h = img.Height;
        if (seedX < 0 || seedY < 0 || seedX >= w || seedY >= h)
            return Fail("Click inside the image.");

        int light = 0, dark = 0;
        int t;
        if (options.Threshold is int fixedT)
        {
            t = Math.Clamp(fixedT, 1, 255);
        }
        else
        {
            t = Imaging.OtsuThreshold(img);
            // Refine: put the edge halfway between the light level just inside and the dark
            // level just outside the found boundary (a 50% intensity edge criterion).
            for (int iter = 0; iter < 3; iter++)
            {
                var m = Imaging.FloodFillLight(img, seedX, seedY, t, out bool leak, out int n);
                if (n == 0 || leak) break;
                var filled0 = Imaging.FillHoles(m, w, h);
                if (!BandLevels(img, filled0, 3, 10, out light, out dark)) break;
                int nt = Math.Clamp((light + dark + 1) / 2, 1, 255);
                if (nt == t) break;
                t = nt;
            }
        }

        if (img.Data[seedY * w + seedX] < t)
            return Fail($"The clicked point is darker than the threshold ({t}). Click in the light aperture, or lower the threshold.", t);

        var region = Imaging.FloodFillLight(img, seedX, seedY, t, out bool touches, out int count);
        if (touches)
            return Fail("The light region reaches the image edge, so it isn't enclosed by the cutout. " +
                        "Click inside the aperture, lower the threshold, or draw the outline manually.", t);
        if (count > options.MaxAreaFraction * w * h)
            return Fail("The detected region is implausibly large.", t);
        if (count < 20)
            return Fail("The detected region is too small — try clicking nearer the middle of the aperture.", t);

        var filled = Imaging.FillHoles(region, w, h);
        var contour = Imaging.TraceContour(img, region, filled, t);
        if (contour.Count < 3) return Fail("Could not trace the outline.", t);

        if (light == 0 && dark == 0) BandLevels(img, filled, 3, 10, out light, out dark);

        var outline = Polygon.Simplify(contour, options.SimplifyTolerance);
        int filledCount = 0; foreach (var b in filled) if (b) filledCount++;
        return new DetectionResult
        {
            Success = true,
            Message = $"Outline found ({outline.Count} vertices, threshold {t}).",
            Outline = outline,
            ThresholdUsed = t,
            LightLevel = light,
            DarkLevel = dark,
            PixelCount = filledCount,
        };

        static DetectionResult Fail(string msg, int thr = 0) => new() { Success = false, Message = msg, ThresholdUsed = thr };
    }

    /// <summary>Median grey level in a band [dMin, dMax] pixels inside and outside the mask boundary.</summary>
    public static bool BandLevels(GrayImage img, bool[] mask, int dMin, int dMax, out int inside, out int outside)
    {
        int w = img.Width, h = img.Height;
        var dist = new int[w * h];
        Array.Fill(dist, int.MaxValue);
        var q = new Queue<int>();
        // Seed with all pixels on either side of the boundary (distance 1).
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x; bool v = mask[i];
            if ((x > 0 && mask[i - 1] != v) || (x < w - 1 && mask[i + 1] != v) ||
                (y > 0 && mask[i - w] != v) || (y < h - 1 && mask[i + w] != v))
            { dist[i] = 1; q.Enqueue(i); }
        }
        var hin = new int[256]; var hout = new int[256];
        while (q.Count > 0)
        {
            int i = q.Dequeue();
            int d = dist[i];
            if (d >= dMin) { if (mask[i]) hin[img.Data[i]]++; else hout[img.Data[i]]++; }
            if (d >= dMax) continue;
            int x = i % w, y = i / w;
            void Visit(int j) { if (dist[j] == int.MaxValue && mask[j] == mask[i]) { dist[j] = d + 1; q.Enqueue(j); } }
            if (x > 0) Visit(i - 1);
            if (x < w - 1) Visit(i + 1);
            if (y > 0) Visit(i - w);
            if (y < h - 1) Visit(i + w);
        }
        inside = Median(hin); outside = Median(hout);
        return inside >= 0 && outside >= 0 && inside > outside;
    }

    private static int Median(int[] hist)
    {
        long total = 0; foreach (var c in hist) total += c;
        if (total == 0) return -1;
        long half = (total + 1) / 2, acc = 0;
        for (int i = 0; i < 256; i++) { acc += hist[i]; if (acc >= half) return i; }
        return 255;
    }
}
