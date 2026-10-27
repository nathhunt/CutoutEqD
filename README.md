# Cutout Equivalent Diameter

A Windows desktop tool (.NET 8, WinForms). It measures the area and equivalent diameter (EqD) of a superficial or
orthovoltage radiotherapy cutout from a scan that includes a ruler.

## Build and run

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet run --project src\CutoutEqD.App -c Release
```

Or open `CutoutEqD.sln` in Visual Studio 2022 and press F5. In VS Code, open the folder and install the recommended
C# Dev Kit extension. *Run Build Task* (Ctrl+Shift+B) builds, F5 debugs the app, and *Tasks: Run Task* has test, run and publish. An image path can be passed on the command line: `CutoutEqD.exe scan.png`.

To build a single self-contained `.exe` for PCs without .NET installed:

```
dotnet publish src\CutoutEqD.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The version is set once in `Directory.Build.props`. Each build also records the git commit and the build date. The app
shows them in the title bar and status bar (e.g. `v1.1.0 (5ac6b10, 2026-10-27)`) and in the footer of the PDF report.

## Using it

1. **Load the image**: *Open…* (Ctrl+O), *Paste* (Ctrl+V) or drag and drop.
   Optionally *Rotate* (R / Shift+R) or *Crop* (C, then Enter). Rotating and cropping don't change the pixel spacing, so the scale stays valid.
2. **Set the scale** (S): click two ruler ticks far apart and enter the distance between them in mm.
   Zoom in with the mouse wheel; the magnifier shows the exact pixel. Either end can be dragged afterwards.
3. **Auto-detect** (A): click inside the light aperture.
4. **Check and adjust**:
   - *Edit outline* (E): drag, insert (click an edge) or delete (right-click / Delete) vertices. Arrow keys nudge by ¼ px (Shift: 1 px).
     *Soft drag* moves neighbouring vertices too, for smooth reshaping. Its *reach* slider (or [ and ]) sets how far along the
     outline the effect extends, in image pixels (shown in mm once the scale is set), independent of zoom. A blue circle previews it.
   - *Draw outline* (D): click round the edge by hand; Enter or double-click closes it.
   - *Threshold*: untick *Automatic threshold* to set it manually. The outline re-detects live unless it has been hand-edited.
   - Ctrl+Z / Ctrl+Y undo and redo.
5. **Results**: *Copy results* (Ctrl+C) or *Save PDF report…* (Ctrl+S). The PDF is one A4 page with EqD, area, perimeter, 4A/P,
   the scale and threshold settings, and the image with the outline drawn on it.

Pan with middle-drag, Space+drag or *Pan* (H). *Fit* (F) re-centres the image.

## How the outline is found

1. **Greyscale and smoothing.** The image is converted to luma, then given a Gaussian blur (σ = 1 px by default) to suppress scan noise.
2. **Threshold.** By default the edge is placed at 50 % grey, halfway between the aperture and the plate. Starting from Otsu's
   threshold [1], the app flood-fills from the click and takes the median grey level in a band 3–10 px inside and outside the boundary.
   It sets the threshold to their midpoint and repeats until the value stops changing (up to 3 times).
   The result doesn't depend on the overall image brightness.
3. **Region.** A flood fill from the click collects the connected light region; dust specks inside it are filled in.
   If the region reaches the image border, the click wasn't inside an enclosed aperture, and detection stops with a message rather than guessing.
4. **Sub-pixel edge.** Marching squares [2] traces the boundary through the grey values at the threshold level,
   interpolating between pixel centres. Douglas–Peucker [3] (0.2 px tolerance) simplifies the contour to an editable polygon.
5. **Measurements.** Area is calculated from the polygon with the shoelace formula, so hand edits change the result exactly as shown.

**EqD = 2·√(A/π)**: the diameter of the circle with the same area as the cutout. Kilovoltage dosimetry data, such as backscatter factors,
are tabulated for circular fields [4, 5], so the equal-area circle is used. **Check that this matches your department's protocol.**
4A/P [6] is shown for reference only; for a circle it equals the diameter.

## Accuracy

`dotnet run --project tests\CutoutEqD.Tests` runs a dependency-free test harness on synthetic scans with known areas:

| Case | Area error |
|---|---|
| Circle, r = 100 px | 0.01 % |
| Rotated ellipse with noise (σ = 10 grey levels) and dark specks inside | 0.06 % |
| Rectangle 200 × 120 px | 0.10 % |

## Things that affect the result

- **Ruler in the same plane as the cutout.** Fine on a flatbed scanner. With a camera, shoot square-on from a distance.
- **Scale clicks.** At ~3 px/mm, a 1 px error over 200 mm is about 0.15 % in EqD. Use widely separated ticks and zoom in.
- **Bevelled plate edges** can appear as a grey band, which moves the 50 % edge. Check the outline at high zoom before accepting it.
- **Resolution.** Scan at ≥ 2 px/mm; 300 dpi (≈ 11.8 px/mm) is plenty.

## References

1. Otsu N. A threshold selection method from gray-level histograms. *IEEE Trans Syst Man Cybern* 1979;9(1):62–66.
2. Lorensen WE, Cline HE. Marching cubes: a high resolution 3D surface construction algorithm. *Comput Graph* 1987;21(4):163–169.
3. Douglas DH, Peucker TK. Algorithms for the reduction of the number of points required to represent a digitized line or its caricature.
   *Cartographica* 1973;10(2):112–122.
4. Klevenhagen SC, Aukett RJ, Harrison RM, Moretti C, Nahum AE, Rosser KE. The IPEMB code of practice for the determination of
   absorbed dose for x-rays below 300 kV generating potential. *Phys Med Biol* 1996;41:2605–2625.
5. Ma C-M, Coffey CW, DeWerd LA, Liu C, Nath R, Seltzer SM, Seuntjens JP. AAPM protocol for 40–300 kV x-ray beam dosimetry in
   radiotherapy and radiobiology (TG-61). *Med Phys* 2001;28(6):868–893.
6. Sterling TD, Perry H, Katz L. Automation of radiation treatment planning IV. *Br J Radiol* 1964;37:544–550.
