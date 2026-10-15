// Shims used only when compiling for .NET Framework 4.8 (the standalone test .exe).
// The .NET 8 build ignores this file.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }

namespace CutoutEqD.Core
{
    internal static class Math
    {
        public const double PI = System.Math.PI;
        public static double Sqrt(double v) => System.Math.Sqrt(v);
        public static double Abs(double v) => System.Math.Abs(v);
        public static double Exp(double v) => System.Math.Exp(v);
        public static double Ceiling(double v) => System.Math.Ceiling(v);
        public static double Round(double v) => System.Math.Round(v);
        public static double Cos(double v) => System.Math.Cos(v);
        public static int Max(int a, int b) => System.Math.Max(a, b);
        public static int Min(int a, int b) => System.Math.Min(a, b);
        public static double Max(double a, double b) => System.Math.Max(a, b);
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    internal static class Array
    {
        public static void Fill<T>(T[] a, T v) { for (int i = 0; i < a.Length; i++) a[i] = v; }
    }
}
#endif
