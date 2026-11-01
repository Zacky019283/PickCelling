namespace PickCelling;

/// <summary>Composite auto-shade pass (light direction, centering glow, edge shadow).</summary>
public static class AutoShader
{
    public static void Apply(CanvasGrid grid, AutoShadeSettings s)
    {
        int w = grid.CompositeWidth, h = grid.CompositeHeight;
        int size = grid.Size, cols = grid.Cols;

        var orig = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                orig[y * w + x] = grid.GetComposite(x, y);

        int minX = w, maxX = -1, minY = h, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (orig[y * w + x].A < 3) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        if (maxX < minX || maxY < minY) return;

        float spanX = Math.Max(1, maxX - minX);
        float spanY = Math.Max(1, maxY - minY);
        float cx0 = (minX + maxX) * 0.5f;
        float cy0 = (minY + maxY) * 0.5f;
        float halfSpanX = Math.Max(1, spanX * 0.5f);
        float halfSpanY = Math.Max(1, spanY * 0.5f);

        float rad = s.LightAngle * MathF.PI / 180f;
        float lx = MathF.Cos(rad), ly = MathF.Sin(rad);

        float lightI = s.LightIntensity / 100f;
        float shadowI = s.ShadowIntensity / 100f;
        float borderC = s.BorderClarity / 100f;
        float contrast = s.Contrast / 100f;
        float ambient = s.AmbientFloor / 100f;
        float blend = s.BlendAmount / 100f;

        const float warmHue = 0.08f, coolHue = 0.60f, edgeWidth = 0.25f;

        var result = new Color[w * h];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                Color c = orig[i];
                if (c.A < 3) { result[i] = c; continue; }

                var (hue, sat, val) = PixelMath.ToHsv(c);

                float u = (x - cx0) / halfSpanX;
                float v = (y - cy0) / halfSpanY;
                float bu = (x - minX) / spanX;
                float bv = (y - minY) / spanY;

                int subIdx = Math.Clamp((y / size) * cols + (x / size), 0, grid.Count - 1);

                bool centerOn = grid.CenteringEnabled[subIdx];
                float centerU = grid.CenteringPoints[subIdx].X;
                float centerV = grid.CenteringPoints[subIdx].Y;

                float roughI = grid.Roughness[subIdx] / 100f;
                float reflectionI = grid.Reflection[subIdx] / 100f;
                float centerFalloff = grid.FalloffStrength[subIdx] / 100f;
                float edgeShadowI = grid.EdgeShadow[subIdx] / 100f;
                float hueShift = grid.HueShift[subIdx] / 100f;
                float satBoost = grid.SatBoost[subIdx] / 100f;
                float falloffExp = 0.5f + centerFalloff * 7.5f;

                float localU = ((x % size) + 0.5f) / size;
                float localV = ((y % size) + 0.5f) / size;

                float proj = u * lx + v * ly;
                float t = Math.Clamp((proj + 1) * 0.5f, 0, 1);

                switch (s.Falloff)
                {
                    case FalloffMode.Smooth: t = t * t * (3 - 2 * t); break;
                    case FalloffMode.Soft: t = MathF.Pow(t, 1.5f); break;
                }

                float dvN = 0f, dvC = 0f;

                if (t > 0.5f) dvN += (t - 0.5f) * 2 * lightI;
                else dvN -= (0.5f - t) * 2 * shadowI;

                if (borderC > 0)
                {
                    float ox = 0, oy = 0;
                    bool isEdge = false;
                    if (y + 1 >= h || orig[(y + 1) * w + x].A < 3) { oy += 1; isEdge = true; }
                    if (y - 1 < 0 || orig[(y - 1) * w + x].A < 3) { oy -= 1; isEdge = true; }
                    if (x + 1 >= w || orig[y * w + x + 1].A < 3) { ox += 1; isEdge = true; }
                    if (x - 1 < 0 || orig[y * w + x - 1].A < 3) { ox -= 1; isEdge = true; }
                    if (isEdge)
                    {
                        float baseDark = 0.32f * borderC;
                        float mod = 1f;
                        float olen = MathF.Sqrt(ox * ox + oy * oy);
                        if (olen > 1e-4f)
                        {
                            ox /= olen; oy /= olen;
                            float tlx = centerU - localU, tly = centerV - localV;
                            float tlen = MathF.Sqrt(tlx * tlx + tly * tly);
                            if (tlen > 1e-4f)
                            {
                                tlx /= tlen; tly /= tlen;
                                mod = 1f - (ox * tlx + oy * tly) * 0.4f;
                            }
                        }
                        dvN -= baseDark * mod;
                    }
                }

                if (reflectionI > 0)
                {
                    float du = localU - centerU;
                    float dvv = localV - centerV;
                    float dist = MathF.Sqrt(du * du + dvv * dvv);
                    dvC += reflectionI * MathF.Pow(MathF.Max(0, 1 - dist), falloffExp) * 0.55f;
                }

                if (edgeShadowI > 0)
                {
                    float e = Math.Min(bu, 1 - bu);
                    e = Math.Min(e, Math.Min(bv, 1 - bv));
                    if (e < edgeWidth) dvN -= edgeShadowI * (1 - e / edgeWidth) * 0.30f;
                }

                dvN += ((float)Random.Shared.NextDouble() * 2 - 1) * roughI * 0.15f;

                float dv = (centerOn ? dvN + dvC : dvN) * contrast;
                val = Math.Clamp(val + dv, ambient, 1);

                float lightSide = Math.Max(0, t - 0.5f) * 2;
                float shadowSide = Math.Max(0, 0.5f - t) * 2;

                sat = Math.Clamp(sat + satBoost * (lightSide - shadowSide * 0.5), 0, 1);

                if (hueShift > 0)
                {
                    if (lightSide > 0)
                    {
                        double d = warmHue - hue;
                        if (d > 0.5) d -= 1; if (d < -0.5) d += 1;
                        hue += d * hueShift * lightSide;
                    }
                    if (shadowSide > 0)
                    {
                        double d = coolHue - hue;
                        if (d > 0.5) d -= 1; if (d < -0.5) d += 1;
                        hue += d * hueShift * shadowSide;
                    }
                    hue -= Math.Floor(hue);
                }

                Color shaded = PixelMath.FromHsv(hue, sat, val, c.A);
                result[i] = PixelMath.Lerp(c, shaded, blend);
            }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                grid.SetComposite(x, y, result[y * w + x]);
    }
}