using System.Drawing.Imaging;
using System.Text.Json;

namespace PickCelling;

/// <summary>Project file schema (JSON with base64-encoded PNG per canvas).</summary>
public class ProjectData
{
    public int Version { get; set; } = 1;
    public int Size { get; set; }
    public int Cols { get; set; }
    public int Rows { get; set; }
    public List<string> Canvases { get; set; } = new();
    public List<bool> CenteringEnabled { get; set; } = new();
    public List<float> CenteringX { get; set; } = new();
    public List<float> CenteringY { get; set; } = new();
    public List<int> Roughness { get; set; } = new();
    public List<int> Reflection { get; set; } = new();
    public List<int> Falloff { get; set; } = new();
    public List<int> EdgeShadow { get; set; } = new();
    public List<int> HueShift { get; set; } = new();
    public List<int> SatBoost { get; set; } = new();
}

public class AppSettings
{
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    public List<string> RecentFiles { get; set; } = new();
}

/// <summary>Project, image, and settings serialization.</summary>
public static class FileIO
{
    public static string AppDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pick-celling");

    public static string TexturesDir => Path.Combine(AppDir, "Textures");
    public static string SettingsPath => Path.Combine(AppDir, "settings.json");

    public static void EnsureFolders()
    {
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(TexturesDir);
    }

    public static byte[] EncodePng(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public static Bitmap DecodePng(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var tmp = new Bitmap(ms);
        return new Bitmap(tmp);
    }

    public static Bitmap LoadPng(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return DecodePng(bytes);
    }

    public static void SaveProject(string path, CanvasGrid grid, AutoShadeSettings shade)
    {
        var data = new ProjectData
        {
            Size = grid.Size,
            Cols = grid.Cols,
            Rows = grid.Rows
        };
        for (int i = 0; i < grid.Count; i++)
        {
            var bmp = PixelMath.PixelsToBitmap(grid.Canvases[i], grid.Size, grid.Size);
            try { data.Canvases.Add(Convert.ToBase64String(EncodePng(bmp))); }
            finally { bmp.Dispose(); }

            data.CenteringEnabled.Add(grid.CenteringEnabled[i]);
            data.CenteringX.Add(grid.CenteringPoints[i].X);
            data.CenteringY.Add(grid.CenteringPoints[i].Y);
            data.Roughness.Add(grid.Roughness[i]);
            data.Reflection.Add(grid.Reflection[i]);
            data.Falloff.Add(grid.FalloffStrength[i]);
            data.EdgeShadow.Add(grid.EdgeShadow[i]);
            data.HueShift.Add(grid.HueShift[i]);
            data.SatBoost.Add(grid.SatBoost[i]);
        }
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(data, opts));
    }

    public static CanvasGrid LoadProject(string path)
    {
        string json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<ProjectData>(json);
        if (data == null || data.Size < 4) throw new InvalidDataException("Invalid project file.");

        var grid = new CanvasGrid(data.Size, data.Cols, data.Rows);
        int n = Math.Min(data.Canvases.Count, grid.Count);
        for (int i = 0; i < n; i++)
        {
            byte[] bytes = Convert.FromBase64String(data.Canvases[i]);
            using var bmp = DecodePng(bytes);
            var pixels = PixelMath.BitmapToPixels(bmp);
            if (bmp.Width != grid.Size || bmp.Height != grid.Size)
                pixels = PixelMath.ResizeNearest(pixels, bmp.Width, bmp.Height, grid.Size, grid.Size);
            Array.Copy(pixels, grid.Canvases[i], Math.Min(pixels.Length, grid.Canvases[i].Length));

            if (i < data.CenteringEnabled.Count) grid.CenteringEnabled[i] = data.CenteringEnabled[i];
            if (i < data.CenteringX.Count && i < data.CenteringY.Count)
                grid.CenteringPoints[i] = new PointF(data.CenteringX[i], data.CenteringY[i]);
            if (i < data.Roughness.Count) grid.Roughness[i] = data.Roughness[i];
            if (i < data.Reflection.Count) grid.Reflection[i] = data.Reflection[i];
            if (i < data.Falloff.Count) grid.FalloffStrength[i] = data.Falloff[i];
            if (i < data.EdgeShadow.Count) grid.EdgeShadow[i] = data.EdgeShadow[i];
            if (i < data.HueShift.Count) grid.HueShift[i] = data.HueShift[i];
            if (i < data.SatBoost.Count) grid.SatBoost[i] = data.SatBoost[i];
        }
        return grid;
    }

    public static void ExportComposite(string path, CanvasGrid grid)
    {
        int w = grid.CompositeWidth, h = grid.CompositeHeight;
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = grid.GetComposite(x, y);
        using var bmp = PixelMath.PixelsToBitmap(pixels, w, h);
        bmp.Save(path, ImageFormat.Png);
    }

    public static void MergeAllCanvases(string path, CanvasGrid grid)
    {
        var pixels = new Color[grid.Size * grid.Size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.Transparent;
        for (int c = 0; c < grid.Count; c++)
            for (int p = 0; p < pixels.Length; p++)
                pixels[p] = PixelMath.Merge(pixels[p], grid.Canvases[c][p]);
        using var bmp = PixelMath.PixelsToBitmap(pixels, grid.Size, grid.Size);
        bmp.Save(path, ImageFormat.Png);
    }

    public static void MergeTwoFiles(string pathA, string pathB, string output)
    {
        using var a = LoadPng(pathA);
        using var b = LoadPng(pathB);
        if (a.Width != b.Width || a.Height != b.Height)
            throw new InvalidDataException("Size mismatch between the two textures.");
        var pa = PixelMath.BitmapToPixels(a);
        var pb = PixelMath.BitmapToPixels(b);
        var merged = new Color[pa.Length];
        for (int i = 0; i < pa.Length; i++) merged[i] = PixelMath.Merge(pa[i], pb[i]);
        using var bmp = PixelMath.PixelsToBitmap(merged, a.Width, a.Height);
        bmp.Save(output, ImageFormat.Png);
    }

    public static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public static void SaveSettings(AppSettings s)
    {
        try
        {
            EnsureFolders();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore */ }
    }
}