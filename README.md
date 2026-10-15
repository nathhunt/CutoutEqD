# Cutout Equivalent Diameter

A Windows desktop tool (.NET 8, WinForms) that measures the area and equivalent diameter of a
superficial / orthovoltage radiotherapy cutout from a scan or photo that includes a ruler.

## Build and run (Windows)

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet run --project src\CutoutEqD.App -c Release
```

Or open `CutoutEqD.sln` in Visual Studio 2022 and press F5.

To make a single self-contained `.exe` you can copy to other PCs (no .NET install needed there):

```
dotnet publish src\CutoutEqD.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

You can also pass an image path on the command line: `CutoutEqD.exe scan.png`.

## Using it

1. **Load the image.** Use *Open…* (Ctrl+O), *Paste* (Ctrl+V, handy for screenshots), or drag a file onto the window.
2. **Set the scale** (S). Click one ruler tick, then another tick far from it, e.g. 100 mm and 300 mm.
   Enter the distance between them in mm. Zoom with the mouse wheel; the magnifier in the corner shows exactly which pixel you are on.
   Drag either magenta end-mark afterwards to refine it.
3. **Auto-detect** (A). Click anywhere inside the light aperture. The edge is found automatically and the results update.
4. **Adjust if needed:**
   - *Edit outline* (E): drag a vertex; click on an edge to insert a vertex there; right-click or Delete removes one.
     Arrow keys nudge the highlighted vertex by ¼ px (Shift: 1 px). *Soft drag* moves nearby vertices too, for smooth reshaping.
   - *Draw outline* (D): click around the edge by hand. Right-click or Backspace removes the last point.
     Click the first point, double-click or press Enter to close.
   - *Threshold*: untick *Automatic threshold* and move the slider. The outline re-detects live, unless you've hand-edited it.
   - Ctrl+Z / Ctrl+Y undo and redo outline changes.
5. **Results:** *Copy results* (Ctrl+C) copies a text summary. *Save annotated image…* (Ctrl+S) writes a PNG with the outline, scale line and numbers for the record.

Panning: middle-drag, Space+drag, or *Pan* mode (H). *Fit* (F) re-centres the image.

## How it works

- **Greyscale and smoothing.** The image is converted to luma. A small Gaussian blur (default σ = 1 px) suppresses scanner noise.
- **Threshold.** By default the edge is placed at the 50 % grey level, halfway between two medians:
  the light level just inside the aperture and the dark level just outside it.
  These are measured in a 3–10 px band either side of the boundary and iterated from an Otsu starting point.
  This makes the result independent of the overall image brightness.
- **Region.** A flood fill from your click takes the connected light region. Dust specks inside it are filled in.
  If the region reaches the image border, the click wasn't inside an enclosed aperture, and the tool says so instead of guessing.
- **Sub-pixel edge.** Marching squares interpolates the grey values at the threshold level.
  The contour is then simplified (Douglas–Peucker, 0.2 px tolerance) into an editable polygon.
- **Area** comes from the shoelace formula on that polygon. Edits therefore change the result exactly as you see them.

### Equivalent diameter

**EqD = 2·√(A/π)**: the diameter of the circle with the same area as the outline.

Kilovoltage dosimetry data such as backscatter factors are tabulated against the diameter of circular fields
(IPEMB code of practice, Klevenhagen et al. 1996; AAPM TG-61, Ma et al. 2001; see also Smith et al. 2011,
who measured BSFs for 2–6 cm circular fields against TG-61 values). An irregular cutout therefore has to be reduced to an equivalent circle.
The equal-area circle is the conversion this tool uses. **Confirm it matches your department's protocol**, since some centres use other methods.

**4A/P** (Sterling et al. 1964) is also shown for reference. It was derived for rectangular megavoltage fields; for a circle it equals the diameter.

## Accuracy checks

`tests/CutoutEqD.Tests` is a dependency-free console test harness: `dotnet run --project tests\CutoutEqD.Tests`.
It renders synthetic scans with known areas and anti-aliased edges, and checks the detected values:

| Case | Area error |
|---|---|
| Circle, r = 100 px | 0.01 % |
| Rotated ellipse with noise (σ = 10 grey levels) and dark specks inside | 0.06 % |
| Rectangle 200 × 120 px | 0.10 % |

On the example scan supplied with the request (ruler ≈ 3.23 px/mm), auto-detect gave EqD ≈ 9.48–9.50 cm.
The cutout is labelled 9.52 cm.

## Things that affect the result

- **Ruler in the same plane as the cutout.** On a flatbed scanner both lie on the glass, so this is fine.
  With a camera, keep it square-on and far away, or the ruler and aperture will be magnified differently.
- **Scale clicks.** At ~3 px/mm, a 1 px error over a 200 mm reference is about 0.15 % in scale, and 0.15 % in EqD.
  Use widely separated ticks and zoom in.
- **Edge placement.** The cut edge of the plate can show as a grey bevel. The 50 % rule usually lands on the inner (aperture) edge.
  On the example scan, raising the threshold from 167 to 197 grey levels changed EqD by about 1 %.
  Check the outline at high zoom before accepting it.
- **Low resolution.** Below ~2 px/mm, scan at a higher dpi (300 dpi ≈ 11.8 px/mm is plenty).

## References

- Klevenhagen SC, Aukett RJ, Harrison RM, Moretti C, Nahum AE, Rosser KE. The IPEMB code of practice for the determination of
  absorbed dose for x-rays below 300 kV generating potential. *Phys Med Biol* 1996;41:2605–2625.
- Ma C-M, Coffey CW, DeWerd LA, Liu C, Nath R, Seltzer SM, Seuntjens JP. AAPM protocol for 40–300 kV x-ray beam dosimetry in
  radiotherapy and radiobiology (TG-61). *Med Phys* 2001;28(6):868–893.
- Smith L, Hill R, Nakano M, Kim J, Kuncic Z. The measurement of backscatter factors of kilovoltage X-ray beams using
  Gafchromic EBT2 film. *Australas Phys Eng Sci Med* 2011;34(2):261–266. https://doi.org/10.1007/s13246-011-0073-5
- Sterling TD, Perry H, Katz L. Automation of radiation treatment planning IV: derivation of a mathematical expression for the
  per cent depth dose surface of cobalt 60 beams and visualisation of multiple field dose distributions. *Br J Radiol* 1964;37:544–550.
