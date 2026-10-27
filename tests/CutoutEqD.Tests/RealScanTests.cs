using System.Globalization;
using Xunit.Sdk;

namespace CutoutEqD.Tests;

/// <summary>
/// Optional check against a real scan. Skipped unless CUTOUTEQD_SCAN points at a binary PGM file:
/// <code>CUTOUTEQD_SCAN=scan.pgm CUTOUTEQD_MM_PER_PX=0.0847 CUTOUTEQD_SEED=812,640 dotnet test</code>
/// Writes the outline next to the scan (scan.outline.txt) and the measurements to the test output.
/// </summary>
public class RealScanTests(ITestOutputHelper output)
{
    [Fact]
    public void Detects_the_outline_in_a_real_scan()
    {
        string? path = Environment.GetEnvironmentVariable("CUTOUTEQD_SCAN");
        if (string.IsNullOrEmpty(path)) Assert.Skip("Set CUTOUTEQD_SCAN to a .pgm scan to run this test.");
        if (!File.Exists(path)) throw new FileNotFoundException("CUTOUTEQD_SCAN not found.", path);

        var img = TestImages.ReadPgm(path);
        double mmPerPx = double.TryParse(Environment.GetEnvironmentVariable("CUTOUTEQD_MM_PER_PX"),
                                         NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 1;
        int sx = img.Width / 2, sy = img.Height / 2;
        var seed = Environment.GetEnvironmentVariable("CUTOUTEQD_SEED")?.Split(',');
        if (seed is { Length: 2 }) { sx = int.Parse(seed[0], CultureInfo.InvariantCulture); sy = int.Parse(seed[1], CultureInfo.InvariantCulture); }

        var res = CutoutDetector.Detect(img, sx, sy, new DetectionOptions());
        var m = FieldMetrics.From(res.Outline, mmPerPx);
        output.WriteLine($"success={res.Success} T={res.ThresholdUsed} light={res.LightLevel} dark={res.DarkLevel} verts={res.Outline.Count}");
        output.WriteLine($"area={m.AreaCm2:F2} cm²  EqD={m.EquivalentDiameterCm:F2} cm  4A/P={m.FourAOverPCm:F2} cm  ({res.Message})");

        using (var sw = new StreamWriter(Path.ChangeExtension(path, ".outline.txt")))
            foreach (var p in res.Outline) sw.WriteLine(FormattableString.Invariant($"{p.X:F3} {p.Y:F3}"));

        Assert.True(res.Success, res.Message);
    }
}
