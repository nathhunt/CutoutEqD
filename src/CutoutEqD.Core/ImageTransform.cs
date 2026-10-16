namespace CutoutEqD.Core;

/// <summary>
/// Rigid map between image-pixel coordinate systems, built from 90° rotations and crops:
/// p' = (A·x + B·y + Tx, C·x + D·y + Ty). Used to carry the outline and scale line along when the
/// image is rotated or cropped, and to undo all of that with <see cref="Inverse"/>.
/// Distances are preserved, so the scale (mm per pixel) stays valid.
/// </summary>
public readonly record struct ImageTransform(double A, double B, double C, double D, double Tx, double Ty)
{
    public static ImageTransform Identity => new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => this == Identity;

    public Pt Apply(Pt p) => new(A * p.X + B * p.Y + Tx, C * p.X + D * p.Y + Ty);

    /// <summary>This transform followed by <paramref name="next"/>.</summary>
    public ImageTransform Then(ImageTransform next) => new(
        next.A * A + next.B * C, next.A * B + next.B * D,
        next.C * A + next.D * C, next.C * B + next.D * D,
        next.A * Tx + next.B * Ty + next.Tx, next.C * Tx + next.D * Ty + next.Ty);

    /// <summary>Inverse map. The 2×2 part is a rotation, so its inverse is its transpose.</summary>
    public ImageTransform Inverse() => new(A, C, B, D, -(A * Tx + C * Ty), -(B * Tx + D * Ty));

    /// <summary>Rotate a <paramref name="width"/>×<paramref name="height"/> image 90° clockwise
    /// (pixel (i, j) moves to (height−1−j, i)).</summary>
    public static ImageTransform RotateClockwise(int width, int height) => new(0, -1, 1, 0, height, 0);

    /// <summary>Rotate 90° counter-clockwise (pixel (i, j) moves to (j, width−1−i)).</summary>
    public static ImageTransform RotateCounterClockwise(int width, int height) => new(0, 1, -1, 0, 0, width);

    /// <summary>Crop so that (x, y) becomes the new origin.</summary>
    public static ImageTransform Crop(int x, int y) => new(1, 0, 0, 1, -x, -y);
}
