namespace PickCelling;

/// <summary>Applies stone pits, stripes, and plank lines to a pixel.</summary>
public static class Effects
{
    public static Color Apply(Color baseColor, int x, int y, int roughness255,
                               EffectSettings s, int strokeSeed = -1)
    {
        if (baseColor.A == 0) return baseColor;
        Color c = baseColor;

        float r1 = strokeSeed >= 0
            ? PixelMath.StrokeNoise(x, y, strokeSeed)
            : (float)Random.Shared.NextDouble();
        float r2 = strokeSeed >= 0
            ? PixelMath.StrokeNoise(x, y, strokeSeed ^ 0x12345)
            : (float)Random.Shared.NextDouble();

        if (roughness255 > 0)
        {
            var (h, sv, v) = PixelMath.ToHsv(c);
            float amt = roughness255 / 255f;
            v = Math.Clamp(v + (r1 * 2 - 1) * amt, 0, 1);
            c = PixelMath.FromHsv(h, sv, v, c.A);
        }

        var (hh, ss, vv) = PixelMath.ToHsv(c);
        bool changed = false;

        if (s.StripesEnabled)
        {
            int w = Math.Max(1, s.StripeWidth);
            int idx = s.StripeDir switch
            {
                StripeDirection.Vertical => x / w,
                StripeDirection.Diagonal => (x + y) / w,
                _ => y / w
            };
            float shift = (idx % 2 == 0) ? -s.StripeIntensity / 255f : s.StripeIntensity / 255f;
            vv = Math.Clamp(vv + shift * 0.5, 0, 1);
            changed = true;
        }

        if (s.PlankLinesEnabled && y % Math.Max(1, s.PlankLineSpacing) == 0)
        {
            vv = Math.Clamp(vv - s.PlankLineDarkness / 255.0, 0, 1);
            changed = true;
        }

        if (s.StonePitsEnabled && r2 < s.StonePitChance / 100f)
        {
            float variation = 0.5f + (strokeSeed >= 0
                ? PixelMath.StrokeNoise(x, y, strokeSeed ^ 0x67890)
                : (float)Random.Shared.NextDouble()) * 0.5f;
            vv = Math.Clamp(vv - (s.StonePitDepth / 255.0) * variation, 0, 1);
            changed = true;
        }

        return changed ? PixelMath.FromHsv(hh, ss, vv, c.A) : c;
    }
}