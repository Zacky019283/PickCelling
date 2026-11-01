namespace PickCelling;

public enum StripeDirection { Horizontal, Vertical, Diagonal }
public enum FalloffMode { Linear, Smooth, Soft }

public sealed class EffectSettings
{
    public bool StonePitsEnabled;
    public int StonePitChance = 15;
    public int StonePitDepth = 30;
    public bool StripesEnabled;
    public StripeDirection StripeDir = StripeDirection.Horizontal;
    public int StripeWidth = 4;
    public int StripeIntensity = 20;
    public bool PlankLinesEnabled;
    public int PlankLineSpacing = 4;
    public int PlankLineDarkness = 40;
}

public sealed class AutoShadeSettings
{
    public float LightAngle = 60f;
    public int LightIntensity = 55;
    public int ShadowIntensity = 55;
    public int BorderClarity = 60;
    public int Contrast = 100;
    public int AmbientFloor = 5;
    public int BlendAmount = 100;
    public FalloffMode Falloff = FalloffMode.Linear;
}

/// <summary>Multi-canvas grid: pixel data, per-canvas settings, bitmap cache.</summary>
public sealed class CanvasGrid : IDisposable
{
    public int Size { get; private set; }
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public Color[][] Canvases { get; private set; }
    public int Count => Canvases.Length;
    public int CompositeWidth => Cols * Size;
    public int CompositeHeight => Rows * Size;

    public bool[] CenteringEnabled { get; private set; }
    public PointF[] CenteringPoints { get; private set; }
    public int[] Roughness { get; private set; }
    public int[] Reflection { get; private set; }
    public int[] FalloffStrength { get; private set; }
    public int[] EdgeShadow { get; private set; }
    public int[] HueShift { get; private set; }
    public int[] SatBoost { get; private set; }

    private Bitmap[] _bitmaps;
    private bool[] _dirty;

    public CanvasGrid(int size, int cols, int rows)
    {
        Size = Math.Max(4, size);
        Cols = Math.Max(1, cols);
        Rows = Math.Max(1, rows);
        int total = Cols * Rows;
        Canvases = new Color[total][];
        for (int i = 0; i < total; i++) Canvases[i] = new Color[Size * Size];
        CenteringEnabled = new bool[total];
        CenteringPoints = new PointF[total];
        Roughness = new int[total];
        Reflection = new int[total];
        FalloffStrength = new int[total];
        EdgeShadow = new int[total];
        HueShift = new int[total];
        SatBoost = new int[total];
        for (int i = 0; i < total; i++)
        {
            CenteringEnabled[i] = true;
            CenteringPoints[i] = new PointF(0.5f, 0.5f);
            Roughness[i] = 15; Reflection[i] = 40; FalloffStrength[i] = 50;
            EdgeShadow[i] = 20; HueShift[i] = 20; SatBoost[i] = 20;
        }
        _bitmaps = new Bitmap[total];
        _dirty = new bool[total];
        for (int i = 0; i < total; i++) _dirty[i] = true;
    }

    public Color GetComposite(int cx, int cy)
    {
        if (cx < 0 || cx >= CompositeWidth || cy < 0 || cy >= CompositeHeight) return Color.Transparent;
        int idx = (cy / Size) * Cols + (cx / Size);
        return Canvases[idx][(cy % Size) * Size + (cx % Size)];
    }

    public void SetComposite(int cx, int cy, Color c)
    {
        if (cx < 0 || cx >= CompositeWidth || cy < 0 || cy >= CompositeHeight) return;
        int idx = (cy / Size) * Cols + (cx / Size);
        Canvases[idx][(cy % Size) * Size + (cx % Size)] = c;
        _dirty[idx] = true;
    }

    public int CompositeToCanvasIndex(int cx, int cy) => (cy / Size) * Cols + (cx / Size);

    public Bitmap GetBitmap(int index)
    {
        if (_bitmaps[index] == null || _dirty[index])
        {
            _bitmaps[index]?.Dispose();
            _bitmaps[index] = PixelMath.PixelsToBitmap(Canvases[index], Size, Size);
            _dirty[index] = false;
        }
        return _bitmaps[index];
    }

    public void InvalidateCell(int index)
    {
        if (index >= 0 && index < _dirty.Length)
            _dirty[index] = true;
    }

    public void MarkAllDirty()
    {
        for (int i = 0; i < _dirty.Length; i++)
            _dirty[i] = true;
    }

    public Color[][] Snapshot()
    {
        var snap = new Color[Count][];
        for (int i = 0; i < Count; i++) snap[i] = (Color[])Canvases[i].Clone();
        return snap;
    }

    public void Restore(Color[][] snapshot)
    {
        if (snapshot == null) return;
        int n = Math.Min(snapshot.Length, Count);
        for (int i = 0; i < n; i++)
        {
            Array.Copy(snapshot[i], Canvases[i], Math.Min(snapshot[i].Length, Canvases[i].Length));
            _dirty[i] = true;
        }
    }

    public void ClearAll()
    {
        for (int i = 0; i < Count; i++)
        {
            Array.Clear(Canvases[i], 0, Canvases[i].Length);
            _dirty[i] = true;
        }
    }

    // ================================================================
    // Transform operations
    // ================================================================

    public void FlipHorizontal()
    {
        for (int i = 0; i < Count; i++)
        {
            var src = Canvases[i];
            var dst = new Color[src.Length];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    dst[y * Size + x] = src[y * Size + (Size - 1 - x)];
            Canvases[i] = dst;
            _dirty[i] = true;
        }
    }

    public void FlipVertical()
    {
        for (int i = 0; i < Count; i++)
        {
            var src = Canvases[i];
            var dst = new Color[src.Length];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    dst[y * Size + x] = src[(Size - 1 - y) * Size + x];
            Canvases[i] = dst;
            _dirty[i] = true;
        }
    }

    public void Rotate90()
    {
        for (int i = 0; i < Count; i++)
        {
            var src = Canvases[i];
            var dst = new Color[src.Length];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    dst[x * Size + (Size - 1 - y)] = src[y * Size + x];
            Canvases[i] = dst;
            _dirty[i] = true;
        }
    }

    public void Dispose()
    {
        if (_bitmaps != null)
            foreach (var b in _bitmaps) b?.Dispose();
        _bitmaps = null;
    }
}

/// <summary>Snapshot-based undo/redo (max 50 levels).</summary>
public sealed class UndoStack
{
    private readonly List<Color[][]> _undo = new();
    private readonly List<Color[][]> _redo = new();
    private const int MaxDepth = 50;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(Color[][] snap)
    {
        _undo.Add(snap);
        if (_undo.Count > MaxDepth) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public Color[][] Undo(Color[][] current)
    {
        if (_undo.Count == 0) return null;
        var prev = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        return prev;
    }

    public Color[][] Redo(Color[][] current)
    {
        if (_redo.Count == 0) return null;
        var next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        return next;
    }

    public void Clear() { _undo.Clear(); _redo.Clear(); }
}