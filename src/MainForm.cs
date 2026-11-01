namespace PickCelling;

/// <summary>Main window: toolbar, file list, canvas, tool settings sidebar.</summary>
public sealed class MainForm : Form
{
    private CanvasGrid _grid = new(16, 1, 1);
    private readonly CanvasPanel _canvas = new();
    private readonly UndoStack _undo = new();
    private readonly EffectSettings _effects = new();
    private readonly AutoShadeSettings _shade = new();

    private readonly List<string> _files = new();
    private readonly List<string> _recent = new();
    private readonly List<Color> _colorHistory = new();
    private bool _multiMode;
    private int _nextSlot;

    private string _currentProjectPath = "";
    private string _fileName = "new_texture";

    private ListBox _fileList;
    private ComboBox _cmbSymmetry, _cmbDither;
    private NumericUpDown _numSize, _numCols, _numRows, _numZoom, _numBrush, _numAlpha;
    private Panel _swatch;
    private NumericUpDown _numColorRough, _numFillRough;
    private CheckBox _chkGhosts, _chkGrid, _chkMulti;
    private Label _lblSlot;
    private ToolStripStatusLabel _lblStatusPos, _lblStatusTool, _lblStatusZoom;
    private FlowLayoutPanel _colorHistoryBar;
    private FlowLayoutPanel _perCanvasHost;
    private NumericUpDown _numLightAngle, _numLightInt, _numShadowInt, _numContrast,
                          _numAmbient, _numBorder, _numBlend;
    private ComboBox _cmbFalloff;

    public MainForm()
    {
        Text = "Pick-Celling";
        var settings = FileIO.LoadSettings();

        Height = Math.Max(900, settings.WindowHeight);
        int estimatedCenterHeight = Height - 110;
        Width = 260 + 520 + estimatedCenterHeight;

        MinimumSize = new Size(1100, 700);
        if (settings.RecentFiles != null) _recent.AddRange(settings.RecentFiles);
        KeyPreview = true;
        BackColor = Color.FromArgb(40, 40, 40);
        ForeColor = Color.White;

        FileIO.EnsureFolders();
        BuildUi();
        WireCanvas();
        LoadFileList();
        RefreshFromGrid();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var menu = new MenuStrip { Dock = DockStyle.Fill };
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("New", null, (_, _) => OnNew());
        file.DropDownItems.Add("Open Project...", null, (_, _) => OnOpenProject());
        file.DropDownItems.Add("Save Project", null, (_, _) => OnSaveProject(false));
        file.DropDownItems.Add("Save Project As...", null, (_, _) => OnSaveProject(true));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Import PNG...", null, (_, _) => OnImportPng());
        file.DropDownItems.Add("Export Composite PNG...", null, (_, _) => OnExportComposite());
        file.DropDownItems.Add("Open Textures Folder", null, (_, _) => OnOpenTextures());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => Close());
        menu.Items.Add(file);

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.Add("Undo  (Ctrl+Z)", null, (_, _) => DoUndo());
        edit.DropDownItems.Add("Redo  (Ctrl+Y)", null, (_, _) => DoRedo());
        edit.DropDownItems.Add(new ToolStripSeparator());

        var transform = new ToolStripMenuItem("Transform");
        transform.DropDownItems.Add("Flip Horizontal  (Ctrl+H)", null, (_, _) => TransformGrid(g => g.FlipHorizontal()));
        transform.DropDownItems.Add("Flip Vertical  (Ctrl+Shift+H)", null, (_, _) => TransformGrid(g => g.FlipVertical()));
        transform.DropDownItems.Add("Rotate 90°  (Ctrl+R)", null, (_, _) => TransformGrid(g => g.Rotate90()));
        edit.DropDownItems.Add(transform);

        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add("Clear All", null, (_, _) => ClearAll());
        menu.Items.Add(edit);

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.Add("Fit to Window", null, (_, _) => _canvas.FitToWindow());
        view.DropDownItems.Add("Reset Pan", null, (_, _) => _canvas.ResetView());
        menu.Items.Add(view);

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add("Paint (B)", null, (_, _) => SetTool(ToolMode.Paint));
        tools.DropDownItems.Add("Erase (E)", null, (_, _) => SetTool(ToolMode.Erase));
        tools.DropDownItems.Add("Fill (G)", null, (_, _) => SetTool(ToolMode.Fill));
        tools.DropDownItems.Add("Line (L)", null, (_, _) => SetTool(ToolMode.Line));
        tools.DropDownItems.Add("Pick (I)", null, (_, _) => SetTool(ToolMode.Pick));
        tools.DropDownItems.Add("Blur (R)", null, (_, _) => SetTool(ToolMode.Blur));
        menu.Items.Add(tools);

        MainMenuStrip = menu;
        root.Controls.Add(menu, 0, 0);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.FromArgb(40, 40, 40)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));
        root.Controls.Add(table, 0, 1);

        Controls.Add(root);

        BuildLeftPanel(table);
        BuildCenterPanel(table);
        BuildRightPanel(table);
        BuildStatusBar();
    }

    private void BuildLeftPanel(TableLayoutPanel table)
    {
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        var leftTop = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 140,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        _chkMulti = new CheckBox { Text = "Multi-load mode", Width = 220, ForeColor = Color.White, AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
        _chkMulti.CheckedChanged += (_, _) => { _multiMode = _chkMulti.Checked; _nextSlot = 0; UpdateSlotLabel(); };

        _lblSlot = new Label { Text = "", Width = 220, ForeColor = Color.LightGray, AutoSize = true, Height = 24, Margin = new Padding(0, 0, 0, 8) };

        var btnRefresh = MakeButton("Refresh", 100);
        btnRefresh.Click += (_, _) => LoadFileList();
        var btnFolder = MakeButton("Folder", 100);
        btnFolder.Click += (_, _) => OnOpenTextures();

        var row = new FlowLayoutPanel { Width = 240, Height = 44, Margin = new Padding(0) };
        row.Controls.Add(btnRefresh);
        row.Controls.Add(btnFolder);

        leftTop.Controls.Add(_chkMulti);
        leftTop.Controls.Add(_lblSlot);
        leftTop.Controls.Add(row);

        _fileList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            BackColor = Color.FromArgb(40, 40, 40),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _fileList.DoubleClick += (_, _) => LoadSelectedFile();

        left.Controls.Add(_fileList);
        left.Controls.Add(leftTop);
        table.Controls.Add(left, 0, 0);
    }

    private void BuildCenterPanel(TableLayoutPanel table)
    {
        var center = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(52, 52, 52),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(8, 0, 0, 0)
        };

        topBar.Controls.Add(InlineLabel("Size"));
        _numSize = new WheelThroughNumericUpDown
        {
            Width = 70,
            Minimum = 4,
            Maximum = 512,
            Value = 16,
            ForeColor = Color.Black,
            BackColor = Color.White,
            Margin = new Padding(0, 16, 16, 0)
        };
        _numSize.ValueChanged += (_, _) => OnChangeSize((int)_numSize.Value);
        topBar.Controls.Add(_numSize);

        topBar.Controls.Add(InlineLabel("Grid"));
        _numCols = new WheelThroughNumericUpDown
        {
            Width = 50,
            Minimum = 1,
            Maximum = 16,
            Value = 1,
            ForeColor = Color.Black,
            BackColor = Color.White,
            Margin = new Padding(0, 16, 4, 0)
        };
        _numCols.ValueChanged += (_, _) => OnChangeGrid();
        topBar.Controls.Add(_numCols);

        topBar.Controls.Add(InlineLabel("x"));
        _numRows = new WheelThroughNumericUpDown
        {
            Width = 50,
            Minimum = 1,
            Maximum = 16,
            Value = 1,
            ForeColor = Color.Black,
            BackColor = Color.White,
            Margin = new Padding(0, 16, 16, 0)
        };
        _numRows.ValueChanged += (_, _) => OnChangeGrid();
        topBar.Controls.Add(_numRows);

        topBar.Controls.Add(InlineLabel("Zoom"));
        _numZoom = new WheelThroughNumericUpDown
        {
            Width = 70,
            Minimum = 1,
            Maximum = 512,
            Value = 16,
            ForeColor = Color.Black,
            BackColor = Color.White,
            Margin = new Padding(0, 16, 0, 0)
        };
        _numZoom.ValueChanged += (_, _) =>
        {
            if (_canvas.Zoom != (int)_numZoom.Value)
            {
                _canvas.Zoom = (int)_numZoom.Value;
            }
            UpdateStatus();
        };
        topBar.Controls.Add(_numZoom);

        _canvas.Dock = DockStyle.Fill;
        _canvas.Grid = _grid;

        center.Controls.Add(topBar, 0, 0);
        center.Controls.Add(_canvas, 0, 1);
        table.Controls.Add(center, 1, 0);
    }

    private void BuildRightPanel(TableLayoutPanel table)
    {
        var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(8) };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            ForeColor = Color.White
        };
        BuildSidebar(flow);
        right.Controls.Add(flow);
        table.Controls.Add(right, 2, 0);
    }

    private void BuildStatusBar()
    {
        var status = new StatusStrip { BackColor = Color.FromArgb(40, 40, 40), ForeColor = Color.White };
        _lblStatusPos = new ToolStripStatusLabel("(0, 0)") { Width = 150 };
        _lblStatusTool = new ToolStripStatusLabel("Tool: Paint") { Width = 200 };
        _lblStatusZoom = new ToolStripStatusLabel("Zoom: 16") { Width = 150 };
        status.Items.AddRange(new ToolStripItem[] { _lblStatusPos, _lblStatusTool, _lblStatusZoom });
        Controls.Add(status);
    }

    private void BuildSidebar(FlowLayoutPanel flow)
    {
        // TOOL
        flow.Controls.Add(Header("Tool"));
        var toolBar = new FlowLayoutPanel { Width = 500, Height = 44, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 12) };
        foreach (var name in new[] { "Paint", "Erase", "Fill", "Line", "Pick", "Blur" })
        {
            var btn = MakeButton(name, 76);
            btn.Height = 36;
            btn.Margin = new Padding(0, 0, 4, 0);
            var mode = (ToolMode)Enum.Parse(typeof(ToolMode), name);
            btn.Click += (_, _) => SetTool(mode);
            toolBar.Controls.Add(btn);
        }
        flow.Controls.Add(toolBar);

        // TRANSFORM (quick access)
        flow.Controls.Add(Header("Transform"));
        var transformBar = new FlowLayoutPanel { Width = 500, Height = 44, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 4, 0, 12) };
        var btnFlipH = MakeButton("Flip H", 118);
        btnFlipH.Height = 36;
        btnFlipH.Click += (_, _) => TransformGrid(g => g.FlipHorizontal());
        var btnFlipV = MakeButton("Flip V", 118);
        btnFlipV.Height = 36;
        btnFlipV.Click += (_, _) => TransformGrid(g => g.FlipVertical());
        var btnRotate = MakeButton("Rotate 90°", 118);
        btnRotate.Height = 36;
        btnRotate.Click += (_, _) => TransformGrid(g => g.Rotate90());
        transformBar.Controls.AddRange(new Control[] { btnFlipH, btnFlipV, btnRotate });
        flow.Controls.Add(transformBar);

        // SYMMETRY
        flow.Controls.Add(Header("Symmetry"));
        _cmbSymmetry = MakeCombo(500);
        _cmbSymmetry.Items.AddRange(new object[] { "None", "Horizontal", "Vertical", "Both" });
        _cmbSymmetry.SelectedIndex = 0;
        _cmbSymmetry.SelectedIndexChanged += (_, _) => _canvas.Symmetry = (SymmetryMode)_cmbSymmetry.SelectedIndex;
        flow.Controls.Add(_cmbSymmetry);

        // BRUSH
        flow.Controls.Add(Header("Brush"));
        _numBrush = MakeNud(100, 1, 64, 1);
        _numBrush.ValueChanged += (_, _) => _canvas.BrushSize = (int)_numBrush.Value;
        flow.Controls.Add(Row("Size", _numBrush));

        // COLOR & ALPHA
        flow.Controls.Add(Header("Color & Alpha"));
        var colorRow = new FlowLayoutPanel { Width = 500, Height = 44, Margin = new Padding(0, 6, 0, 6) };
        _swatch = new Panel { Width = 60, Height = 36, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 8, 0) };
        _swatch.Click += (_, _) => PickColorDialog();

        var btnPick = MakeButton("Choose...", 90);
        btnPick.Height = 36;
        btnPick.Margin = new Padding(0, 4, 16, 0);
        btnPick.Click += (_, _) => PickColorDialog();

        var lblAlpha = new Label { Text = "Alpha", ForeColor = Color.White, Width = 50, Height = 36, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 4, 4, 0) };
        _numAlpha = new WheelThroughNumericUpDown { Width = 80, Minimum = 0, Maximum = 255, Value = 255, ForeColor = Color.Black, BackColor = Color.White, Margin = new Padding(0, 8, 0, 0) };
        _numAlpha.ValueChanged += (_, _) => { _canvas.CurrentAlpha = (int)_numAlpha.Value; };

        colorRow.Controls.AddRange(new Control[] { _swatch, btnPick, lblAlpha, _numAlpha });
        flow.Controls.Add(colorRow);

        _colorHistoryBar = new FlowLayoutPanel { Width = 500, Height = 44, Margin = new Padding(0, 6, 0, 6) };
        flow.Controls.Add(_colorHistoryBar);

        var palette = new FlowLayoutPanel { Width = 500, Height = 100, Margin = new Padding(0, 8, 0, 8) };
        var paletteColors = new[]
        {
            Color.White, Color.Black, Color.Gray, Color.DimGray,
            Color.Red, Color.LimeGreen, Color.DodgerBlue, Color.Yellow,
            Color.Orange, Color.MediumPurple, Color.SaddleBrown, Color.PeachPuff
        };
        foreach (var c in paletteColors)
        {
            var b = new Button { Width = 60, Height = 40, BackColor = c, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 4, 4) };
            b.FlatAppearance.BorderColor = Color.Black;
            b.Click += (_, _) => SetCurrentColor(c);
            palette.Controls.Add(b);
        }
        flow.Controls.Add(palette);

        _numColorRough = MakeNud(100, 0, 100000, 0);
        _numColorRough.ValueChanged += (_, _) => _canvas.ColorRoughness = (int)_numColorRough.Value;
        flow.Controls.Add(Row("Color Roughness", _numColorRough));

        _numFillRough = MakeNud(100, 0, 100000, 0);
        _numFillRough.ValueChanged += (_, _) => _canvas.FillRoughness = (int)_numFillRough.Value;
        flow.Controls.Add(Row("Fill Roughness", _numFillRough));

        // DISPLAY
        flow.Controls.Add(Header("Display"));
        _chkGhosts = new CheckBox { Text = "Show ghost overlay", Checked = true, ForeColor = Color.White, AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
        _chkGhosts.CheckedChanged += (_, _) => { _canvas.ShowGhosts = _chkGhosts.Checked; _canvas.Invalidate(); };
        flow.Controls.Add(_chkGhosts);

        _chkGrid = new CheckBox { Text = "Show grid lines", Checked = true, ForeColor = Color.White, AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
        _chkGrid.CheckedChanged += (_, _) => { _canvas.ShowGrid = _chkGrid.Checked; _canvas.Invalidate(); };
        flow.Controls.Add(_chkGrid);

        _cmbDither = new WheelThroughComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, ForeColor = Color.Black, BackColor = Color.White, Margin = new Padding(0, 6, 0, 0) };
        _cmbDither.Items.AddRange(Enum.GetNames(typeof(DitherMode)));
        _cmbDither.SelectedIndex = 0;
        _cmbDither.SelectedIndexChanged += (_, _) => _canvas.DitherMode = (DitherMode)_cmbDither.SelectedIndex;
        flow.Controls.Add(Row("Dithering Mode", _cmbDither));

        // SMART EFFECTS
        flow.Controls.Add(Header("Smart Effects"));

        var chkStone = new CheckBox { Text = "Stone pits", ForeColor = Color.White, AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
        var numChance = MakeNud(100, 0, 100000, 15);
        var numDepth = MakeNud(100, 0, 100000, 30);
        chkStone.CheckedChanged += (_, _) => _effects.StonePitsEnabled = chkStone.Checked;
        numChance.ValueChanged += (_, _) => _effects.StonePitChance = (int)numChance.Value;
        numDepth.ValueChanged += (_, _) => _effects.StonePitDepth = (int)numDepth.Value;
        flow.Controls.Add(chkStone);
        flow.Controls.Add(Row("Stone Pit Chance", numChance));
        flow.Controls.Add(Row("Stone Pit Depth", numDepth));

        var chkStripes = new CheckBox { Text = "Striped surface", ForeColor = Color.White, AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        var cmbDir = new WheelThroughComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, ForeColor = Color.Black, BackColor = Color.White, Margin = new Padding(0, 6, 0, 0) };
        cmbDir.Items.AddRange(Enum.GetNames(typeof(StripeDirection)));
        cmbDir.SelectedIndex = 0;
        var numStripeW = MakeNud(100, 1, 100000, 4);
        var numStripeI = MakeNud(100, 0, 100000, 20);
        chkStripes.CheckedChanged += (_, _) => _effects.StripesEnabled = chkStripes.Checked;
        cmbDir.SelectedIndexChanged += (_, _) => _effects.StripeDir = (StripeDirection)cmbDir.SelectedIndex;
        numStripeW.ValueChanged += (_, _) => _effects.StripeWidth = (int)numStripeW.Value;
        numStripeI.ValueChanged += (_, _) => _effects.StripeIntensity = (int)numStripeI.Value;
        flow.Controls.Add(chkStripes);
        flow.Controls.Add(Row("Direction", cmbDir));
        flow.Controls.Add(Row("Stripe Width", numStripeW));
        flow.Controls.Add(Row("Stripe Intensity", numStripeI));

        var chkPlank = new CheckBox { Text = "Plank lines", ForeColor = Color.White, AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        var numPlankSpace = MakeNud(100, 1, 100000, 4);
        var numPlankDark = MakeNud(100, 0, 100000, 40);
        chkPlank.CheckedChanged += (_, _) => _effects.PlankLinesEnabled = chkPlank.Checked;
        numPlankSpace.ValueChanged += (_, _) => _effects.PlankLineSpacing = (int)numPlankSpace.Value;
        numPlankDark.ValueChanged += (_, _) => _effects.PlankLineDarkness = (int)numPlankDark.Value;
        flow.Controls.Add(chkPlank);
        flow.Controls.Add(Row("Plank Spacing", numPlankSpace));
        flow.Controls.Add(Row("Plank Darkness", numPlankDark));

        var btnApplyFx = MakeButton("Apply effects to all canvases", 500);
        btnApplyFx.Height = 40;
        btnApplyFx.Click += (_, _) => ApplyEffectsAll();
        flow.Controls.Add(btnApplyFx);

        // AUTO-SHADE
        flow.Controls.Add(Header("Auto-Shade"));
        _numLightAngle = MakeNud(100, 0, 100000, 60);
        _numLightAngle.ValueChanged += (_, _) => _shade.LightAngle = (float)_numLightAngle.Value;

        _numLightInt = MakeNud(100, 0, 100000, 55);
        _numLightInt.ValueChanged += (_, _) => _shade.LightIntensity = (int)_numLightInt.Value;

        _numShadowInt = MakeNud(100, 0, 100000, 55);
        _numShadowInt.ValueChanged += (_, _) => _shade.ShadowIntensity = (int)_numShadowInt.Value;

        _numContrast = MakeNud(100, 0, 100000, 100);
        _numContrast.ValueChanged += (_, _) => _shade.Contrast = (int)_numContrast.Value;

        _numAmbient = MakeNud(100, 0, 100, 5);
        _numAmbient.ValueChanged += (_, _) => _shade.AmbientFloor = (int)_numAmbient.Value;

        _numBorder = MakeNud(100, 0, 100000, 60);
        _numBorder.ValueChanged += (_, _) => _shade.BorderClarity = (int)_numBorder.Value;

        _numBlend = MakeNud(100, 0, 100000, 100);
        _numBlend.ValueChanged += (_, _) => _shade.BlendAmount = (int)_numBlend.Value;

        _cmbFalloff = MakeCombo(180);
        _cmbFalloff.Items.AddRange(Enum.GetNames(typeof(FalloffMode)));
        _cmbFalloff.SelectedIndex = 0;
        _cmbFalloff.SelectedIndexChanged += (_, _) => _shade.Falloff = (FalloffMode)_cmbFalloff.SelectedIndex;

        flow.Controls.Add(Row("Light Angle", _numLightAngle));
        flow.Controls.Add(Row("Light Intensity", _numLightInt));
        flow.Controls.Add(Row("Shadow Intensity", _numShadowInt));
        flow.Controls.Add(Row("Contrast", _numContrast));
        flow.Controls.Add(Row("Ambient Color", _numAmbient));
        flow.Controls.Add(Row("Border Clarity", _numBorder));
        flow.Controls.Add(Row("Blend Amount", _numBlend));
        flow.Controls.Add(Row("Falloff Mode", _cmbFalloff));

        var btnCentering = MakeButton("Set centering (click canvas)", 500);
        btnCentering.Height = 40;
        btnCentering.Click += (_, _) => { _canvas.CenteringMode = !_canvas.CenteringMode; _canvas.Invalidate(); };
        flow.Controls.Add(btnCentering);

        _perCanvasHost = new FlowLayoutPanel { Width = 500, Height = 400, FlowDirection = FlowDirection.TopDown, AutoScroll = true, WrapContents = false, Margin = new Padding(0, 8, 0, 8) };
        flow.Controls.Add(_perCanvasHost);

        var btnShade = MakeButton("Auto-Shade All Canvases", 500);
        btnShade.Height = 44;
        btnShade.BackColor = Color.FromArgb(100, 200, 100);
        btnShade.Click += (_, _) => AutoShadeAll();
        flow.Controls.Add(btnShade);

        // MERGE
        flow.Controls.Add(Header("Merge"));
        var btnMergeAll = MakeButton("Merge All Canvases -> PNG", 500);
        btnMergeAll.Height = 40;
        btnMergeAll.Click += (_, _) => MergeCanvases();
        var btnMergeTwo = MakeButton("Merge Two PNG Files...", 500);
        btnMergeTwo.Height = 40;
        btnMergeTwo.Click += (_, _) => MergeTwoFilesDialog();
        flow.Controls.Add(btnMergeAll);
        flow.Controls.Add(btnMergeTwo);

        // HISTORY
        flow.Controls.Add(Header("History"));
        var row = new FlowLayoutPanel { Width = 500, Height = 44, Margin = new Padding(0, 8, 0, 8) };
        var btnUndo = MakeButton("Undo", 158);
        btnUndo.Height = 36;
        btnUndo.Click += (_, _) => DoUndo();
        var btnRedo = MakeButton("Redo", 158);
        btnRedo.Height = 36;
        btnRedo.Click += (_, _) => DoRedo();
        var btnClear = MakeButton("Clear All", 158);
        btnClear.Height = 36;
        btnClear.Click += (_, _) => ClearAll();
        row.Controls.AddRange(new Control[] { btnUndo, btnRedo, btnClear });
        flow.Controls.Add(row);
    }

    private Label Header(string t) => new()
    {
        Text = t,
        Font = new Font(Font, FontStyle.Bold),
        ForeColor = Color.LightGray,
        Width = 500,
        Height = 36,
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 20, 0, 6)
    };

    private Label InlineLabel(string t) => new()
    {
        Text = t,
        ForeColor = Color.White,
        AutoSize = true,
        Margin = new Padding(8, 17, 4, 0)
    };

    private FlowLayoutPanel Row(string label, Control ctrl)
    {
        var p = new FlowLayoutPanel
        {
            Width = 500,
            Height = 40,
            WrapContents = false,
            Margin = new Padding(0, 4, 0, 4)
        };
        p.Controls.Add(new Label
        {
            Text = label,
            Width = 180,
            Height = 36,
            AutoSize = false,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 0)
        });
        ctrl.Margin = new Padding(0, 6, 0, 0);
        p.Controls.Add(ctrl);
        return p;
    }

    private Button MakeButton(string text, int width) => new()
    {
        Text = text,
        Width = width,
        Height = 36,
        ForeColor = Color.Black,
        BackColor = Color.LightGray,
        FlatStyle = FlatStyle.Standard,
        Margin = new Padding(0, 4, 4, 4)
    };

    private NumericUpDown MakeNud(int width, decimal min, decimal max, decimal val) => new WheelThroughNumericUpDown
    {
        Width = width,
        Minimum = min,
        Maximum = max,
        Value = val,
        ForeColor = Color.Black,
        BackColor = Color.White,
        Margin = new Padding(0, 6, 0, 0)
    };

    private ComboBox MakeCombo(int width) => new WheelThroughComboBox
    {
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        ForeColor = Color.Black,
        BackColor = Color.White,
        Margin = new Padding(0, 6, 0, 0)
    };

    private void WireCanvas()
    {
        _canvas.PaintStarted += () => _undo.Push(_grid.Snapshot());
        _canvas.PaintEnded += () => { _canvas.InvalidateGhosts(); _canvas.Invalidate(); };
        _canvas.ColorPicked += c => SetCurrentColor(c);
        _canvas.StatusChanged += s => _lblStatusPos.Text = s;

        _canvas.ZoomChanged += () =>
        {
            int z = (int)Math.Min(_numZoom.Maximum, Math.Max(_numZoom.Minimum, _canvas.Zoom));
            if (_numZoom.Value != z)
                _numZoom.Value = z;
            UpdateStatus();
        };
    }

    private void SetTool(ToolMode mode)
    {
        _canvas.Tool = mode;
        _lblStatusTool.Text = "Tool: " + mode;
    }

    private void SetCurrentColor(Color c)
    {
        _canvas.CurrentColor = c;
        _swatch.BackColor = c;
        if (!_colorHistory.Contains(c))
        {
            _colorHistory.Insert(0, c);
            if (_colorHistory.Count > 8) _colorHistory.RemoveAt(_colorHistory.Count - 1);
            RefreshColorHistory();
        }
    }

    private void RefreshColorHistory()
    {
        _colorHistoryBar.Controls.Clear();
        foreach (var c in _colorHistory)
        {
            var b = new Button { Width = 44, Height = 36, BackColor = c, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 4, 0) };
            b.FlatAppearance.BorderColor = Color.Black;
            var cc = c;
            b.Click += (_, _) => SetCurrentColor(cc);
            _colorHistoryBar.Controls.Add(b);
        }
    }

    private void PickColorDialog()
    {
        using var dlg = new ColorDialog { Color = _canvas.CurrentColor, FullOpen = true };
        if (dlg.ShowDialog() == DialogResult.OK) SetCurrentColor(dlg.Color);
    }

    private void RefreshFromGrid()
    {
        _canvas.Grid = _grid;
        _canvas.Zoom = (int)_numZoom.Value;
        _numSize.Value = _grid.Size;
        _numCols.Value = _grid.Cols;
        _numRows.Value = _grid.Rows;
        RefreshPerCanvasControls();
        UpdateSlotLabel();
        UpdateStatus();
        _canvas.InvalidateGhosts();
        _canvas.Invalidate();
    }

    private void RefreshPerCanvasControls()
    {
        _perCanvasHost.Controls.Clear();
        for (int i = 0; i < _grid.Count; i++)
        {
            int idx = i;
            var gb = new GroupBox { Text = "#" + i, Width = 480, Height = 380, ForeColor = Color.LightGray, Margin = new Padding(0, 8, 0, 8) };
            var flip = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            var cb = new CheckBox { Text = "Centering Enabled", Checked = _grid.CenteringEnabled[i], ForeColor = Color.LightGray, AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
            cb.CheckedChanged += (_, _) => _grid.CenteringEnabled[idx] = cb.Checked;
            flip.Controls.Add(cb);
            flip.Controls.Add(MiniRow("Roughness", _grid.Roughness[i], v => _grid.Roughness[idx] = v));
            flip.Controls.Add(MiniRow("Reflection", _grid.Reflection[i], v => _grid.Reflection[idx] = v));
            flip.Controls.Add(MiniRow("Falloff Strength", _grid.FalloffStrength[i], v => _grid.FalloffStrength[idx] = v));
            flip.Controls.Add(MiniRow("Edge Shadow", _grid.EdgeShadow[i], v => _grid.EdgeShadow[idx] = v));
            flip.Controls.Add(MiniRow("Hue Shift", _grid.HueShift[i], v => _grid.HueShift[idx] = v));
            flip.Controls.Add(MiniRow("Saturation Boost", _grid.SatBoost[i], v => _grid.SatBoost[idx] = v));
            gb.Controls.Add(flip);
            _perCanvasHost.Controls.Add(gb);
        }
    }

    private FlowLayoutPanel MiniRow(string label, int value, Action<int> setter)
    {
        var p = new FlowLayoutPanel { Width = 460, Height = 40, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        p.Controls.Add(new Label
        {
            Text = label,
            Width = 180,
            Height = 32,
            AutoSize = false,
            ForeColor = Color.LightGray,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 0)
        });
        var num = new WheelThroughNumericUpDown
        {
            Width = 100,
            Maximum = 100000,
            Value = value,
            ForeColor = Color.Black,
            BackColor = Color.White,
            Margin = new Padding(0, 6, 0, 0)
        };
        num.ValueChanged += (_, _) => setter((int)num.Value);
        p.Controls.Add(num);
        return p;
    }

    private void OnChangeSize(int newSize)
    {
        if (newSize == _grid.Size) return;
        if (MessageBox.Show($"Change size to {newSize}×{newSize}? All content will be wiped.",
            "Resize", MessageBoxButtons.YesNo) != DialogResult.Yes)
        { _numSize.Value = _grid.Size; return; }
        _grid = new CanvasGrid(newSize, _grid.Cols, _grid.Rows);
        _undo.Clear();
        RefreshFromGrid();
    }

    private void OnChangeGrid()
    {
        int nc = (int)_numCols.Value, nr = (int)_numRows.Value;
        if (nc == _grid.Cols && nr == _grid.Rows) return;
        if (MessageBox.Show($"Change grid to {nc}×{nr}? Content will be re-laid out.",
            "Grid", MessageBoxButtons.YesNo) != DialogResult.Yes)
        { _numCols.Value = _grid.Cols; _numRows.Value = _grid.Rows; return; }

        var old = _grid;
        var fresh = new CanvasGrid(old.Size, nc, nr);
        int copyC = Math.Min(old.Cols, nc);
        int copyR = Math.Min(old.Rows, nr);
        for (int y = 0; y < copyR; y++)
            for (int x = 0; x < copyC; x++)
            {
                int src = y * old.Cols + x;
                int dst = y * nc + x;
                Array.Copy(old.Canvases[src], fresh.Canvases[dst], fresh.Canvases[dst].Length);
                fresh.CenteringEnabled[dst] = old.CenteringEnabled[src];
                fresh.CenteringPoints[dst] = old.CenteringPoints[src];
            }
        _grid = fresh;
        _undo.Clear();
        RefreshFromGrid();
    }

    private void TransformGrid(Action<CanvasGrid> transformAction)
    {
        _undo.Push(_grid.Snapshot());
        transformAction(_grid);
        _canvas.InvalidateGhosts();
        _canvas.Invalidate();
    }

    private void AutoShadeAll()
    {
        _undo.Push(_grid.Snapshot());
        AutoShader.Apply(_grid, _shade);
        _canvas.InvalidateGhosts();
        _canvas.Invalidate();
    }

    private void ApplyEffectsAll()
    {
        _undo.Push(_grid.Snapshot());
        for (int i = 0; i < _grid.Count; i++)
        {
            var pixels = _grid.Canvases[i];
            int scx = (i % _grid.Cols) * _grid.Size;
            int scy = (i / _grid.Cols) * _grid.Size;
            for (int y = 0; y < _grid.Size; y++)
                for (int x = 0; x < _grid.Size; x++)
                {
                    int p = y * _grid.Size + x;
                    if (pixels[p].A < 3) continue;
                    pixels[p] = Effects.Apply(pixels[p], scx + x, scy + y, 0, _effects);
                }
            _grid.InvalidateCell(i);
        }
        _canvas.InvalidateGhosts();
        _canvas.Invalidate();
    }

    private void DoUndo()
    {
        var prev = _undo.Undo(_grid.Snapshot());
        if (prev == null) return;
        _grid.Restore(prev);
        _canvas.InvalidateGhosts();
        _canvas.Invalidate();
    }

    private void DoRedo()
    {
        var next = _undo.Redo(_grid.Snapshot());
        if (next == null) return;
        _grid.Restore(next);
        _canvas.InvalidateGhosts();
        _canvas.Invalidate();
    }

    private void ClearAll()
    {
        if (MessageBox.Show("Clear all canvases?", "Confirm", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _undo.Push(_grid.Snapshot());
        _grid.ClearAll();
        _canvas.InvalidateGhosts();
        _canvas.Invalidate();
    }

    private void LoadFileList()
    {
        _files.Clear();
        if (Directory.Exists(FileIO.TexturesDir))
            foreach (var p in Directory.GetFiles(FileIO.TexturesDir, "*.png", SearchOption.AllDirectories))
                _files.Add(p);
        _files.Sort();
        _fileList.Items.Clear();
        foreach (var f in _files)
            _fileList.Items.Add(Path.GetRelativePath(FileIO.TexturesDir, f));
    }

    private void LoadSelectedFile()
    {
        if (_fileList.SelectedIndex < 0 || _fileList.SelectedIndex >= _files.Count) return;
        ImportTexture(_files[_fileList.SelectedIndex]);
    }

    private void ImportTexture(string path)
    {
        try
        {
            using var bmp = FileIO.LoadPng(path);
            if (bmp.Width != bmp.Height || bmp.Width < 4)
            { MessageBox.Show("Only square PNGs >= 4 px."); return; }

            if (_multiMode && _grid.Count > 1)
            {
                int idx = _nextSlot % _grid.Count;
                var pixels = PixelMath.BitmapToPixels(bmp);
                if (bmp.Width != _grid.Size)
                    pixels = PixelMath.ResizeNearest(pixels, bmp.Width, bmp.Height, _grid.Size, _grid.Size);
                _undo.Push(_grid.Snapshot());
                Array.Copy(pixels, _grid.Canvases[idx], Math.Min(pixels.Length, _grid.Canvases[idx].Length));
                _grid.InvalidateCell(idx);
                _nextSlot = (_nextSlot + 1) % _grid.Count;
                _canvas.InvalidateGhosts();
                _canvas.Invalidate();
                UpdateSlotLabel();
            }
            else
            {
                int size = bmp.Width;
                _grid = new CanvasGrid(size, 1, 1);
                Array.Copy(PixelMath.BitmapToPixels(bmp), _grid.Canvases[0], size * size);
                _fileName = Path.GetFileNameWithoutExtension(path);
                _undo.Clear();
                RefreshFromGrid();
            }
        }
        catch (Exception ex) { MessageBox.Show("Load failed: " + ex.Message); }
    }

    private void UpdateSlotLabel()
    {
        _lblSlot.Text = _multiMode && _grid.Count > 1
            ? $"Next slot: {_nextSlot + 1}/{_grid.Count}"
            : "";
    }

    private void OnImportPng()
    {
        using var dlg = new OpenFileDialog { Filter = "PNG|*.png" };
        if (dlg.ShowDialog() == DialogResult.OK) ImportTexture(dlg.FileName);
    }

    private void OnExportComposite()
    {
        using var dlg = new SaveFileDialog { Filter = "PNG|*.png", FileName = _fileName + "_composite.png" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        FileIO.ExportComposite(dlg.FileName, _grid);
    }

    private void MergeCanvases()
    {
        using var dlg = new SaveFileDialog { Filter = "PNG|*.png", FileName = _fileName + "_merged.png" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        FileIO.MergeAllCanvases(dlg.FileName, _grid);
    }

    private void MergeTwoFilesDialog()
    {
        if (_files.Count < 2) { MessageBox.Show("Need at least two PNGs."); return; }
        using var dlg = new Form
        {
            Text = "Merge Two Files",
            Width = 500,
            Height = 300,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Color.FromArgb(40, 40, 40),
            ForeColor = Color.White
        };

        var lblA = new Label { Text = "A:", Left = 20, Top = 20, Width = 440, ForeColor = Color.White, Height = 24 };
        var cmbA = new ComboBox { Left = 20, Top = 44, Width = 440, DropDownStyle = ComboBoxStyle.DropDownList, ForeColor = Color.Black, BackColor = Color.White };
        var lblB = new Label { Text = "B:", Left = 20, Top = 74, Width = 440, ForeColor = Color.White, Height = 24 };
        var cmbB = new ComboBox { Left = 20, Top = 98, Width = 440, DropDownStyle = ComboBoxStyle.DropDownList, ForeColor = Color.Black, BackColor = Color.White };
        var lblOut = new Label { Text = "Output name:", Left = 20, Top = 128, Width = 440, ForeColor = Color.White, Height = 24 };
        var txtOut = new TextBox { Left = 20, Top = 152, Width = 440, Text = "merged", ForeColor = Color.Black, BackColor = Color.White };

        foreach (var f in _files) { cmbA.Items.Add(f); cmbB.Items.Add(f); }
        cmbA.SelectedIndex = 0;
        cmbB.SelectedIndex = Math.Min(1, _files.Count - 1);

        var btnOk = new Button
        {
            Text = "Merge",
            Left = 20,
            Top = 196,
            Width = 140,
            Height = 36,
            DialogResult = DialogResult.OK,
            ForeColor = Color.Black,
            BackColor = Color.LightGray
        };

        dlg.Controls.AddRange(new Control[] { lblA, cmbA, lblB, cmbB, lblOut, txtOut, btnOk });

        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string outPath = Path.Combine(FileIO.TexturesDir, txtOut.Text.Trim() + ".png");
            FileIO.MergeTwoFiles((string)cmbA.SelectedItem, (string)cmbB.SelectedItem, outPath);
            LoadFileList();
        }
        catch (Exception ex) { MessageBox.Show("Merge failed: " + ex.Message); }
    }

    private void OnNew()
    {
        if (MessageBox.Show("Clear ALL canvases?", "New", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        _grid = new CanvasGrid(_grid.Size, _grid.Cols, _grid.Rows);
        _undo.Clear();
        _currentProjectPath = "";
        RefreshFromGrid();
    }

    private void OnOpenProject()
    {
        using var dlg = new OpenFileDialog { Filter = "Pick-celling project|*.pxb" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            _grid = FileIO.LoadProject(dlg.FileName);
            _currentProjectPath = dlg.FileName;
            _fileName = Path.GetFileNameWithoutExtension(dlg.FileName);
            _undo.Clear();
            RefreshFromGrid();
        }
        catch (Exception ex) { MessageBox.Show("Open failed: " + ex.Message); }
    }

    private void OnSaveProject(bool saveAs)
    {
        string path = _currentProjectPath;
        if (saveAs || string.IsNullOrEmpty(path))
        {
            using var dlg = new SaveFileDialog { Filter = "Pick-celling project|*.pxb", FileName = _fileName + ".pxb" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            path = dlg.FileName;
            _currentProjectPath = path;
            _fileName = Path.GetFileNameWithoutExtension(path);
        }
        try { FileIO.SaveProject(path, _grid, _shade); }
        catch (Exception ex) { MessageBox.Show("Save failed: " + ex.Message); }
    }

    private void OnOpenTextures()
    {
        Directory.CreateDirectory(FileIO.TexturesDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = FileIO.TexturesDir,
            UseShellExecute = true
        });
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z)) { DoUndo(); return true; }
        if (keyData == (Keys.Control | Keys.Y)) { DoRedo(); return true; }
        if (keyData == (Keys.Control | Keys.S)) { OnSaveProject(false); return true; }
        if (keyData == (Keys.Control | Keys.O)) { OnOpenProject(); return true; }
        if (keyData == (Keys.Control | Keys.N)) { OnNew(); return true; }
        if (keyData == (Keys.Control | Keys.H)) { TransformGrid(g => g.FlipHorizontal()); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.H)) { TransformGrid(g => g.FlipVertical()); return true; }
        if (keyData == (Keys.Control | Keys.R)) { TransformGrid(g => g.Rotate90()); return true; }
        if (keyData == Keys.B) { SetTool(ToolMode.Paint); return true; }
        if (keyData == Keys.E) { SetTool(ToolMode.Erase); return true; }
        if (keyData == Keys.G) { SetTool(ToolMode.Fill); return true; }
        if (keyData == Keys.L) { SetTool(ToolMode.Line); return true; }
        if (keyData == Keys.I) { SetTool(ToolMode.Pick); return true; }
        if (keyData == Keys.R) { SetTool(ToolMode.Blur); return true; }
        if (keyData == Keys.OemOpenBrackets)
        {
            _canvas.BrushSize = Math.Max(1, _canvas.BrushSize - 1);
            _numBrush.Value = _canvas.BrushSize;
            return true;
        }
        if (keyData == Keys.OemCloseBrackets)
        {
            _canvas.BrushSize++;
            _numBrush.Value = _canvas.BrushSize;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateStatus()
    {
        _lblStatusZoom.Text = "Zoom: " + _canvas.Zoom;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var s = new AppSettings
        {
            WindowWidth = Width,
            WindowHeight = Height,
            RecentFiles = _recent
        };
        FileIO.SaveSettings(s);
        base.OnFormClosing(e);
    }
}

// ================================================================
// Custom Controls for Scroll Wheel behavior
// ================================================================

public class WheelThroughNumericUpDown : NumericUpDown
{
    private const int WM_MOUSEWHEEL = 0x020A;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private bool _canChangeValue = false;
    private System.Windows.Forms.Timer _hoverTimer;

    public WheelThroughNumericUpDown()
    {
        _hoverTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _hoverTimer.Tick += (s, e) => { _canChangeValue = true; _hoverTimer.Stop(); };
        this.MouseEnter += (s, e) => { _canChangeValue = false; _hoverTimer.Stop(); };
        this.MouseLeave += (s, e) => { _canChangeValue = false; _hoverTimer.Stop(); };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEWHEEL)
        {
            if (Focused || _canChangeValue)
            {
                base.WndProc(ref m);
            }
            else
            {
                Control parent = Parent;
                while (parent != null && !(parent is ScrollableControl))
                    parent = parent.Parent;

                if (parent != null)
                {
                    SendMessage(parent.Handle, WM_MOUSEWHEEL, m.WParam, m.LParam);
                }
                _canChangeValue = false;
                _hoverTimer.Stop();
                _hoverTimer.Start();
            }
            return;
        }
        base.WndProc(ref m);
    }
}

public class WheelThroughComboBox : ComboBox
{
    private const int WM_MOUSEWHEEL = 0x020A;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private bool _canChangeValue = false;
    private System.Windows.Forms.Timer _hoverTimer;

    public WheelThroughComboBox()
    {
        _hoverTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _hoverTimer.Tick += (s, e) => { _canChangeValue = true; _hoverTimer.Stop(); };
        this.MouseEnter += (s, e) => { _canChangeValue = false; _hoverTimer.Stop(); };
        this.MouseLeave += (s, e) => { _canChangeValue = false; _hoverTimer.Stop(); };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEWHEEL)
        {
            if (Focused || _canChangeValue)
            {
                base.WndProc(ref m);
            }
            else
            {
                Control parent = Parent;
                while (parent != null && !(parent is ScrollableControl))
                    parent = parent.Parent;

                if (parent != null)
                {
                    SendMessage(parent.Handle, WM_MOUSEWHEEL, m.WParam, m.LParam);
                }
                _canChangeValue = false;
                _hoverTimer.Stop();
                _hoverTimer.Start();
            }
            return;
        }
        base.WndProc(ref m);
    }
}