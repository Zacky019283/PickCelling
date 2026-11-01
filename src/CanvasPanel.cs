using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PickCelling;

public enum ToolMode { Paint, Erase, Fill, Line, Pick, Blur }
public enum SymmetryMode { None, Horizontal, Vertical, Both }
public enum DitherMode { None, Checkerboard50, Grid25 }

/// <summary>Interactive pixel canvas: rendering, painting, symmetry, centering.</summary>
public sealed class CanvasPanel : Control
{
    private CanvasGrid _grid;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CanvasGrid Grid
    {
        get => _grid;
        set
        {
            _grid = value;
            _scrollX = 0;
            _scrollY = 0;
            UpdateScrollbars();
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ToolMode Tool { get; set; } = ToolMode.Paint;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SymmetryMode Symmetry { get; set; } = SymmetryMode.None;

    private int _zoom = 16;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Zoom
    {
        get => _zoom;
        set
        {
            if (value < 1) value = 1;
            if (value > 512) value = 512;
            if (_zoom != value)
            {
                _zoom = value;
                UpdateScrollbars();
                Invalidate();
                ZoomChanged?.Invoke();
            }
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int BrushSize { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CurrentColor { get; set; } = Color.White;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CurrentAlpha { get; set; } = 255;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ColorRoughness { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int FillRoughness { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public EffectSettings Effects { get; set; } = new();
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowGhosts { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowGrid { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DitherMode DitherMode { get; set; } = DitherMode.None;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CenteringMode { get; set; }

    public event Action PaintStarted;
    public event Action PaintEnded;
    public event Action<Color> ColorPicked;
    public event Action<string> StatusChanged;
    public event Action ZoomChanged;

    private readonly HScrollBar _hScroll;
    private readonly VScrollBar _vScroll;
    private int _scrollX = 0;
    private int _scrollY = 0;
    private bool _updatingScrollbars = false;

    private bool _panning;
    private Point _panStart;
    private int _panStartX, _panStartY;

    private bool _painting;
    private Point _lastPaint = new(-1, -1);
    private Point _lastDrawnPoint = new(-1, -1);
    private readonly HashSet<Point> _strokePixels = new();

    private Point _lineStart;
    private Color[][] _lineSnapshot;
    private int _lineSeed;

    private Point _hover = new(-1, -1);

    private Bitmap[] _ghostCache;
    private bool _ghostDirty = true;

    public CanvasPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(28, 28, 28);
        SetStyle(ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _hScroll = new HScrollBar { Visible = false, Dock = DockStyle.Bottom };
        _vScroll = new VScrollBar { Visible = false, Dock = DockStyle.Right };

        _hScroll.Scroll += (s, e) => { _scrollX = _hScroll.Value; Invalidate(); };
        _vScroll.Scroll += (s, e) => { _scrollY = _vScroll.Value; Invalidate(); };

        Controls.Add(_hScroll);
        Controls.Add(_vScroll);
    }

    private void UpdateScrollbars()
    {
        if (Grid == null || _updatingScrollbars) return;

        _updatingScrollbars = true;
        try
        {
            int zoomW = Grid.CompositeWidth * Zoom;
            int zoomH = Grid.CompositeHeight * Zoom;

            _hScroll.Visible = false;
            _vScroll.Visible = false;
            PerformLayout();

            bool needV = zoomH > ClientSize.Height;
            bool needH = zoomW > ClientSize.Width;

            if (needH && !needV && zoomH > ClientSize.Height - SystemInformation.HorizontalScrollBarHeight)
                needV = true;
            if (needV && !needH && zoomW > ClientSize.Width - SystemInformation.VerticalScrollBarWidth)
                needH = true;

            _hScroll.Visible = needH;
            _vScroll.Visible = needV;

            PerformLayout();

            int availW = ClientSize.Width;
            int availH = ClientSize.Height;

            if (needH)
            {
                _hScroll.Minimum = 0;
                _hScroll.Maximum = zoomW - 1;
                _hScroll.LargeChange = Math.Max(1, availW);
                _hScroll.SmallChange = Zoom;
            }
            if (needV)
            {
                _vScroll.Minimum = 0;
                _vScroll.Maximum = zoomH - 1;
                _vScroll.LargeChange = Math.Max(1, availH);
                _vScroll.SmallChange = Zoom;
            }

            int maxScrollX = Math.Max(0, zoomW - availW);
            int maxScrollY = Math.Max(0, zoomH - availH);

            _scrollX = Math.Max(0, Math.Min(_scrollX, maxScrollX));
            _scrollY = Math.Max(0, Math.Min(_scrollY, maxScrollY));

            if (needH) _hScroll.Value = _scrollX;
            if (needV) _vScroll.Value = _scrollY;
        }
        finally
        {
            _updatingScrollbars = false;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollbars();
    }

    public void InvalidateGhosts() { _ghostDirty = true; }

    public void ResetView()
    {
        _scrollX = 0;
        _scrollY = 0;
        UpdateScrollbars();
        Invalidate();
    }

    public void FitToWindow()
    {
        if (Grid == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        int zw = Math.Max(1, (ClientSize.Width - 40) / Grid.CompositeWidth);
        int zh = Math.Max(1, (ClientSize.Height - 40) / Grid.CompositeHeight);
        _scrollX = 0;
        _scrollY = 0;
        Zoom = Math.Max(1, Math.Min(zw, zh));
        UpdateScrollbars();
        Invalidate();
    }

    private Point ContentOrigin
    {
        get
        {
            if (Grid == null) return Point.Empty;
            int zoomW = Grid.CompositeWidth * Zoom;
            int zoomH = Grid.CompositeHeight * Zoom;

            int x = zoomW <= ClientSize.Width
                ? (ClientSize.Width - zoomW) / 2
                : -_scrollX;

            int y = zoomH <= ClientSize.Height
                ? (ClientSize.Height - zoomH) / 2
                : -_scrollY;

            return new Point(x, y);
        }
    }

    private bool ScreenToComposite(int sx, int sy, out int cx, out int cy)
    {
        cx = cy = -1;
        if (Grid == null) return false;
        Point o = ContentOrigin;
        int lx = sx - o.X;
        int ly = sy - o.Y;
        if (lx < 0 || ly < 0 || lx >= Grid.CompositeWidth * Zoom || ly >= Grid.CompositeHeight * Zoom) return false;
        cx = lx / Zoom;
        cy = ly / Zoom;
        return true;
    }

    private IEnumerable<Point> Mirror(int cx, int cy)
    {
        yield return new Point(cx, cy);
        if (Grid == null) yield break;
        int s = Grid.Size;
        int scx = cx / s, lx = cx % s;
        int scy = cy / s, ly = cy % s;
        if (Symmetry == SymmetryMode.Horizontal || Symmetry == SymmetryMode.Both)
            yield return new Point(scx * s + (s - 1 - lx), cy);
        if (Symmetry == SymmetryMode.Vertical || Symmetry == SymmetryMode.Both)
            yield return new Point(cx, scy * s + (s - 1 - ly));
        if (Symmetry == SymmetryMode.Both)
            yield return new Point(scx * s + (s - 1 - lx), scy * s + (s - 1 - ly));
    }

    private Color ApplyEffects(Color c, int cx, int cy, int rough, int seed)
    {
        if (Effects == null) return c;
        return EffectsHelper.Apply(c, cx, cy, rough, Effects, seed);
    }

    private void PaintPixel(int cx, int cy, Color c, int rough, int seed)
    {
        if (cx < 0 || cy < 0 || cx >= Grid.CompositeWidth || cy >= Grid.CompositeHeight) return;

        if (DitherMode == DitherMode.Checkerboard50 && (cx + cy) % 2 != 0) return;
        if (DitherMode == DitherMode.Grid25 && (cx % 2 != 0 || cy % 2 != 0)) return;

        var key = new Point(cx, cy);
        if (_strokePixels.Contains(key)) return;
        _strokePixels.Add(key);
        Color cc = ApplyEffects(c, cx, cy, rough, seed);
        Grid.SetComposite(cx, cy, cc);
    }

    private void PaintBrushAt(int cx, int cy, Color c, int rough, int seed = -1)
    {
        int off = (BrushSize - 1) / 2;
        for (int by = 0; by < BrushSize; by++)
            for (int bx = 0; bx < BrushSize; bx++)
            {
                int px = cx - off + bx;
                int py = cy - off + by;
                foreach (var m in Mirror(px, py))
                    PaintPixel(m.X, m.Y, c, rough, seed);
            }
    }

    private void PaintLineBresenham(int x0, int y0, int x1, int y1, Color c, int rough, int seed)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            PaintBrushAt(x0, y0, c, rough, seed);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    private void FloodFill(int cx, int cy, Color c, int rough)
    {
        if (cx < 0 || cy < 0 || cx >= Grid.CompositeWidth || cy >= Grid.CompositeHeight) return;
        int s = Grid.Size;
        int idx = (cy / s) * Grid.Cols + (cx / s);
        int lx = cx % s, ly = cy % s;
        var sub = Grid.Canvases[idx];
        Color target = sub[ly * s + lx];
        if (target.ToArgb() == c.ToArgb() && rough == 0) return;

        var stack = new Stack<Point>();
        var seen = new bool[s * s];
        stack.Push(new Point(lx, ly));
        int scx = cx - lx, scy = cy - ly;

        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (p.X < 0 || p.X >= s || p.Y < 0 || p.Y >= s) continue;
            if (seen[p.Y * s + p.X]) continue;
            if (sub[p.Y * s + p.X].ToArgb() != target.ToArgb()) continue;
            seen[p.Y * s + p.X] = true;
            sub[p.Y * s + p.X] = EffectsHelper.Apply(c, scx + p.X, scy + p.Y, rough, Effects);
            stack.Push(new Point(p.X + 1, p.Y));
            stack.Push(new Point(p.X - 1, p.Y));
            stack.Push(new Point(p.X, p.Y + 1));
            stack.Push(new Point(p.X, p.Y - 1));
        }
        Grid.InvalidateCell(idx);
    }

    private void PickColor(int cx, int cy)
    {
        Color c = Grid.GetComposite(cx, cy);
        if (c.A < 3) { StatusChanged?.Invoke("Pick: transparent pixel ignored."); return; }
        Color picked = Color.FromArgb(255, c.R, c.G, c.B);
        CurrentColor = picked;
        ColorPicked?.Invoke(picked);
        StatusChanged?.Invoke($"Picked RGB({c.R},{c.G},{c.B})");
    }

    private void ApplyBlur(int cx, int cy)
    {
        int radius = Math.Max(0, (BrushSize - 1) / 2);
        for (int by = -radius; by <= radius; by++)
        {
            for (int bx = -radius; bx <= radius; bx++)
            {
                int px = cx + bx;
                int py = cy + by;
                if (px < 0 || px >= Grid.CompositeWidth || py < 0 || py >= Grid.CompositeHeight) continue;

                Color currentC = Grid.GetComposite(px, py);
                if (currentC.A == 0) continue;

                float r = 0, g = 0, b = 0, a = 0;
                int count = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = px + dx;
                        int ny = py + dy;
                        if (nx < 0 || nx >= Grid.CompositeWidth || ny < 0 || ny >= Grid.CompositeHeight) continue;
                        Color c = Grid.GetComposite(nx, ny);

                        if (c.A > 0)
                        {
                            r += c.R * c.A;
                            g += c.G * c.A;
                            b += c.B * c.A;
                            a += c.A;
                            count++;
                        }
                    }
                }

                if (count > 0)
                {
                    Color blurred = Color.FromArgb(
                        (int)(a / count),
                        (int)(r / a),
                        (int)(g / a),
                        (int)(b / a));
                    Grid.SetComposite(px, py, blurred);
                }
            }
        }
    }

    private void BlurLineBresenham(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        while (true)
        {
            ApplyBlur(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Grid == null) return;
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.SmoothingMode = SmoothingMode.None;

        Point origin = ContentOrigin;
        int cw = Grid.CompositeWidth * Zoom;
        int ch = Grid.CompositeHeight * Zoom;

        DrawChecker(g, new Rectangle(origin.X, origin.Y, cw, ch));

        if (ShowGhosts && (_ghostDirty || _ghostCache == null || _ghostCache.Length != Grid.Count))
            RebuildGhostCache();

        for (int cy = 0; cy < Grid.Rows; cy++)
            for (int cx = 0; cx < Grid.Cols; cx++)
            {
                int idx = cy * Grid.Cols + cx;
                var dest = new Rectangle(
                    origin.X + cx * Grid.Size * Zoom,
                    origin.Y + cy * Grid.Size * Zoom,
                    Grid.Size * Zoom,
                    Grid.Size * Zoom);
                g.DrawImage(Grid.GetBitmap(idx), dest);
                if (ShowGhosts && _ghostCache != null)
                    g.DrawImage(_ghostCache[idx], dest);
            }

        if (ShowGrid && Zoom >= 4)
        {
            using var pen = new Pen(Color.FromArgb(60, 0, 0, 0));
            for (int x = 1; x < Grid.CompositeWidth; x++)
                if (x % Grid.Size != 0)
                    g.DrawLine(pen, origin.X + x * Zoom, origin.Y, origin.X + x * Zoom, origin.Y + ch);
            for (int y = 1; y < Grid.CompositeHeight; y++)
                if (y % Grid.Size != 0)
                    g.DrawLine(pen, origin.X, origin.Y + y * Zoom, origin.X + cw, origin.Y + y * Zoom);
        }

        using (var sep = new Pen(Color.FromArgb(240, 15, 15, 15), 2f))
        {
            for (int cx = 1; cx < Grid.Cols; cx++)
            {
                int x = origin.X + cx * Grid.Size * Zoom;
                g.DrawLine(sep, x, origin.Y, x, origin.Y + ch);
            }
            for (int cy = 1; cy < Grid.Rows; cy++)
            {
                int y = origin.Y + cy * Grid.Size * Zoom;
                g.DrawLine(sep, origin.X, y, origin.X + cw, y);
            }
        }

        using (var border = new Pen(Color.Black, 1f))
            g.DrawRectangle(border, origin.X - 1, origin.Y - 1, cw + 1, ch + 1);

        if (CenteringMode) DrawCenteringMarkers(g, origin);
        else DrawBrushPreview(g, origin);
    }

    private void DrawChecker(Graphics g, Rectangle r)
    {
        const int cell = 8;
        using var b1 = new SolidBrush(Color.FromArgb(56, 56, 56));
        using var b2 = new SolidBrush(Color.FromArgb(76, 76, 76));
        for (int y = 0; y < r.Height; y += cell)
            for (int x = 0; x < r.Width; x += cell)
            {
                var brush = ((x / cell) + (y / cell)) % 2 == 0 ? b1 : b2;
                int w = Math.Min(cell, r.Width - x);
                int h = Math.Min(cell, r.Height - y);
                g.FillRectangle(brush, r.X + x, r.Y + y, w, h);
            }
    }

    private void RebuildGhostCache()
    {
        if (_ghostCache != null) foreach (var b in _ghostCache) b?.Dispose();
        _ghostCache = new Bitmap[Grid.Count];
        int s = Grid.Size;
        for (int k = 0; k < Grid.Count; k++)
        {
            var pixels = new Color[s * s];
            for (int i = 0; i < pixels.Length; i++)
            {
                if (Grid.Canvases[k][i].A >= 3) { pixels[i] = Color.Transparent; continue; }
                Color best = Color.Transparent;
                for (int j = 0; j < Grid.Count; j++)
                {
                    if (j == k) continue;
                    var p = Grid.Canvases[j][i];
                    if (p.A > best.A) best = p;
                }
                pixels[i] = best.A >= 3
                    ? Color.FromArgb((int)(best.A * 0.3f), best.R, best.G, best.B)
                    : Color.Transparent;
            }
            _ghostCache[k] = PixelMath.PixelsToBitmap(pixels, s, s);
        }
        _ghostDirty = false;
    }

    private void DrawBrushPreview(Graphics g, Point origin)
    {
        if (_hover.X < 0) return;
        int off = (BrushSize - 1) / 2;
        var rect = new Rectangle(
            origin.X + (_hover.X - off) * Zoom,
            origin.Y + (_hover.Y - off) * Zoom,
            BrushSize * Zoom,
            BrushSize * Zoom);
        using var fill = new SolidBrush(Color.FromArgb(40, 255, 255, 255));
        using var outline = new Pen(Color.FromArgb(180, 255, 255, 255), 1f);
        g.FillRectangle(fill, rect);
        g.DrawRectangle(outline, rect);
    }

    private void DrawCenteringMarkers(Graphics g, Point origin)
    {
        using var green = new SolidBrush(Color.FromArgb(120, 80, 255, 100));
        using var outline = new Pen(Color.FromArgb(230, 30, 220, 60), 2f);
        for (int cy = 0; cy < Grid.Rows; cy++)
            for (int cx = 0; cx < Grid.Cols; cx++)
            {
                int idx = cy * Grid.Cols + cx;
                if (!Grid.CenteringEnabled[idx]) continue;
                var pt = Grid.CenteringPoints[idx];
                int px = cx * Grid.Size + (int)(pt.X * Grid.Size);
                int py = cy * Grid.Size + (int)(pt.Y * Grid.Size);
                var r = new Rectangle(origin.X + px * Zoom - 1, origin.Y + py * Zoom - 1, Zoom + 2, Zoom + 2);
                g.FillRectangle(green, r);
                g.DrawRectangle(outline, r);
            }
        if (_hover.X >= 0)
        {
            var r = new Rectangle(origin.X + _hover.X * Zoom, origin.Y + _hover.Y * Zoom, Zoom, Zoom);
            using var yellow = new SolidBrush(Color.FromArgb(180, 255, 240, 90));
            g.FillRectangle(yellow, r);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button == MouseButtons.Middle)
        {
            _panning = true;
            _panStart = e.Location;
            _panStartX = _scrollX;
            _panStartY = _scrollY;
            return;
        }
        if (Grid == null) return;
        if (!ScreenToComposite(e.X, e.Y, out int cx, out int cy)) return;

        if (CenteringMode)
        {
            int idx = (cy / Grid.Size) * Grid.Cols + (cx / Grid.Size);
            Grid.CenteringPoints[idx] = new PointF(
                ((cx % Grid.Size) + 0.5f) / Grid.Size,
                ((cy % Grid.Size) + 0.5f) / Grid.Size);
            CenteringMode = false;
            Invalidate();
            return;
        }

        if (e.Button != MouseButtons.Left) return;
        if (Tool == ToolMode.Pick || (ModifierKeys & Keys.Alt) != 0) { PickColor(cx, cy); return; }

        PaintStarted?.Invoke();
        _strokePixels.Clear();

        if (Tool == ToolMode.Fill) { FloodFill(cx, cy, CurrentColor, FillRoughness); InvalidateGhosts(); Invalidate(); }
        else if (Tool == ToolMode.Line)
        {
            _lineStart = new Point(cx, cy);
            _lineSnapshot = Grid.Snapshot();
            _lineSeed = Random.Shared.Next(1, int.MaxValue);
        }
        else if (Tool == ToolMode.Blur)
        {
            ApplyBlur(cx, cy);
            _painting = true;
            _lastPaint = new Point(cx, cy);
            _lastDrawnPoint = new Point(cx, cy);
            InvalidateGhosts(); Invalidate();
        }
        else
        {
            Color c = Tool == ToolMode.Erase ? Color.Transparent : Color.FromArgb(CurrentAlpha, CurrentColor.R, CurrentColor.G, CurrentColor.B);
            int r = Tool == ToolMode.Erase ? 0 : ColorRoughness;

            if ((ModifierKeys & Keys.Shift) != 0 && _lastDrawnPoint.X >= 0 && _lastDrawnPoint.Y >= 0)
                PaintLineBresenham(_lastDrawnPoint.X, _lastDrawnPoint.Y, cx, cy, c, r, -1);
            else
                PaintBrushAt(cx, cy, c, r);

            _painting = true;
            _lastPaint = new Point(cx, cy);
            _lastDrawnPoint = new Point(cx, cy);
            InvalidateGhosts(); Invalidate();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panning)
        {
            int dx = _panStart.X - e.X;
            int dy = _panStart.Y - e.Y;

            if (_hScroll.Visible)
            {
                int maxX = Math.Max(0, _hScroll.Maximum - _hScroll.LargeChange + 1);
                int newX = _panStartX + dx;
                _scrollX = Math.Max(0, Math.Min(maxX, newX));
                _hScroll.Value = _scrollX;
            }
            if (_vScroll.Visible)
            {
                int maxY = Math.Max(0, _vScroll.Maximum - _vScroll.LargeChange + 1);
                int newY = _panStartY + dy;
                _scrollY = Math.Max(0, Math.Min(maxY, newY));
                _vScroll.Value = _scrollY;
            }
            Invalidate();
            return;
        }
        if (Grid == null) return;

        if (ScreenToComposite(e.X, e.Y, out int cx, out int cy))
        {
            _hover = new Point(cx, cy);
            StatusChanged?.Invoke($"({cx}, {cy})");
        }
        else _hover = new Point(-1, -1);

        if (_painting)
        {
            int lx = _lastPaint.X, ly = _lastPaint.Y;
            if (Tool == ToolMode.Blur)
            {
                BlurLineBresenham(lx, ly, cx, cy);
            }
            else
            {
                Color c = Tool == ToolMode.Erase ? Color.Transparent : Color.FromArgb(CurrentAlpha, CurrentColor.R, CurrentColor.G, CurrentColor.B);
                int r = Tool == ToolMode.Erase ? 0 : ColorRoughness;
                PaintLineBresenham(lx, ly, cx, cy, c, r, -1);
            }
            _lastPaint = new Point(cx, cy);
            _lastDrawnPoint = new Point(cx, cy);
            InvalidateGhosts();
        }
        else if (Tool == ToolMode.Line && _lineSnapshot != null)
        {
            Grid.Restore(_lineSnapshot);
            _strokePixels.Clear();

            int endX = cx;
            int endY = cy;

            if ((ModifierKeys & Keys.Shift) != 0)
            {
                int dx = cx - _lineStart.X;
                int dy = cy - _lineStart.Y;

                if (Math.Abs(dx) > Math.Abs(dy) * 2)
                    endY = _lineStart.Y;
                else if (Math.Abs(dy) > Math.Abs(dx) * 2)
                    endX = _lineStart.X;
                else
                {
                    int dist = Math.Min(Math.Abs(dx), Math.Abs(dy));
                    endX = _lineStart.X + Math.Sign(dx) * dist;
                    endY = _lineStart.Y + Math.Sign(dy) * dist;
                }
            }

            PaintLineBresenham(_lineStart.X, _lineStart.Y, endX, endY, CurrentColor, ColorRoughness, _lineSeed);
            InvalidateGhosts();
        }
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Middle) { _panning = false; return; }
        if (_painting)
        {
            _painting = false;
            _lastPaint = new Point(-1, -1);
            _strokePixels.Clear();
            PaintEnded?.Invoke();
        }
        if (_lineSnapshot != null)
        {
            _lineSnapshot = null;
            _strokePixels.Clear();
            PaintEnded?.Invoke();
        }
        InvalidateGhosts();
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = new Point(-1, -1);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) != 0)
        {
            int step = Math.Max(1, Zoom / 4);
            if (e.Delta > 0) Zoom += step;
            else Zoom = Math.Max(1, Zoom - step);
        }
        else if ((ModifierKeys & Keys.Shift) != 0)
        {
            if (_hScroll.Visible)
            {
                int delta = e.Delta > 0 ? -Zoom : Zoom;
                int maxX = Math.Max(0, _hScroll.Maximum - _hScroll.LargeChange + 1);
                _scrollX = Math.Max(0, Math.Min(maxX, _scrollX + delta));
                _hScroll.Value = _scrollX;
                Invalidate();
            }
        }
        else
        {
            if (_vScroll.Visible)
            {
                int delta = e.Delta > 0 ? -Zoom : Zoom;
                int maxY = Math.Max(0, _vScroll.Maximum - _vScroll.LargeChange + 1);
                _scrollY = Math.Max(0, Math.Min(maxY, _scrollY + delta));
                _vScroll.Value = _scrollY;
                Invalidate();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ghostCache != null)
            foreach (var b in _ghostCache) b?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Adapter so CanvasPanel can call Effects.Apply without circular references.</summary>
internal static class EffectsHelper
{
    public static Color Apply(Color c, int x, int y, int rough, EffectSettings s, int seed = -1)
        => Effects.Apply(c, x, y, rough, s, seed);
}