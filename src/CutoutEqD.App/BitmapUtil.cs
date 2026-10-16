using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using CutoutEqD.Core;

namespace CutoutEqD.App
{
    internal static class BitmapUtil
    {
        /// <summary>Loads an image into a 32bpp bitmap without keeping the file locked.</summary>
        public static Bitmap Load(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            ms.Position = 0;
            using var src = System.Drawing.Image.FromStream(ms);
            return To32bpp(src);
        }

        public static Bitmap To32bpp(System.Drawing.Image src)
        {
            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            bmp.SetResolution(src.HorizontalResolution > 0 ? src.HorizontalResolution : 96,
                              src.VerticalResolution > 0 ? src.VerticalResolution : 96);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);                       // transparent areas count as light background
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height));
            return bmp;
        }

        /// <summary>JPEG-encodes the image on a white background, downsampled so its longer side is at most
        /// <paramref name="maxSide"/> pixels (keeps the PDF a sensible size).</summary>
        public static byte[] ToJpeg(Bitmap src, int maxSide, out int width, out int height, long quality = 92)
        {
            double s = Math.Min(1.0, (double)maxSide / Math.Max(src.Width, src.Height));
            width = Math.Max(1, (int)Math.Round(src.Width * s));
            height = Math.Max(1, (int)Math.Round(src.Height * s));
            using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(src, new Rectangle(0, 0, width, height));
            }
            var codec = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var ms = new MemoryStream();
            if (codec == null) bmp.Save(ms, ImageFormat.Jpeg);
            else
            {
                using var ps = new EncoderParameters(1);
                ps.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                bmp.Save(ms, codec, ps);
            }
            return ms.ToArray();
        }

                /// <summary>Luma (Rec. 601 weights) greyscale copy of a 32bpp ARGB bitmap.</summary>
        public static GrayImage ToGray(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                var row = new byte[Math.Abs(stride)];
                var gray = new byte[w * h];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * stride), row, 0, row.Length);
                    for (int x = 0; x < w; x++)
                    {
                        int o = x * 4;                  // B, G, R, A
                        int v = (114 * row[o] + 587 * row[o + 1] + 299 * row[o + 2] + 500) / 1000;
                        gray[y * w + x] = (byte)v;
                    }
                }
                return new GrayImage(w, h, gray);
            }
            finally { bmp.UnlockBits(data); }
        }
    }
}
