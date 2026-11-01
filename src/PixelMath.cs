using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PickCelling;

/// <summary>Color conversion, sampling, and bitmap helpers.</summary>
public static class PixelMath
{
    public static Color FromHsv(double h, double s, double v, int a = 255)
    {
        h -= Math.Floor(h); s = Math.Clamp(s, 0, 1); v = Math.Clamp(v, 0, 1);
        int i = (int)(h * 6);
        double f = h * 6 - i;
        double p = v * (1 - s);
        double q = v * (1 - f * s);
        double t = v * (1 - (1 - f) * s);
        double r, g, b;
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return Color.FromArgb(a, (int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    public static (double h, double s, double v) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 1e-9)
        {
            if (max == r) h = ((g - b) / d) % 6;
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
            if (h < 0) h += 1;
        }
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    public static Color Merge(Color a, Color b)
    {
        bool ae = a.A < 3, be = b.A < 3;
        if (ae && be) return Color.Transparent;
        if (ae) return b;
        if (be) return a;
        double total = a.A + b.A;
        return Color.FromArgb(
            Math.Max(a.A, b.A),
            (int)((a.R * a.A + b.R * b.A) / total),
            (int)((a.G * a.A + b.G * b.A) / total),
            (int)((a.B * a.A + b.B * b.A) / total));
    }

    public static float StrokeNoise(int x, int y, int seed)
    {
        unchecked
        {
            int h = seed;
            h = h * 73856093 ^ x;
            h = h * 19349663 ^ y;
            h ^= h >> 13;
            h *= (int)0x85EBCA6B;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    /// <summary>Fast Color[] → Bitmap (32bpp ARGB).</summary>
    public static Bitmap PixelsToBitmap(Color[] pixels, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bytes = new byte[data.Stride * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = pixels[y * w + x];
                    int p = y * data.Stride + x * 4;
                    bytes[p + 0] = c.B;
                    bytes[p + 1] = c.G;
                    bytes[p + 2] = c.R;
                    bytes[p + 3] = c.A;
                }
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    public static Color[] BitmapToPixels(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var pixels = new Color[w * h];
        var data = bmp.LockBits(new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bytes = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = y * data.Stride + x * 4;
                    pixels[y * w + x] = Color.FromArgb(bytes[p + 3], bytes[p + 2], bytes[p + 1], bytes[p + 0]);
                }
        }
        finally { bmp.UnlockBits(data); }
        return pixels;
    }

    public static Color[] ResizeNearest(Color[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new Color[dw * dh];
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                int sx = Math.Clamp((int)((float)x / dw * sw), 0, sw - 1);
                int sy = Math.Clamp((int)((float)y / dh * sh), 0, sh - 1);
                dst[y * dw + x] = src[sy * sw + sx];
            }
        return dst;
    }
}