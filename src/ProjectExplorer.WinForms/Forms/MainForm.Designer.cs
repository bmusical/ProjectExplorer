namespace ProjectExplorer.WinForms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    // Branded header band
    private Panel headerPanel;
    private PictureBox headerLogo;
    private Label headerTitle;
    private Label headerTagline;

    // Top navigation
    private ToolStrip toolStripNav;
    private ToolStripButton btnBack;
    private ToolStripButton btnForward;
    private ToolStripButton btnUp;
    private ToolStripButton btnOpenExplorer;
    private ToolStripButton btnOpenCmd;
    private ToolStripButton btnOpenPowerShell;
    private ToolStripButton btnCopyPath;
    private ToolStripTextBox txtAddress;

    // Main splitter
    private SplitContainer splitMain;

    // Tree view (left panel)
    private TreeView treeView;

    // List view (right panel)
    private ListView listView;

    // File preview panel (right panel, shown instead of listView for a selected FileReference)
    private FilePreviewPanel filePreviewPanel;

    // Web resource preview panel (right panel, shown instead of listView for a selected WebResource)
    private WebResourcePreviewPanel webResourcePreviewPanel;

    // Status bar
    private StatusStrip statusStrip;
    private ToolStripStatusLabel lblStatus;

    // Image lists
    private ImageList imageListSmall;
    private ImageList imageListLarge;
    private ImageList imageListExtraLarge;

    // Menu strip
    private MenuStrip menuStrip;
    private ToolStripMenuItem menuFile;
    private ToolStripMenuItem menuFileNewProject;
    private ToolStripMenuItem menuFileExit;
    private ToolStripMenuItem menuView;
    private ToolStripMenuItem menuViewDetails;
    private ToolStripMenuItem menuViewExtraLargeIcons;
    private ToolStripMenuItem menuViewLargeIcons;
    private ToolStripMenuItem menuViewSmallIcons;
    private ToolStripMenuItem menuViewList;
    private ToolStripMenuItem menuViewTile;
    private ToolStripMenuItem menuProject;
    private ToolStripMenuItem menuProjectNewCollection;
    private ToolStripMenuItem menuProjectAddFolder;
    private ToolStripMenuItem menuHelp;
    private ToolStripMenuItem menuHelpRegister;
    private ToolStripMenuItem menuHelpCheckForUpdates;
    private ToolStripMenuItem menuHelpAbout;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.SuspendLayout();

        // ── Image Lists ──
        this.imageListSmall = new ImageList(this.components)
        {
            ImageSize = new Size(16, 16),
            ColorDepth = ColorDepth.Depth32Bit
        };
        this.imageListLarge = new ImageList(this.components)
        {
            ImageSize = new Size(48, 48),
            ColorDepth = ColorDepth.Depth32Bit
        };
        this.imageListExtraLarge = new ImageList(this.components)
        {
            ImageSize = new Size(96, 96),
            ColorDepth = ColorDepth.Depth32Bit
        };

        // ── Navigation ToolStrip ──
        // Larger, clearer buttons. Glyphs are drawn from the "Segoe MDL2 Assets"
        // system font (present on Windows 10/11) so no external icon assets are needed.
        var navGlyphFont = new Font("Segoe MDL2 Assets", 12f);

        this.btnBack = new ToolStripButton
        {
            Text = "\uE72B", // ChevronLeft / Back
            Font = navGlyphFont,
            ToolTipText = "Back",
            Enabled = false,
            DisplayStyle = ToolStripItemDisplayStyle.Text
        };
        this.btnForward = new ToolStripButton
        {
            Text = "\uE72A", // ChevronRight / Forward
            Font = navGlyphFont,
            ToolTipText = "Forward",
            Enabled = false,
            DisplayStyle = ToolStripItemDisplayStyle.Text
        };
        this.btnUp = new ToolStripButton
        {
            Text = "\uE74A", // Up
            Font = navGlyphFont,
            ToolTipText = "Up",
            DisplayStyle = ToolStripItemDisplayStyle.Text
        };
        // Action buttons get colour-coded "chip" icons (instead of plain glyph
        // text) so each one visually echoes the context-menu command it runs.
        this.btnOpenExplorer = new ToolStripButton
        {
            Image = ChipBitmap("\uE838", "Segoe MDL2 Assets", 24, Color.FromArgb(255, 236, 196), Color.FromArgb(180, 120, 20)),
            ToolTipText = "Open in Explorer",
            Enabled = false,
            DisplayStyle = ToolStripItemDisplayStyle.Image
        };
        this.btnOpenCmd = new ToolStripButton
        {
            Image = ChipBitmap(">_", "Consolas", 24, Color.FromArgb(32, 32, 32), Color.FromArgb(90, 230, 120)),
            ToolTipText = "Open Command Prompt Here",
            Enabled = false,
            DisplayStyle = ToolStripItemDisplayStyle.Image
        };
        this.btnOpenPowerShell = new ToolStripButton
        {
            Image = ChipBitmap("PS", "Consolas", 24, Color.FromArgb(1, 36, 86), Color.White),
            ToolTipText = "Open PowerShell Here",
            Enabled = false,
            DisplayStyle = ToolStripItemDisplayStyle.Image
        };
        this.btnCopyPath = new ToolStripButton
        {
            Image = ChipBitmap("\uE8C8", "Segoe MDL2 Assets", 24, Color.FromArgb(230, 230, 230), Color.FromArgb(70, 70, 70)),
            ToolTipText = "Copy Path",
            Enabled = false,
            DisplayStyle = ToolStripItemDisplayStyle.Image
        };
        this.txtAddress = new ToolStripTextBox
        {
            Name = "txtAddress",
            Size = new Size(500, 28),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f)
        };

        this.toolStripNav = new ToolStrip
        {
            Items = {
                this.btnBack, this.btnForward, this.btnUp, new ToolStripSeparator(),
                this.btnOpenExplorer, this.btnOpenCmd, this.btnOpenPowerShell, this.btnCopyPath,
                new ToolStripSeparator(), this.txtAddress
            },
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            ImageScalingSize = new Size(24, 24),
            AutoSize = false,
            Height = 40,
            Padding = new Padding(6, 4, 6, 4),
            RenderMode = ToolStripRenderMode.System,
            // A little extra 3D: buttons stay flat at rest and pop into a
            // raised/sunken bevel on hover/press instead of the flat system look.
            Renderer = new Toolbar3DRenderer()
        };

        this.btnBack.Click += BtnBack_Click;
        this.btnForward.Click += BtnForward_Click;
        this.btnUp.Click += BtnUp_Click;
        this.btnOpenExplorer.Click += BtnOpenExplorer_Click;
        this.btnOpenCmd.Click += BtnOpenCmd_Click;
        this.btnOpenPowerShell.Click += BtnOpenPowerShell_Click;
        this.btnCopyPath.Click += BtnCopyPath_Click;

        // ── Menu Strip ──
        this.menuFile = new ToolStripMenuItem { Text = "&File" };
        this.menuFileNewProject = new ToolStripMenuItem { Text = "New &Project...", ShortcutKeys = Keys.Control | Keys.N };
        this.menuFileExit = new ToolStripMenuItem { Text = "E&xit" };
        this.menuFile.DropDownItems.AddRange(new ToolStripItem[] { menuFileNewProject, new ToolStripSeparator(), menuFileExit });

        this.menuView = new ToolStripMenuItem { Text = "&View" };
        this.menuViewDetails = new ToolStripMenuItem { Text = "&Details", Checked = true };
        this.menuViewExtraLargeIcons = new ToolStripMenuItem { Text = "&Extra Large Icons" };
        this.menuViewLargeIcons = new ToolStripMenuItem { Text = "Large &Icons" };
        this.menuViewSmallIcons = new ToolStripMenuItem { Text = "&Small Icons" };
        this.menuViewList = new ToolStripMenuItem { Text = "&List" };
        this.menuViewTile = new ToolStripMenuItem { Text = "&Tile" };
        this.menuView.DropDownItems.AddRange(new ToolStripItem[] {
            menuViewDetails, menuViewExtraLargeIcons, menuViewLargeIcons, menuViewSmallIcons, menuViewList, menuViewTile
        });

        this.menuProject = new ToolStripMenuItem { Text = "&Project" };
        this.menuProjectNewCollection = new ToolStripMenuItem { Text = "New &Collection..." };
        this.menuProjectAddFolder = new ToolStripMenuItem { Text = "&Add Folder..." };
        this.menuProject.DropDownItems.AddRange(new ToolStripItem[] {
            menuProjectNewCollection, menuProjectAddFolder
        });

        this.menuHelpRegister       = new ToolStripMenuItem { Text = "&Register / License..." };
        this.menuHelpCheckForUpdates = new ToolStripMenuItem { Text = "Check for &Updates..." };
        this.menuHelpAbout           = new ToolStripMenuItem { Text = "&About Project Nest Explorer..." };
        this.menuHelp = new ToolStripMenuItem { Text = "&Help" };
        this.menuHelp.DropDownItems.AddRange(new ToolStripItem[]
        {
            menuHelpRegister, new ToolStripSeparator(),
            menuHelpCheckForUpdates, new ToolStripSeparator(),
            menuHelpAbout
        });

        this.menuStrip = new MenuStrip
        {
            Items = { this.menuFile, this.menuView, this.menuProject, this.menuHelp },
            Dock = DockStyle.Top
        };

        this.menuFileNewProject.Click += MenuFileNewProject_Click;
        this.menuFileExit.Click += (s, e) => Close();
        this.menuProjectNewCollection.Click += MenuProjectNewCollection_Click;
        this.menuProjectAddFolder.Click += MenuProjectAddFolder_Click;
        this.menuHelpRegister.Click        += (s, e) => OpenRegistrationDialog();
        this.menuHelpCheckForUpdates.Click += (s, e) => CheckForUpdates(silent: false);
        this.menuHelpAbout.Click           += (s, e) => new AboutForm().ShowDialog(this);
        this.menuViewDetails.Click += (s, e) => SetViewMode(AppView.Details);
        this.menuViewExtraLargeIcons.Click += (s, e) => SetViewMode(AppView.ExtraLargeIcon);
        this.menuViewLargeIcons.Click += (s, e) => SetViewMode(AppView.LargeIcon);
        this.menuViewSmallIcons.Click += (s, e) => SetViewMode(AppView.SmallIcon);
        this.menuViewList.Click += (s, e) => SetViewMode(AppView.List);
        this.menuViewTile.Click += (s, e) => SetViewMode(AppView.Tile);

        // ── Branded Header Band ──
        // Reuses the same accent blue as the About and Registration dialogs so the
        // main window shares the product's visual identity.
        var accentBlue = Color.FromArgb(30, 80, 160);

        this.headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = accentBlue
        };
        // Subtle vertical gradient + bottom shadow line so the header reads as a
        // raised band sitting above the rest of the window instead of a flat fill.
        this.headerPanel.Paint += (s, e) =>
        {
            var rect = this.headerPanel.ClientRectangle;
            using var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(
                rect, ControlPaint.Light(accentBlue, 0.2f), ControlPaint.Dark(accentBlue, 0.1f),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical);
            e.Graphics.FillRectangle(gradient, rect);
            using var shadowPen = new Pen(Color.FromArgb(70, Color.Black));
            e.Graphics.DrawLine(shadowPen, 0, rect.Bottom - 1, rect.Width, rect.Bottom - 1);
        };

        this.headerLogo = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(40, 40),
            Location = new Point(12, 8),
            BackColor = Color.Transparent
        };
        try
        {
            using var logoStream = System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("ProjectExplorer.WinForms.Assets.logo.png");
            if (logoStream != null)
                this.headerLogo.Image = Image.FromStream(logoStream);
        }
        catch { /* logo is decorative — ignore load failures */ }

        this.headerTitle = new Label
        {
            Text = "Project Nest Explorer",
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(60, 7)
        };

        this.headerTagline = new Label
        {
            Text = "All your projects, one place.",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            ForeColor = Color.FromArgb(200, 220, 255),
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(62, 33)
        };

        this.headerPanel.Controls.AddRange(new Control[] { this.headerLogo, this.headerTitle, this.headerTagline });

        // ── Tree View ──
        this.treeView = new TreeView
        {
            Dock = DockStyle.Fill,
            HideSelection = false,
            ShowRootLines = true,
            ShowPlusMinus = true,
            ShowLines = true,
            ShowNodeToolTips = true,
            LabelEdit = true,
            ImageList = imageListSmall,
            StateImageList = imageListSmall
        };
        this.treeView.AfterSelect += TreeView_AfterSelect;
        this.treeView.BeforeExpand += TreeView_BeforeExpand;
        this.treeView.NodeMouseClick += TreeView_NodeMouseClick;
        this.treeView.KeyDown += TreeView_KeyDown;
        this.treeView.AfterLabelEdit += TreeView_AfterLabelEdit;

        // ── List View ──
        this.listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = System.Windows.Forms.View.Details,
            HideSelection = false,
            MultiSelect = true,
            FullRowSelect = true,
            GridLines = false,
            ShowItemToolTips = true,
            SmallImageList = imageListSmall,
            LargeImageList = imageListLarge
        };

        // Default columns for Details view
        this.listView.Columns.AddRange(new ColumnHeader[]
        {
            new ColumnHeader { Text = "Name", Width = 250 },
            new ColumnHeader { Text = "Size", Width = 100, TextAlign = HorizontalAlignment.Right },
            new ColumnHeader { Text = "Type", Width = 150 },
            new ColumnHeader { Text = "Date Modified", Width = 150 },
            new ColumnHeader { Text = "Description", Width = 250 }
        });

        this.listView.DoubleClick += ListView_DoubleClick;
        this.listView.ColumnClick += ListView_ColumnClick;
        this.listView.MouseClick += ListView_MouseClick;
        this.Width = 1200;

        // ── File Preview Panel (shown instead of listView for a selected FileReference) ──
        this.filePreviewPanel = new FilePreviewPanel { Visible = false };
        this.filePreviewPanel.OpenRequested += FilePreviewPanel_OpenRequested;
        this.filePreviewPanel.PropertiesRequested += FilePreviewPanel_PropertiesRequested;

        // ── Web Resource Preview Panel (shown instead of listView for a selected WebResource) ──
        this.webResourcePreviewPanel = new WebResourcePreviewPanel { Visible = false };
        this.webResourcePreviewPanel.OpenExternalRequested += WebResourcePreviewPanel_OpenExternalRequested;

        // ── Split Container ──
        this.splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = SystemColors.Control
        };
        this.splitMain.Panel1.Controls.Add(this.treeView);
        this.splitMain.Panel2.Controls.Add(this.filePreviewPanel);
        this.splitMain.Panel2.Controls.Add(this.webResourcePreviewPanel);
        this.splitMain.Panel2.Controls.Add(this.listView);

        // ── Status Strip ──
        this.lblStatus = new ToolStripStatusLabel { Text = "Ready", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        this.statusStrip = new StatusStrip { Items = { lblStatus } };

        // ── Form ──
        // Docked controls are added in reverse visual order (last added docks first / topmost).
        this.Controls.AddRange(new Control[] {
            this.statusStrip,
            this.splitMain,
            this.toolStripNav,
            this.headerPanel,
            this.menuStrip
        });
        this.MainMenuStrip = this.menuStrip;

        this.Text = "Project Nest Explorer";
        this.Font = new Font("Segoe UI", 9f);
        this.Size = new Size(1200, 750);
        this.MinimumSize = new Size(800, 500);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.KeyPreview = true;
        this.Icon = LoadAppIcon();

        this.ResumeLayout(false);
        this.PerformLayout();
    }

    /// <summary>
    /// Loads the embedded application icon, falling back to the system application
    /// icon if the resource cannot be found (e.g. during design-time).
    /// </summary>
    private static Icon LoadAppIcon()
    {
        try
        {
            using var stream = System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("ProjectExplorer.WinForms.Assets.app.ico");
            if (stream != null)
                return new Icon(stream);
        }
        catch { /* fall through to system icon */ }
        return SystemIcons.Application;
    }
}
