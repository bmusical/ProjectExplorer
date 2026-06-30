namespace ProjectExplorer.WinForms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    // Top navigation
    private ToolStrip toolStripNav;
    private ToolStripButton btnBack;
    private ToolStripButton btnForward;
    private ToolStripButton btnUp;
    private ToolStripButton btnOpenExplorer;
    private ToolStripTextBox txtAddress;

    // Main splitter
    private SplitContainer splitMain;

    // Tree view (left panel)
    private TreeView treeView;

    // List view (right panel)
    private ListView listView;

    // Status bar
    private StatusStrip statusStrip;
    private ToolStripStatusLabel lblStatus;

    // Image lists
    private ImageList imageListSmall;
    private ImageList imageListLarge;

    // Menu strip
    private MenuStrip menuStrip;
    private ToolStripMenuItem menuFile;
    private ToolStripMenuItem menuFileNewProject;
    private ToolStripMenuItem menuFileExit;
    private ToolStripMenuItem menuView;
    private ToolStripMenuItem menuViewDetails;
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

        // ── Navigation ToolStrip ──
        this.btnBack = new ToolStripButton { Text = "←", ToolTipText = "Back", Enabled = false };
        this.btnForward = new ToolStripButton { Text = "→", ToolTipText = "Forward", Enabled = false };
        this.btnUp = new ToolStripButton { Text = "↑", ToolTipText = "Up" };
        this.btnOpenExplorer = new ToolStripButton { Text = "📁", ToolTipText = "Open in Explorer", Enabled = false };
        this.txtAddress = new ToolStripTextBox
        {
            Name = "txtAddress",
            Size = new Size(400, 25),
            BorderStyle = BorderStyle.FixedSingle
        };

        this.toolStripNav = new ToolStrip
        {
            Items = { this.btnBack, this.btnForward, this.btnUp, this.btnOpenExplorer, this.txtAddress },
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            Padding = new Padding(4, 2, 4, 2)
        };

        this.btnBack.Click += BtnBack_Click;
        this.btnForward.Click += BtnForward_Click;
        this.btnUp.Click += BtnUp_Click;
        this.btnOpenExplorer.Click += BtnOpenExplorer_Click;

        // ── Menu Strip ──
        this.menuFile = new ToolStripMenuItem { Text = "&File" };
        this.menuFileNewProject = new ToolStripMenuItem { Text = "New &Project...", ShortcutKeys = Keys.Control | Keys.N };
        this.menuFileExit = new ToolStripMenuItem { Text = "E&xit" };
        this.menuFile.DropDownItems.AddRange(new ToolStripItem[] { menuFileNewProject, new ToolStripSeparator(), menuFileExit });

        this.menuView = new ToolStripMenuItem { Text = "&View" };
        this.menuViewDetails = new ToolStripMenuItem { Text = "&Details", Checked = true };
        this.menuViewLargeIcons = new ToolStripMenuItem { Text = "Large &Icons" };
        this.menuViewSmallIcons = new ToolStripMenuItem { Text = "&Small Icons" };
        this.menuViewList = new ToolStripMenuItem { Text = "&List" };
        this.menuViewTile = new ToolStripMenuItem { Text = "&Tile" };
        this.menuView.DropDownItems.AddRange(new ToolStripItem[] {
            menuViewDetails, menuViewLargeIcons, menuViewSmallIcons, menuViewList, menuViewTile
        });

        this.menuProject = new ToolStripMenuItem { Text = "&Project" };
        this.menuProjectNewCollection = new ToolStripMenuItem { Text = "New &Collection..." };
        this.menuProjectAddFolder = new ToolStripMenuItem { Text = "&Add Folder..." };
        this.menuProject.DropDownItems.AddRange(new ToolStripItem[] {
            menuProjectNewCollection, menuProjectAddFolder
        });

        this.menuHelpRegister       = new ToolStripMenuItem { Text = "&Register / License..." };
        this.menuHelpCheckForUpdates = new ToolStripMenuItem { Text = "Check for &Updates..." };
        this.menuHelpAbout           = new ToolStripMenuItem { Text = "&About Project Nest..." };
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
        this.menuViewLargeIcons.Click += (s, e) => SetViewMode(AppView.LargeIcon);
        this.menuViewSmallIcons.Click += (s, e) => SetViewMode(AppView.SmallIcon);
        this.menuViewList.Click += (s, e) => SetViewMode(AppView.List);
        this.menuViewTile.Click += (s, e) => SetViewMode(AppView.Tile);

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
        // ── Split Container ──
        this.splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = SystemColors.Control
        };
        this.splitMain.Panel1.Controls.Add(this.treeView);
        this.splitMain.Panel2.Controls.Add(this.listView);

        // ── Status Strip ──
        this.lblStatus = new ToolStripStatusLabel { Text = "Ready", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        this.statusStrip = new StatusStrip { Items = { lblStatus } };

        // ── Form ──
        this.Controls.AddRange(new Control[] {
            this.statusStrip,
            this.splitMain,
            this.toolStripNav,
            this.menuStrip
        });
        this.MainMenuStrip = this.menuStrip;

        this.Text = "Project Nest";
        this.Size = new Size(1200, 750);
        this.MinimumSize = new Size(800, 500);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.KeyPreview = true;
        this.Icon = SystemIcons.Application;

        this.ResumeLayout(false);
        this.PerformLayout();
    }
}
