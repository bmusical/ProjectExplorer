using System.IO;
using System.Text.Json;
using AutoUpdaterDotNET;
using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;
using ProjectExplorer.Shell;
using ProjectExplorer.Shell.Services;
using ProjectExplorer.WinForms.Helpers;
using LicenseState = ProjectExplorer.Core.Models.LicenseState;

namespace ProjectExplorer.WinForms;

public partial class MainForm : Form
{
    private readonly ProjectManager _projectManager;
    private readonly IShellIconProvider _shellIconProvider;
    private readonly IShellThumbnailProvider _shellThumbnailProvider;
    private readonly IShellPropertiesProvider _shellPropertiesProvider;

    // Tracks, per image ListViewItem, the icon key (shown in Small/Details/List
    // views) and the thumbnail key (shown in Large/Extra Large views), so we can
    // emulate Windows Explorer: icons in compact views, thumbnails in big views.
    private readonly Dictionary<ListViewItem, (string IconKey, string ThumbKey)> _imageItemKeys = new();

    // The current view mode, so async thumbnail loads know whether to apply
    // the thumbnail immediately (large views) or leave the icon in place.
    private AppView _currentView = AppView.Details;
    private readonly LicenseManager _licenseManager;
    private LicenseInfo _license;
    private readonly AppSettingsManager _appSettingsManager;

    // Navigation history
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    // Current state
    private Project? _currentProject;
    private string _currentPath = string.Empty;

    // The FileReference currently shown in filePreviewPanel, if any (null when listView is showing).
    private FileReference? _currentPreviewFileRef;

    // The WebResource currently shown in webResourcePreviewPanel, if any (null when listView is showing).
    private WebResource? _currentPreviewWebResource;

    // Drag-drop state
    private TreeNode? _dragHighlightNode;

    // ── Resource availability (unreachable folders/files/web resources) ──
    // Cache survives tree rebuilds (RefreshTreeView() recreates TreeNodes/ListViewItems, but not
    // this dictionary), keyed by ProjectChild.Id. Only FolderReference/FileReference/WebResource
    // entries are ever present.
    private readonly Dictionary<Guid, AvailabilityCheckResult> _availabilityCache = new();
    private readonly HashSet<Guid> _availabilityChecksInFlight = new();
    private readonly System.Windows.Forms.Timer _availabilityRetryTimer = new() { Interval = 20_000 };
    private static readonly HttpClient _availabilityHttpClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    // Cached rather than newed up per node/item: Font wraps a GDI handle, and
    // ApplyAvailabilityStyle runs on every tree rebuild and every retry-timer tick.
    private Font? _unavailableTreeFont;
    private Font? _unavailableListFont;

    // Tree UI state persistence
    private static readonly string _uiSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ProjectExplorer", "uisettings.json");

    private sealed class TreeUiSettings
    {
        public List<string> ExpandedTags { get; set; } = new();
        public string? SelectedTag { get; set; }
    }

    /// <summary>
    /// Allows external code to set the ListView's item sorter.
    /// </summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public System.Collections.IComparer ListViewItemSorter
    {
        set => listView.ListViewItemSorter = value;
    }

    // Node type tags for the tree view
    private const string TagProjectsRoot = "ProjectsRoot";
    private const string TagProject = "Project:";
    private const string TagCollection = "Collection:";
    private const string TagFolderRef = "FolderRef:";
    private const string TagWebResource = "WebResource:";
    private const string TagFileRef = "FileRef:";
    private const string TagRealFolder = "RealFolder:";

    public MainForm(ProjectManager projectManager, IShellIconProvider shellIconProvider,
                    IShellThumbnailProvider shellThumbnailProvider,
                    IShellPropertiesProvider shellPropertiesProvider,
                    LicenseManager licenseManager, LicenseInfo license,
                    AppSettingsManager appSettingsManager)
    {
        _projectManager         = projectManager;
        _shellIconProvider      = shellIconProvider;
        _shellThumbnailProvider = shellThumbnailProvider;
        _shellPropertiesProvider = shellPropertiesProvider;
        _licenseManager         = licenseManager;
        _license                = license;
        _appSettingsManager     = appSettingsManager;

        InitializeComponent();
        ApplyPersistedWindowBounds();

        // Adopt Windows 11 Explorer's visual style for the tree/list controls
        // (alternating hover/selection colors, no dotted focus rectangle).
        ModernWindowStyler.ApplyExplorerListStyle(treeView.Handle);
        ModernWindowStyler.ApplyExplorerListStyle(listView.Handle);

        SetWindowTitle();

        // Attach event handler for address bar - must be done after InitializeComponent
        if (txtAddress.TextBox != null)
        {
            txtAddress.TextBox.KeyDown += AddressBar_KeyDown;
        }

        LoadDefaultIcons();
        var persistedState = LoadTreeState();
        InitializeTreeView();
        treeView.BeginUpdate();
        RestoreTreeState(treeView.Nodes, new HashSet<string>(persistedState.ExpandedTags), persistedState.SelectedTag);
        treeView.EndUpdate();
        treeView.SelectedNode?.EnsureVisible();
        // Only network/removable/web resources that are currently unavailable get auto-retried;
        // local-disk resources that vanish were likely moved or deleted, not just disconnected,
        // so retrying them in the background would just be noise (see AddAvailabilityMenuItems).
        _availabilityRetryTimer.Tick += async (s, e) => await RecheckUnavailableResourcesAsync();
        _availabilityRetryTimer.Start();
        EnsureVisibleOnScreen();
    }

    /// <summary>
    /// Applies the persisted window position/size from the last session, if any. Called
    /// right after InitializeComponent (which sets the design-time CenterScreen default)
    /// so a saved position wins when one exists.
    /// </summary>
    private void ApplyPersistedWindowBounds()
    {
        var settings = _appSettingsManager.Load();
        if (settings.WindowWidth is int w && settings.WindowHeight is int h && w > 100 && h > 100)
        {
            this.StartPosition = FormStartPosition.Manual;
            this.Size = new Size(w, h);
            if (settings.WindowLeft is int l && settings.WindowTop is int t)
                this.Location = new Point(l, t);
            if (settings.WindowMaximized)
                this.WindowState = FormWindowState.Maximized;
        }
    }

    private void SaveWindowBounds()
    {
        try
        {
            var settings = _appSettingsManager.Load();
            var bounds = WindowState == FormWindowState.Normal ? this.Bounds : this.RestoreBounds;
            settings.WindowLeft = bounds.Left;
            settings.WindowTop = bounds.Top;
            settings.WindowWidth = bounds.Width;
            settings.WindowHeight = bounds.Height;
            settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            _appSettingsManager.Save(settings);
        }
        catch { /* non-critical */ }
    }

    /// <summary>
    /// Resets the window to a centered position on the primary screen if its current bounds
    /// don't fall on any currently connected screen (e.g. it was last positioned on a second
    /// monitor that's since been unplugged). Applies regardless of the Focus on Run setting —
    /// both a freshly launched instance and an existing instance being refocused go through
    /// this before becoming visible.
    /// </summary>
    private void EnsureVisibleOnScreen()
    {
        if (WindowState != FormWindowState.Normal) return;

        var bounds = this.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds))) return;

        var target = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 750);
        this.Left = target.Left + Math.Max(0, (target.Width - bounds.Width) / 2);
        this.Top = target.Top + Math.Max(0, (target.Height - bounds.Height) / 2);
    }

    /// <summary>
    /// Called when another launch of the app signals us (Focus on Run = Prevent multiple
    /// copies) instead of opening its own window. Brings this window to the foreground,
    /// restoring it first if minimized and repositioning it if it's drifted off-screen.
    /// </summary>
    public void RestoreAndActivate()
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        EnsureVisibleOnScreen();

        Show();
        Activate();
        WindowActivator.ForceToForeground(this.Handle);
    }

    /// <summary>
    /// Sets the window title to "Project Nest Explorer {major.minor.build}", reading the
    /// version from the assembly so it always matches the csproj &lt;Version&gt; with no hardcoding.
    /// </summary>
    private void SetWindowTitle()
    {
        var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                ?? new Version(1, 0, 0);
        this.Text = $"Project Nest Explorer {v.Major}.{v.Minor}.{v.Build}";
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ModernWindowStyler.ApplyRoundedCorners(this.Handle);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        splitMain.SplitterDistance = Math.Max(300, splitMain.Width / 4);
        splitMain.Panel1MinSize = 150;
        splitMain.Panel2MinSize = 150;

        UpdateLicenseUi();
    }

    private void RefreshLicense() =>
        _license = _licenseManager.GetCurrentLicense(_projectManager.Projects);

    private void UpdateLicenseUi()
    {
        switch (_license.State)
        {
            case LicenseState.Free:
                lblStatus.Text = $"Free — {_license.ProjectCount}/{_license.ProjectLimit} projects, " +
                                 $"{_license.LeafNodeCount}/{_license.LeafNodeLimit} references  |  Help > Register";
                break;
            case LicenseState.LimitReached:
                lblStatus.Text = $"Free limit reached ({_license.LeafNodeCount} references, {_license.ProjectCount} projects)  |  Help > Register to unlock";
                break;
            case LicenseState.Licensed:
                lblStatus.Text = $"Licensed to {_license.Email}";
                break;
        }
    }

    /// <summary>
    /// Returns true if the action can proceed. If the free limit would be
    /// breached, shows a prompt and returns false.
    /// </summary>
    private bool CheckLeafLimit(string actionDescription)
    {
        if (_license.State == LicenseState.Licensed) return true;
        RefreshLicense();
        if (_license.LeafNodeCount < _license.LeafNodeLimit) return true;

        var result = MessageBox.Show(
            $"You've reached the free limit of {_license.LeafNodeLimit} folder/web references.\n\n" +
            $"Register Project Nest Explorer to add unlimited references.",
            $"Free Limit Reached — {actionDescription}",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

        if (result == DialogResult.OK) OpenRegistrationDialog();
        return false;
    }

    private bool CheckProjectLimit()
    {
        if (_license.State == LicenseState.Licensed) return true;
        RefreshLicense();
        if (_license.ProjectCount < _license.ProjectLimit) return true;

        var result = MessageBox.Show(
            $"You've reached the free limit of {_license.ProjectLimit} projects.\n\n" +
            $"Register Project Nest Explorer to create unlimited projects.",
            "Free Limit Reached — New Project",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Information);

        if (result == DialogResult.OK) OpenRegistrationDialog();
        return false;
    }

    private const string UpdateCheckUrl =
        "https://raw.githubusercontent.com/bmusical/ProjectExplorer/master/updates/updates.xml";

    /// <param name="silent">
    /// true = only show dialog when an update is available (startup background check).
    /// false = always show result, including "you're up to date" (menu-triggered check).
    /// </param>
    public void CheckForUpdates(bool silent)
    {
        AutoUpdater.AppTitle = "Project Nest Explorer";
        AutoUpdater.RunUpdateAsAdmin = false;
        AutoUpdater.ShowSkipButton = true;
        AutoUpdater.ShowRemindLaterButton = true;
        AutoUpdater.ReportErrors = !silent;   // shows "up to date" dialog when user asks manually
        AutoUpdater.Start(UpdateCheckUrl);
    }

    private void OpenRegistrationDialog()
    {
        using var dlg = new RegistrationDialog(_licenseManager, _license);
        dlg.ShowDialog(this);
        _license = dlg.ResultLicense;
        UpdateLicenseUi();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Set splitter distance after form is shown and fully laid out
        // Calculate a safe value that's always within valid range
        int desiredDistance = 250;
        int minDistance = splitMain.Panel1MinSize;
        int maxDistance = splitMain.Width - splitMain.Panel2MinSize;

        if (maxDistance > minDistance)
        {
            splitMain.SplitterDistance = Math.Clamp(desiredDistance, minDistance, maxDistance);
        }
    }

    // ── Icon Loading ──

    private void LoadDefaultIcons()
    {
        // Real folder icons come from the shell; the conceptual node types
        // (Project / Collection / WebResource / FileReference) get distinct,
        // meaningful glyph icons drawn from the Segoe MDL2 Assets font so each
        // type reads instantly in the tree and list.
        try
        {
            var folderIcon = _shellIconProvider.GetFolderIcon(IconSize.Small);
            imageListSmall.Images.Add("Folder", folderIcon);
            imageListSmall.Images.Add("FolderOpen", _shellIconProvider.GetFolderIcon(IconSize.Small, open: true));
            imageListLarge.Images.Add("Folder", _shellIconProvider.GetFolderIcon(IconSize.Large));
            imageListLarge.Images.Add("FolderOpen", _shellIconProvider.GetFolderIcon(IconSize.Large, open: true));
            imageListExtraLarge.Images.Add("Folder", _shellIconProvider.GetFolderIcon(IconSize.Large));
            imageListExtraLarge.Images.Add("FolderOpen", _shellIconProvider.GetFolderIcon(IconSize.Large, open: true));
        }
        catch
        {
            imageListSmall.Images.Add("Folder", GlyphBitmap("\uE8B7", 16, Color.FromArgb(222, 175, 60)));
            imageListSmall.Images.Add("FolderOpen", GlyphBitmap("\uE838", 16, Color.FromArgb(222, 175, 60)));
            imageListLarge.Images.Add("Folder", GlyphBitmap("\uE8B7", 48, Color.FromArgb(222, 175, 60)));
            imageListLarge.Images.Add("FolderOpen", GlyphBitmap("\uE838", 48, Color.FromArgb(222, 175, 60)));
            imageListExtraLarge.Images.Add("Folder", GlyphBitmap("\uE8B7", 96, Color.FromArgb(222, 175, 60)));
            imageListExtraLarge.Images.Add("FolderOpen", GlyphBitmap("\uE838", 96, Color.FromArgb(222, 175, 60)));
        }

        // Project  = nest/home accent blue   (\uE80F = Home)
        imageListSmall.Images.Add("Project", GlyphBitmap("\uE80F", 16, Color.FromArgb(30, 80, 160)));
        imageListLarge.Images.Add("Project", GlyphBitmap("\uE80F", 48, Color.FromArgb(30, 80, 160)));
        imageListExtraLarge.Images.Add("Project", GlyphBitmap("\uE80F", 96, Color.FromArgb(30, 80, 160)));

        // Collection = library/stack         (\uE8F1 = Library)
        imageListSmall.Images.Add("Collection", GlyphBitmap("\uE8F1", 16, Color.FromArgb(120, 90, 170)));
        imageListLarge.Images.Add("Collection", GlyphBitmap("\uE8F1", 48, Color.FromArgb(120, 90, 170)));
        imageListExtraLarge.Images.Add("Collection", GlyphBitmap("\uE8F1", 96, Color.FromArgb(120, 90, 170)));

        // WebResource = globe (was a confusing security shield) (\uE774 = Globe)
        imageListSmall.Images.Add("WebResource", GlyphBitmap("\uE774", 16, Color.FromArgb(40, 130, 120)));
        imageListLarge.Images.Add("WebResource", GlyphBitmap("\uE774", 48, Color.FromArgb(40, 130, 120)));
        imageListExtraLarge.Images.Add("WebResource", GlyphBitmap("\uE774", 96, Color.FromArgb(40, 130, 120)));

        // FileReference = page/document       (\uE8A5 = Document)
        imageListSmall.Images.Add("FileReference", GlyphBitmap("\uE8A5", 16, Color.FromArgb(90, 100, 110)));
        imageListLarge.Images.Add("FileReference", GlyphBitmap("\uE8A5", 48, Color.FromArgb(90, 100, 110)));
        imageListExtraLarge.Images.Add("FileReference", GlyphBitmap("\uE8A5", 96, Color.FromArgb(90, 100, 110)));
    }

    /// <summary>
    /// Renders a single Segoe MDL2 Assets glyph to a transparent bitmap of the
    /// given size and colour, for use as a node/list icon.
    /// </summary>
    private static Bitmap GlyphBitmap(string glyph, int size, Color color)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        try
        {
            using var font = new Font("Segoe MDL2 Assets", size * 0.72f, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), sf);
        }
        catch
        {
            // Font unavailable (non-Windows) — draw a simple coloured square as a fallback.
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, 2, 2, size - 4, size - 4);
        }
        return bmp;
    }

    /// <summary>
    /// Draws a small, softly beveled "chip" icon — a rounded swatch in
    /// <paramref name="backColor"/> with a centered glyph/text — used for the
    /// toolbar's action buttons so each one visually echoes the context-menu
    /// command it triggers (Explorer / CMD / PowerShell / Copy Path).
    /// </summary>
    private static Bitmap ChipBitmap(string glyph, string fontFamily, int size, Color backColor, Color foreColor)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var rect = new Rectangle(1, 1, size - 3, size - 3);
        int radius = Math.Max(3, size / 5);
        using (var path = RoundedRectPath(rect, radius))
        {
            using (var backBrush = new SolidBrush(backColor))
                g.FillPath(backBrush, path);

            // Raised bevel: a light highlight along the top/left edge and a
            // shadow along the bottom/right, so the chip reads as a small 3D
            // button rather than a flat swatch.
            using var lightPen = new Pen(Color.FromArgb(100, Color.White), 1.25f);
            using var darkPen = new Pen(Color.FromArgb(80, Color.Black), 1.25f);
            g.DrawArc(lightPen, rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
            g.DrawLine(lightPen, rect.X + radius, rect.Y, rect.Right - radius, rect.Y);
            g.DrawArc(darkPen, rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            g.DrawLine(darkPen, rect.X + radius, rect.Bottom, rect.Right - radius, rect.Bottom);
        }

        try
        {
            using var font = new Font(fontFamily, size * 0.46f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(foreColor);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, font, textBrush, new RectangleF(0, 0, size, size), sf);
        }
        catch
        {
            // Requested font unavailable (e.g. non-Windows) — draw a plain dot so the
            // button still has a legible focal point instead of an empty chip.
            using var textBrush = new SolidBrush(foreColor);
            g.FillEllipse(textBrush, size * 0.35f, size * 0.35f, size * 0.3f, size * 0.3f);
        }

        return bmp;
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRectPath(Rectangle rect, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Renders toolbar buttons with a classic raised/sunken 3D bevel on hover
    /// and press, instead of the flat, borderless look of the default system
    /// renderer — buttons stay flat at rest so the toolbar isn't busy.
    /// </summary>
    private sealed class Toolbar3DRenderer : ToolStripSystemRenderer
    {
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item is not ToolStripButton button || !button.Enabled || (!button.Selected && !button.Pressed && !button.Checked))
            {
                base.OnRenderButtonBackground(e);
                return;
            }

            var bounds = new Rectangle(Point.Empty, button.Size);
            var style = button.Pressed || button.Checked ? Border3DStyle.Sunken : Border3DStyle.Raised;
            ControlPaint.DrawBorder3D(e.Graphics, bounds, style);
        }
    }

    // ── Tree View Initialization ──

    private void InitializeTreeView()
    {
        treeView.BeginUpdate();
        treeView.Nodes.Clear();

        var rootNode = new TreeNode("Projects")
        {
            Tag = TagProjectsRoot,
            ImageIndex = GetImageIndex("Project"),
            SelectedImageIndex = GetImageIndex("Project")
        };
        treeView.Nodes.Add(rootNode);

        // Load all projects
        foreach (var project in _projectManager.Projects)
        {
            AddProjectNode(rootNode, project);
        }

        rootNode.Expand();
        treeView.EndUpdate();

        treeView.AllowDrop = true;
        treeView.ItemDrag -= TreeView_ItemDrag;
        treeView.DragEnter -= TreeView_DragEnter;
        treeView.DragOver -= TreeView_DragOver;
        treeView.DragDrop -= TreeView_DragDrop;
        treeView.DragLeave -= TreeView_DragLeave;
        treeView.ItemDrag += TreeView_ItemDrag;
        treeView.DragEnter += TreeView_DragEnter;
        treeView.DragOver += TreeView_DragOver;
        treeView.DragDrop += TreeView_DragDrop;
        treeView.DragLeave += TreeView_DragLeave;
    }

    private void AddProjectNode(TreeNode parent, Project project)
    {
        var node = new TreeNode(project.Name)
        {
            Tag = TagProject + project.Id,
            ToolTipText = project.Description ?? project.Name,
            ImageIndex = GetImageIndex("Project"),
            SelectedImageIndex = GetImageIndex("Project")
        };
        parent.Nodes.Add(node);

        // Add children (Collections and FolderReferences)
        foreach (var child in project.Children.OrderBy(c => c.SortOrder))
        {
            AddChildNode(node, child, project);
        }
    }

    private void AddChildNode(TreeNode parent, ProjectChild child, Project project)
    {
        if (child is Collection collection)
        {
            var collNode = new TreeNode(collection.Name)
            {
                Tag = TagCollection + $"{project.Id}:{collection.Id}",
                ToolTipText = collection.Description ?? collection.Name,
                ImageIndex = GetImageIndex("Collection"),
                SelectedImageIndex = GetImageIndex("Collection")
            };
            parent.Nodes.Add(collNode);

            foreach (var subChild in collection.Children.OrderBy(c => c.SortOrder))
            {
                AddChildNode(collNode, subChild, project);
            }
        }
        else if (child is FolderReference folderRef)
        {
            var refNode = new TreeNode(folderRef.EffectiveName)
            {
                Tag = TagFolderRef + $"{project.Id}:{folderRef.Id}",
                ToolTipText = BuildAvailabilityTooltip(folderRef),
                ImageIndex = GetImageIndex("Folder"),
                SelectedImageIndex = GetImageIndex("FolderOpen")
            };
            parent.Nodes.Add(refNode);
            ApplyAvailabilityStyle(refNode, folderRef.Id);
            EnsureAvailabilityChecked(folderRef);

            // Add a dummy node so the + expander shows, then we lazy-load real subfolders
            if (Directory.Exists(folderRef.RealPath))
            {
                refNode.Nodes.Add(new TreeNode("...") { Tag = "Dummy" });
            }
        }
        else if (child is WebResource webResource)
        {
            var webNode = new TreeNode(webResource.EffectiveName)
            {
                Tag = TagWebResource + $"{project.Id}:{webResource.Id}",
                ToolTipText = BuildAvailabilityTooltip(webResource),
                ImageIndex = GetImageIndex("WebResource"),
                SelectedImageIndex = GetImageIndex("WebResource")
            };
            parent.Nodes.Add(webNode);
            ApplyAvailabilityStyle(webNode, webResource.Id);
            EnsureAvailabilityChecked(webResource);
        }
        else if (child is FileReference fileRef)
        {
            var fileNode = new TreeNode(fileRef.EffectiveName)
            {
                Tag = TagFileRef + $"{project.Id}:{fileRef.Id}",
                ToolTipText = BuildAvailabilityTooltip(fileRef),
                ImageIndex = GetFileRefImageIndex(fileRef),
                SelectedImageIndex = GetFileRefImageIndex(fileRef)
            };
            parent.Nodes.Add(fileNode);
            ApplyAvailabilityStyle(fileNode, fileRef.Id);
            EnsureAvailabilityChecked(fileRef);
        }
    }

    /// <summary>
    /// Returns an image index for a FileReference, preferring the real shell icon
    /// for the file's extension and falling back to the generic FileReference glyph.
    /// </summary>
    private int GetFileRefImageIndex(FileReference fileRef)
    {
        var ext = fileRef.Extension;
        if (!string.IsNullOrEmpty(ext))
        {
            var iconKey = $"ext_{ext}";
            if (!imageListSmall.Images.ContainsKey(iconKey))
            {
                try
                {
                    imageListSmall.Images.Add(iconKey, _shellIconProvider.GetIconByExtension(ext, IconSize.Small));
                    imageListLarge.Images.Add(iconKey, _shellIconProvider.GetIconByExtension(ext, IconSize.Large));
                    imageListExtraLarge.Images.Add(iconKey, _shellIconProvider.GetIconByExtension(ext, IconSize.Large));
                }
                catch { /* fall through to generic glyph */ }
            }
            if (imageListSmall.Images.ContainsKey(iconKey))
                return imageListSmall.Images.IndexOfKey(iconKey);
        }
        return GetImageIndex("FileReference");
    }

    private int GetImageIndex(string key)
    {
        return imageListSmall.Images.IndexOfKey(key);
    }

    // ── Resource Availability ──

    /// <summary>
    /// Kicks off a background availability check the first time a FolderReference/FileReference/
    /// WebResource is rendered this session. No-ops for anything already checked or in flight, and
    /// for non-leaf types (Project/Collection have no availability concept).
    /// </summary>
    private void EnsureAvailabilityChecked(ProjectChild child)
    {
        if (child is not (FolderReference or FileReference or WebResource)) return;
        if (_availabilityCache.ContainsKey(child.Id)) return;
        _ = CheckAvailabilityAsync(child);
    }

    /// <summary>Forces a fresh check regardless of what's cached, e.g. from "Check Availability Now".</summary>
    private Task ForceCheckAvailabilityAsync(ProjectChild child)
    {
        _availabilityCache.Remove(child.Id);
        return CheckAvailabilityAsync(child);
    }

    private async Task CheckAvailabilityAsync(ProjectChild child)
    {
        if (!_availabilityChecksInFlight.Add(child.Id)) return;
        try
        {
            AvailabilityCheckResult? result = child switch
            {
                FolderReference fr => await ResourceAvailabilityChecker.CheckFolderAsync(fr.RealPath),
                FileReference file => await ResourceAvailabilityChecker.CheckFileAsync(file.FilePath),
                WebResource wr => await ResourceAvailabilityChecker.CheckWebResourceAsync(wr.Url, _availabilityHttpClient),
                _ => null
            };
            if (result == null) return;

            _availabilityCache[child.Id] = result.Value;
            UpdateAvailabilityVisuals(child);
        }
        catch
        {
            // Best-effort background check — leave the resource's availability as whatever it was
            // (or unknown) rather than letting an unexpected failure surface to the user.
        }
        finally
        {
            _availabilityChecksInFlight.Remove(child.Id);
        }
    }

    /// <summary>
    /// Re-checks every currently-unavailable network/removable/web resource (the kinds that might
    /// just be temporarily disconnected), skipping ones the user asked to stop auto-retrying via
    /// the "Stop Auto-Retry" context menu action. Local-disk resources are never retried here —
    /// a missing local file was moved or deleted, not disconnected, so polling it is pointless.
    /// Also retries Web resources cached as <see cref="AvailabilityStatus.Unknown"/> (a prior check
    /// that failed to connect at all, rather than getting back a real HTTP error) so a transient
    /// blip doesn't get stuck unchecked forever — it renders normally either way; only a confirmed
    /// HTTP error status renders as unavailable.
    /// </summary>
    private async Task RecheckUnavailableResourcesAsync()
    {
        var candidateIds = _availabilityCache
            .Where(kv => kv.Value.LocationKind != ResourceLocationKind.LocalDisk &&
                         (kv.Value.Status == AvailabilityStatus.Unavailable || kv.Value.Status == AvailabilityStatus.Unknown))
            .Select(kv => kv.Key)
            .ToList();

        foreach (var id in candidateIds)
        {
            var child = FindChildAnywhere(id);
            if (child == null)
            {
                _availabilityCache.Remove(id);
                continue;
            }
            if (child.Metadata.TryGetValue(ResourceAvailabilityChecker.SuppressAutoRetryMetadataKey, out var suppressed) && suppressed == "true")
                continue;

            await CheckAvailabilityAsync(child);
        }
    }

    /// <summary>Finds a child by Id across every loaded project, not just the currently displayed one.</summary>
    private ProjectChild? FindChildAnywhere(Guid childId)
    {
        foreach (var project in _projectManager.Projects)
        {
            var parentList = project.FindParentList(childId);
            var child = parentList?.FirstOrDefault(c => c.Id == childId);
            if (child != null) return child;
        }
        return null;
    }

    /// <summary>Updates an already-rendered TreeNode/ListViewItem's style in place, without rebuilding the tree.</summary>
    private void UpdateAvailabilityVisuals(ProjectChild child)
    {
        var node = FindTreeNodeByChildId(treeView.Nodes, child.Id);
        if (node != null)
        {
            node.ToolTipText = BuildAvailabilityTooltip(child);
            ApplyAvailabilityStyle(node, child.Id);
        }

        foreach (ListViewItem item in listView.Items)
        {
            if (item.Tag is string tag && GetChildIdFromTag(tag) == child.Id)
            {
                ApplyAvailabilityStyle(item, child.Id);
                break;
            }
        }
    }

    private static TreeNode? FindTreeNodeByChildId(TreeNodeCollection nodes, Guid childId)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is string tag && GetChildIdFromTag(tag) == childId)
                return node;
            var found = FindTreeNodeByChildId(node.Nodes, childId);
            if (found != null) return found;
        }
        return null;
    }

    private void ApplyAvailabilityStyle(TreeNode node, Guid childId)
    {
        var unavailable = _availabilityCache.TryGetValue(childId, out var result) && result.Status == AvailabilityStatus.Unavailable;
        node.ForeColor = unavailable ? Color.Gray : Color.Empty;
        node.NodeFont = unavailable ? (_unavailableTreeFont ??= new Font(treeView.Font, FontStyle.Strikeout)) : null;
    }

    private void ApplyAvailabilityStyle(ListViewItem item, Guid childId)
    {
        var unavailable = _availabilityCache.TryGetValue(childId, out var result) && result.Status == AvailabilityStatus.Unavailable;
        item.ForeColor = unavailable ? Color.Gray : listView.ForeColor;
        item.Font = unavailable ? (_unavailableListFont ??= new Font(listView.Font, FontStyle.Strikeout)) : listView.Font;
    }

    /// <summary>Builds a tree node tooltip: the item's normal description/path, plus an availability note when unavailable.</summary>
    private string BuildAvailabilityTooltip(ProjectChild child)
    {
        var baseText = child switch
        {
            FolderReference fr => fr.Description ?? fr.RealPath,
            FileReference file => file.Description ?? file.FilePath,
            WebResource wr => wr.Description ?? wr.Url,
            _ => ""
        };

        if (!_availabilityCache.TryGetValue(child.Id, out var result) || result.Status != AvailabilityStatus.Unavailable)
            return baseText;

        return baseText + Environment.NewLine + Environment.NewLine + DescribeUnavailable(result.LocationKind);
    }

    private static string DescribeUnavailable(ResourceLocationKind kind) => kind switch
    {
        ResourceLocationKind.LocalDisk =>
            "⚠ Not found. It may have been moved, renamed, or deleted — use \"Locate...\" to relink it.",
        ResourceLocationKind.NetworkOrRemovable =>
            "⚠ Not reachable right now. This may be a temporarily disconnected network or removable drive — Project Nest Explorer will keep checking automatically.",
        ResourceLocationKind.Web =>
            "⚠ The site returned an error (e.g. a 404) the last time it was checked — Project Nest Explorer will keep checking automatically, and this clears as soon as it loads again.",
        _ => "⚠ Could not be verified."
    };

    private static string DescribeUnavailableShort(ResourceLocationKind kind) => kind switch
    {
        ResourceLocationKind.LocalDisk => "moved or deleted?",
        ResourceLocationKind.NetworkOrRemovable => "network/removable drive unreachable",
        ResourceLocationKind.Web => "site returned an error",
        _ => "could not verify"
    };

    // ── Tree View Events ──

    private void TreeView_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node == null) return;

        var tag = e.Node.Tag?.ToString() ?? "";

        if (tag.StartsWith(TagFileRef))
        {
            var parts = tag.Substring(TagFileRef.Length).Split(':');
            var projectId = Guid.Parse(parts[0]);
            _currentProject = _projectManager.GetProject(projectId);
            var fileRefId = Guid.Parse(parts[1]);
            var fileRef = FindFileRef(_currentProject, fileRefId);
            if (fileRef != null)
            {
                var dir = Path.GetDirectoryName(fileRef.FilePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    _currentPath = dir;
                else
                    _currentPath = string.Empty;
                HideWebResourcePreview();
                ShowFileReferencePreview(fileRef);
            }

            UpdateAddressBar();
            UpdateStatusBar();
            UpdateToolbarButtons();
            return;
        }

        if (tag.StartsWith(TagWebResource))
        {
            var parts = tag.Substring(TagWebResource.Length).Split(':');
            var projectId = Guid.Parse(parts[0]);
            _currentProject = _projectManager.GetProject(projectId);
            var resourceId = Guid.Parse(parts[1]);
            var webResource = FindWebResource(_currentProject, resourceId);
            if (webResource != null)
            {
                _currentPath = string.Empty;
                HideFileReferencePreview();
                ShowWebResourcePreview(webResource);
            }

            UpdateAddressBar();
            UpdateStatusBar();
            UpdateToolbarButtons();
            return;
        }

        HideFileReferencePreview();
        HideWebResourcePreview();

        listView.BeginUpdate();
        listView.Items.Clear();

        if (tag == TagProjectsRoot)
        {
            PopulateProjectList();
        }
        else if (tag.StartsWith(TagProject))
        {
            var projectId = Guid.Parse(tag.Substring(TagProject.Length));
            _currentProject = _projectManager.GetProject(projectId);
            PopulateProjectContents();
        }
        else if (tag.StartsWith(TagCollection))
        {
            var parts = tag.Substring(TagCollection.Length).Split(':');
            var projectId = Guid.Parse(parts[0]);
            _currentProject = _projectManager.GetProject(projectId);
            var collectionId = Guid.Parse(parts[1]);
            PopulateCollectionContents(collectionId);
        }
        else if (tag.StartsWith(TagFolderRef))
        {
            var parts = tag.Substring(TagFolderRef.Length).Split(':');
            var projectId = Guid.Parse(parts[0]);
            _currentProject = _projectManager.GetProject(projectId);
            var folderRefId = Guid.Parse(parts[1]);
            var folderRef = FindFolderRef(_currentProject, folderRefId);
            if (folderRef != null && Directory.Exists(folderRef.RealPath))
            {
                _currentPath = folderRef.RealPath;
                PopulateFileList(folderRef.RealPath);
            }
        }
        else if (tag.StartsWith(TagRealFolder))
        {
            var path = tag.Substring(TagRealFolder.Length);
            _currentPath = path;
            PopulateFileList(path);
        }

        listView.EndUpdate();
        UpdateAddressBar();
        UpdateStatusBar();
        UpdateToolbarButtons();
    }

    /// <summary>
    /// Shows the given FileReference's inline preview in place of the ListView:
    /// renders the file's content when we can (image/text), and always leaves
    /// Open/Properties available regardless of whether the format is supported.
    /// </summary>
    private void ShowFileReferencePreview(FileReference fileRef)
    {
        _currentPreviewFileRef = fileRef;
        listView.Visible = false;
        filePreviewPanel.Visible = true;

        var icon = string.IsNullOrEmpty(fileRef.Extension)
            ? _shellIconProvider.GetFileIcon(fileRef.FilePath, IconSize.Jumbo)
            : _shellIconProvider.GetIconByExtension(fileRef.Extension, IconSize.Jumbo);
        filePreviewPanel.ShowFile(fileRef.FilePath, fileRef.Description, icon);
    }

    private void HideFileReferencePreview()
    {
        if (_currentPreviewFileRef == null) return;
        _currentPreviewFileRef = null;
        filePreviewPanel.Visible = false;
        listView.Visible = true;
    }

    private void FilePreviewPanel_OpenRequested(object? sender, EventArgs e)
    {
        if (_currentPreviewFileRef != null) OpenFileReference(_currentPreviewFileRef);
    }

    private void FilePreviewPanel_PropertiesRequested(object? sender, EventArgs e)
    {
        if (_currentPreviewFileRef != null && File.Exists(_currentPreviewFileRef.FilePath))
            _shellPropertiesProvider.ShowPropertiesDialog(_currentPreviewFileRef.FilePath, this.Handle);
    }

    /// <summary>
    /// Shows the given WebResource's inline preview in place of the ListView:
    /// navigates the embedded browser (WebResourcePreviewPanel) to its URL.
    /// "Open in External Browser" is always available via the event below.
    /// </summary>
    private void ShowWebResourcePreview(WebResource webResource)
    {
        _currentPreviewWebResource = webResource;
        listView.Visible = false;
        webResourcePreviewPanel.Visible = true;

        webResourcePreviewPanel.ShowWebResource(webResource.Url, webResource.EffectiveName, imageListExtraLarge.Images["WebResource"]);
    }

    private void HideWebResourcePreview()
    {
        if (_currentPreviewWebResource == null) return;
        _currentPreviewWebResource = null;
        webResourcePreviewPanel.Visible = false;
        listView.Visible = true;
    }

    private void WebResourcePreviewPanel_OpenExternalRequested(object? sender, EventArgs e)
    {
        if (_currentPreviewWebResource != null) LaunchWebResource(_currentPreviewWebResource);
    }

    private void TreeView_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        if (e.Node == null) return;
        var tag = e.Node.Tag?.ToString() ?? "";

        // Lazy-load real subfolders for FolderReference nodes
        if (tag.StartsWith(TagFolderRef) || tag.StartsWith(TagRealFolder))
        {
            string path;
            if (tag.StartsWith(TagFolderRef))
            {
                var parts = tag.Substring(TagFolderRef.Length).Split(':');
                var projectId = Guid.Parse(parts[0]);
                var folderRefId = Guid.Parse(parts[1]);
                var project = _projectManager.GetProject(projectId);
                var folderRef = FindFolderRef(project, folderRefId);
                path = folderRef?.RealPath ?? "";
            }
            else
            {
                path = tag.Substring(TagRealFolder.Length);
            }

            if (Directory.Exists(path))
            {
                e.Node.Nodes.Clear();
                try
                {
                    foreach (var dir in Directory.GetDirectories(path))
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        var subNode = new TreeNode(dirInfo.Name)
                        {
                            Tag = TagRealFolder + dir,
                            ImageIndex = GetImageIndex("Folder"),
                            SelectedImageIndex = GetImageIndex("FolderOpen")
                        };
                        // Add dummy for further expansion
                        try
                        {
                            if (Directory.GetDirectories(dir).Length > 0)
                                subNode.Nodes.Add(new TreeNode("...") { Tag = "Dummy" });
                        }
                        catch { /* access denied */ }

                        e.Node.Nodes.Add(subNode);
                    }
                }
                catch { /* access denied */ }
            }
        }
    }

    private void TreeView_NodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        if (e.Button == MouseButtons.Right && e.Node != null)
        {
            treeView.SelectedNode = e.Node;
            ShowTreeViewContextMenu(e.Node, e.Location);
        }
    }

    private void TreeView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F2 && treeView.SelectedNode != null)
        {
            var tag = treeView.SelectedNode.Tag?.ToString() ?? "";
            if (tag.StartsWith(TagProject) || tag.StartsWith(TagCollection))
            {
                treeView.SelectedNode.BeginEdit();
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// F2 rename from the ListView. Unlike the TreeView (<see cref="TreeView_KeyDown"/>), the
    /// ListView has no inline label editing, so this routes through the same dialog-based rename
    /// the right-click "Rename" menu item already uses (<see cref="RenameViaDialog"/>) — e.g. when
    /// renaming a top-level Project while its row is selected in the ListView rather than the tree.
    /// </summary>
    private void ListView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.F2 || listView.SelectedItems.Count != 1) return;

        var tag = listView.SelectedItems[0].Tag?.ToString() ?? "";
        if (tag.StartsWith(TagProject) || tag.StartsWith(TagCollection))
        {
            RenameViaDialog(tag);
            e.Handled = true;
        }
    }

    private void TreeView_AfterLabelEdit(object? sender, NodeLabelEditEventArgs e)
    {
        // Always cancel the built-in label update; we manage node text manually after the async save.
        e.CancelEdit = true;
        if (e.Label == null || e.Node == null) return; // Escape pressed — no change
        var newName = e.Label.Trim();
        if (string.IsNullOrEmpty(newName)) return;
        _ = PerformRenameAsync(e.Node, e.Node.Tag?.ToString() ?? "", newName);
    }

    private async Task PerformRenameAsync(TreeNode node, string tag, string newName)
    {
        try
        {
            if (tag.StartsWith(TagProject))
            {
                var projectId = Guid.Parse(tag.Substring(TagProject.Length));
                await _projectManager.RenameProjectAsync(projectId, newName);
                node.Text = newName;
            }
            else if (tag.StartsWith(TagCollection))
            {
                var parts = tag.Substring(TagCollection.Length).Split(':');
                var projectId = Guid.Parse(parts[0]);
                var collectionId = Guid.Parse(parts[1]);
                await _projectManager.RenameCollectionAsync(projectId, collectionId, newName);
                node.Text = newName;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Rename failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── List View Population ──

    private void PopulateProjectList()
    {
        listView.Columns[3].Text = "Date Modified";
        foreach (var project in _projectManager.Projects)
        {
            var item = new ListViewItem(project.Name, "Project")
            {
                Tag = TagProject + project.Id
            };
            item.SubItems.Add("");
            item.SubItems.Add("Project");
            item.SubItems.Add(project.Modified.ToString("g"));
            item.SubItems.Add(project.Description ?? "");
            listView.Items.Add(item);
        }
    }

    private void PopulateProjectContents()
    {
        if (_currentProject == null) return;
        listView.Columns[3].Text = "URL";

        foreach (var child in _currentProject.Children.OrderBy(c => c.SortOrder))
        {
            if (child is Collection coll)
            {
                var item = new ListViewItem(coll.Name, "Collection")
                {
                    Tag = TagCollection + $"{_currentProject.Id}:{coll.Id}"
                };
                item.SubItems.Add("");
                item.SubItems.Add("Collection");
                item.SubItems.Add("");
                item.SubItems.Add(coll.Description ?? "");
                listView.Items.Add(item);
            }
            else if (child is FolderReference fr)
            {
                var item = new ListViewItem(fr.EffectiveName, "Folder")
                {
                    Tag = TagFolderRef + $"{_currentProject.Id}:{fr.Id}"
                };
                item.SubItems.Add("");
                item.SubItems.Add("Folder Reference");
                item.SubItems.Add("");
                item.SubItems.Add(fr.Description ?? "");
                listView.Items.Add(item);
                ApplyAvailabilityStyle(item, fr.Id);
                EnsureAvailabilityChecked(fr);
            }
            else if (child is WebResource wr)
            {
                var item = new ListViewItem(wr.EffectiveName, "WebResource")
                {
                    Tag = TagWebResource + $"{_currentProject.Id}:{wr.Id}"
                };
                item.SubItems.Add("");
                item.SubItems.Add("Web Resource");
                item.SubItems.Add(wr.Url);
                item.SubItems.Add(wr.Description ?? "");
                listView.Items.Add(item);
                ApplyAvailabilityStyle(item, wr.Id);
                EnsureAvailabilityChecked(wr);
            }
            else if (child is FileReference fileRef)
            {
                var item = new ListViewItem(fileRef.EffectiveName, GetFileRefListImageKey(fileRef))
                {
                    Tag = TagFileRef + $"{_currentProject.Id}:{fileRef.Id}"
                };
                item.SubItems.Add("");
                item.SubItems.Add("File");
                item.SubItems.Add(fileRef.FilePath);
                item.SubItems.Add(fileRef.Description ?? "");
                listView.Items.Add(item);
                ApplyAvailabilityStyle(item, fileRef.Id);
                EnsureAvailabilityChecked(fileRef);
            }
        }
    }

    private void PopulateCollectionContents(Guid collectionId)
    {
        if (_currentProject == null) return;
        listView.Columns[3].Text = "URL";
        var collection = _currentProject.FindCollection(collectionId);
        if (collection == null) return;

        foreach (var child in collection.Children.OrderBy(c => c.SortOrder))
        {
            if (child is Collection coll)
            {
                var item = new ListViewItem(coll.Name, "Collection")
                {
                    Tag = TagCollection + $"{_currentProject.Id}:{coll.Id}"
                };
                item.SubItems.Add("");
                item.SubItems.Add("Collection");
                item.SubItems.Add("");
                item.SubItems.Add(coll.Description ?? "");
                listView.Items.Add(item);
            }
            else if (child is FolderReference fr)
            {
                var item = new ListViewItem(fr.EffectiveName, "Folder")
                {
                    Tag = TagFolderRef + $"{_currentProject.Id}:{fr.Id}"
                };
                item.SubItems.Add("");
                item.SubItems.Add("Folder Reference");
                item.SubItems.Add("");
                item.SubItems.Add(fr.Description ?? "");
                listView.Items.Add(item);
                ApplyAvailabilityStyle(item, fr.Id);
                EnsureAvailabilityChecked(fr);
            }
            else if (child is WebResource wr)
            {
                var item = new ListViewItem(wr.EffectiveName, "WebResource")
                {
                    Tag = TagWebResource + $"{_currentProject.Id}:{wr.Id}"
                };
                item.SubItems.Add("");
                item.SubItems.Add("Web Resource");
                item.SubItems.Add(wr.Url);
                item.SubItems.Add(wr.Description ?? "");
                listView.Items.Add(item);
                ApplyAvailabilityStyle(item, wr.Id);
                EnsureAvailabilityChecked(wr);
            }
            else if (child is FileReference fileRef)
            {
                var item = new ListViewItem(fileRef.EffectiveName, GetFileRefListImageKey(fileRef))
                {
                    Tag = TagFileRef + $"{_currentProject.Id}:{fileRef.Id}"
                };
                item.SubItems.Add("");
                item.SubItems.Add("File");
                item.SubItems.Add(fileRef.FilePath);
                item.SubItems.Add(fileRef.Description ?? "");
                listView.Items.Add(item);
                ApplyAvailabilityStyle(item, fileRef.Id);
                EnsureAvailabilityChecked(fileRef);
            }
        }
    }

    private void PopulateFileList(string path)
    {
        if (!Directory.Exists(path)) return;
        listView.Columns[3].Text = "Date Modified";

        try
        {
            // Add subdirectories
            foreach (var dir in Directory.GetDirectories(path))
            {
                var dirInfo = new DirectoryInfo(dir);
                var item = new ListViewItem(dirInfo.Name, "Folder")
                {
                    Tag = TagRealFolder + dir
                };
                item.SubItems.Add("");
                item.SubItems.Add("File folder");
                item.SubItems.Add(dirInfo.LastWriteTime.ToString("g"));
                listView.Items.Add(item);
            }

            // Add files
            foreach (var file in Directory.GetFiles(path))
            {
                var fileInfo = new FileInfo(file);
                var ext = fileInfo.Extension.ToLowerInvariant();

                // Try to get the file's shell icon
                var iconKey = $"ext_{ext}";
                if (!imageListSmall.Images.ContainsKey(iconKey))
                {
                    try
                    {
                        var icon = _shellIconProvider.GetIconByExtension(ext, IconSize.Small);
                        imageListSmall.Images.Add(iconKey, icon);
                        var largeIcon = _shellIconProvider.GetIconByExtension(ext, IconSize.Large);
                        imageListLarge.Images.Add(iconKey, largeIcon);
                        imageListExtraLarge.Images.Add(iconKey, _shellIconProvider.GetIconByExtension(ext, IconSize.Large));
                    }
                    catch
                    {
                        iconKey = "Folder"; // fallback
                    }
                }

                var item = new ListViewItem(fileInfo.Name, iconKey)
                {
                    Tag = "File:" + file
                };
                item.SubItems.Add(FormatFileSize(fileInfo.Length));
                item.SubItems.Add(GetFileTypeDescription(ext));
                item.SubItems.Add(fileInfo.LastWriteTime.ToString("g"));
                listView.Items.Add(item);

                // For image files, request a real thumbnail in the background.
                // The item keeps its file-type icon for compact views; the
                // thumbnail is used only in Large/Extra Large views (Option B).
                if (ImageFileHelper.IsImageExtension(ext))
                    QueueThumbnail(item, file, iconKey);
            }
        }
        catch (UnauthorizedAccessException)
        {
            listView.Items.Add(new ListViewItem("(Access denied)") { ForeColor = Color.Gray });
        }
    }

    /// <summary>
    /// Loads a real image thumbnail off the UI thread and, once available,
    /// assigns it to the given ListView item's Large Icon. Falls back silently
    /// to the existing extension icon when no thumbnail can be produced.
    /// </summary>
    private void QueueThumbnail(ListViewItem item, string filePath, string iconKey)
    {
        // Option B (Windows Explorer behaviour): image files keep their
        // file-type ICON in compact views (Small Icons / List / Details) and
        // show the picture THUMBNAIL only in Large / Extra Large views.
        //
        // We therefore add the thumbnail only to the large image lists, record
        // both keys for the item, and switch the item's ImageKey between them
        // in SetViewMode. The item starts on its icon key (set at creation).
        var requestSize = imageListExtraLarge.ImageSize;
        var thumbKey = "thumb:" + filePath;

        System.Threading.Tasks.Task.Run(() =>
        {
            System.Drawing.Bitmap? bmp = null;
            try { bmp = _shellThumbnailProvider.GetThumbnail(filePath, requestSize); }
            catch { bmp = null; }
            if (bmp == null) return;

            if (IsDisposed || listView.IsDisposed) { bmp.Dispose(); return; }

            try
            {
                BeginInvoke(() =>
                {
                    try
                    {
                        if (IsDisposed || listView.IsDisposed || item.ListView == null)
                        {
                            bmp.Dispose();
                            return;
                        }

                        // Thumbnails live only in the large image lists.
                        AddThumbToList(imageListLarge, thumbKey, bmp);
                        AddThumbToList(imageListExtraLarge, thumbKey, bmp);

                        // Remember both keys so view switches can toggle them.
                        _imageItemKeys[item] = (iconKey, thumbKey);

                        // If we're currently in a large view, show the thumbnail
                        // now; otherwise leave the icon in place.
                        if (IsLargeView(_currentView))
                            item.ImageKey = thumbKey;

                        bmp.Dispose();
                    }
                    catch { bmp.Dispose(); }
                });
            }
            catch { bmp.Dispose(); }
        });
    }

    /// <summary>True for the icon views that should display picture thumbnails.</summary>
    private static bool IsLargeView(AppView view) =>
        view == AppView.LargeIcon || view == AppView.ExtraLargeIcon;

    /// <summary>
    /// Opens a terminal (Command Prompt or PowerShell) with its working
    /// directory set to <paramref name="folderPath"/>. Prefers PowerShell 7
    /// (pwsh.exe) when requested and available, falling back to Windows
    /// PowerShell. Shows a friendly message if the folder no longer exists.
    /// </summary>
    private void LaunchTerminal(string folderPath, bool usePowerShell)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            MessageBox.Show(
                $"The folder could not be found:\n{folderPath}\n\nIt may have been moved, renamed, or deleted.",
                "Folder Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var fileName = usePowerShell ? "powershell.exe" : "cmd.exe";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = folderPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open {(usePowerShell ? "PowerShell" : "Command Prompt")}:\n{ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Adds <paramref name="source"/> to <paramref name="list"/> under
    /// <paramref name="key"/>, scaled to the list's image size. No-op if the
    /// key already exists.
    /// </summary>
    private static void AddThumbToList(ImageList list, string key, System.Drawing.Bitmap source)
    {
        if (list.Images.ContainsKey(key)) return;

        var target = list.ImageSize;
        var scaled = new System.Drawing.Bitmap(target.Width, target.Height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = System.Drawing.Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.Clear(System.Drawing.Color.Transparent);

            // Preserve aspect ratio, centered ("fit").
            var ratio = Math.Min((float)target.Width / source.Width, (float)target.Height / source.Height);
            var w = Math.Max(1, (int)(source.Width * ratio));
            var h = Math.Max(1, (int)(source.Height * ratio));
            var x = (target.Width - w) / 2;
            var y = (target.Height - h) / 2;
            g.DrawImage(source, x, y, w, h);
        }
        list.Images.Add(key, scaled);
    }

    // ── Navigation ──

    private void BtnBack_Click(object? sender, EventArgs e)
    {
        if (_backStack.Count > 0)
        {
            _forwardStack.Push(_currentPath);
            var prev = _backStack.Pop();
            NavigateToPath(prev);
        }
    }

    private void BtnForward_Click(object? sender, EventArgs e)
    {
        if (_forwardStack.Count > 0)
        {
            _backStack.Push(_currentPath);
            var next = _forwardStack.Pop();
            NavigateToPath(next);
        }
    }

    private void BtnUp_Click(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentPath))
        {
            var parent = Path.GetDirectoryName(_currentPath);
            if (parent != null)
            {
                _backStack.Push(_currentPath);
                _forwardStack.Clear();
                NavigateToPath(parent);
            }
        }
    }

    private void BtnOpenExplorer_Click(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentPath) && Directory.Exists(_currentPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_currentPath) { UseShellExecute = true });
        }
    }

    private void BtnOpenCmd_Click(object? sender, EventArgs e) => LaunchTerminal(_currentPath, usePowerShell: false);

    private void BtnOpenPowerShell_Click(object? sender, EventArgs e) => LaunchTerminal(_currentPath, usePowerShell: true);

    private void BtnCopyPath_Click(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentPath) && Directory.Exists(_currentPath))
        {
            Clipboard.SetText(_currentPath);
        }
    }

    private void AddressBar_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            var path = txtAddress.Text.Trim();
            if (Directory.Exists(path))
            {
                _backStack.Push(_currentPath);
                _forwardStack.Clear();
                NavigateToPath(path);
            }
            e.Handled = true;
        }
    }

    private void NavigateToPath(string path)
    {
        HideFileReferencePreview();
        HideWebResourcePreview();
        _currentPath = path;
        listView.BeginUpdate();
        listView.Items.Clear();
        _imageItemKeys.Clear();
        PopulateFileList(path);
        listView.EndUpdate();
        UpdateAddressBar();
        UpdateStatusBar();
        UpdateToolbarButtons();
    }

    private void UpdateAddressBar()
    {
        txtAddress.Text = _currentPreviewWebResource != null ? _currentPreviewWebResource.Url : _currentPath;
    }

    private void UpdateStatusBar()
    {
        if (_currentPreviewFileRef != null)
        {
            lblStatus.Text = $"{_currentPreviewFileRef.EffectiveName}  |  {_currentPreviewFileRef.FilePath}";
            return;
        }

        if (_currentPreviewWebResource != null)
        {
            lblStatus.Text = $"{_currentPreviewWebResource.EffectiveName}  |  {_currentPreviewWebResource.Url}";
            return;
        }

        var count = listView.Items.Count;
        lblStatus.Text = count == 1 ? "1 item" : $"{count} items";
        if (!string.IsNullOrEmpty(_currentPath))
            lblStatus.Text += $"  |  {_currentPath}";
    }

    private void UpdateToolbarButtons()
    {
        // Enable Explorer/CMD/PowerShell/Copy Path buttons when viewing a real folder path
        var onRealFolder = !string.IsNullOrEmpty(_currentPath) && Directory.Exists(_currentPath);
        btnOpenExplorer.Enabled = onRealFolder;
        btnOpenCmd.Enabled = onRealFolder;
        btnOpenPowerShell.Enabled = onRealFolder;
        btnCopyPath.Enabled = onRealFolder;

        // Update navigation buttons
        btnBack.Enabled = _backStack.Count > 0;
        btnForward.Enabled = _forwardStack.Count > 0;
    }

    // ── List View Events ──

    private void ListView_DoubleClick(object? sender, EventArgs e)
    {
        if (listView.SelectedItems.Count == 0) return;

        var item = listView.SelectedItems[0];
        var tag = item.Tag?.ToString() ?? "";

        if (tag.StartsWith(TagProject) || tag.StartsWith(TagCollection) || tag.StartsWith(TagFolderRef))
        {
            // Find and select the corresponding tree node
            SelectTreeNodeByTag(tag);
        }
        else if (tag.StartsWith(TagWebResource))
        {
            // Select the corresponding tree node so it shows in the browser preview,
            // same as Project/Collection/FolderRef above; use the panel's
            // "Open in External Browser" action to launch it in the default browser.
            SelectTreeNodeByTag(tag);
        }
        else if (tag.StartsWith(TagFileRef))
        {
            var parts = tag.Substring(TagFileRef.Length).Split(':');
            if (parts.Length >= 2)
            {
                var projectId = Guid.Parse(parts[0]);
                var fileRefId = Guid.Parse(parts[1]);
                var fileRef = FindFileRef(_projectManager.GetProject(projectId), fileRefId);
                if (fileRef != null) OpenFileReference(fileRef);
            }
        }
        else if (tag.StartsWith(TagRealFolder))
        {
            var path = tag.Substring(TagRealFolder.Length);
            _backStack.Push(_currentPath);
            _forwardStack.Clear();
            NavigateToPath(path);
        }
        else if (tag.StartsWith("File:"))
        {
            var filePath = tag.Substring("File:".Length);

            // Image files open in the built-in viewer; everything else opens
            // with its associated application via the shell.
            if (ImageFileHelper.IsImageFile(filePath))
            {
                OpenImageViewer(filePath);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch { }
        }
    }

    /// <summary>
    /// Opens the in-app image viewer for the given image, seeded with all
    /// image files in the same folder so Next/Previous work.
    /// </summary>
    private void OpenImageViewer(string imagePath)
    {
        try
        {
            var folder = Path.GetDirectoryName(imagePath);
            IEnumerable<string> images;
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                images = Directory.EnumerateFiles(folder)
                                  .Where(ImageFileHelper.IsImageFile)
                                  .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                  .ToList();
            }
            else
            {
                images = new[] { imagePath };
            }

            using var viewer = new ImageViewerForm(images, imagePath);
            viewer.ShowDialog(this);
        }
        catch
        {
            // Fall back to the OS default app if the viewer cannot open.
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(imagePath) { UseShellExecute = true }); }
            catch { }
        }
    }

    private void ListView_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        // Toggle sort order
        if (listView.ListViewItemSorter is Helpers.ListViewColumnSorter sorter)
        {
            if (sorter.SortColumn == e.Column)
                sorter.Order = sorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;
            else
            {
                sorter.SortColumn = e.Column;
                sorter.Order = SortOrder.Ascending;
            }
            listView.Sort();
        }
    }

    private void ListView_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;

        var hit = listView.HitTest(e.Location);
        if (hit.Item != null && !hit.Item.Selected)
        {
            listView.SelectedItems.Clear();
            hit.Item.Selected = true;
        }

        ShowListViewContextMenu(hit.Item, e.Location);
    }

    private void ShowListViewContextMenu(ListViewItem? item, Point location)
    {
        var menu = new ContextMenuStrip();
        var tag = item?.Tag?.ToString() ?? "";

        if (item == null)
        {
            // Empty-area click: offer view options / new-item shortcuts if a project is open.
            if (_currentProject != null)
            {
                AddNewChildMenuItems(menu, _currentProject.Id, null);
                menu.Items.Add(new ToolStripSeparator());
            }
            AddViewSubmenu(menu);
        }
        else if (tag.StartsWith(TagProject))
        {
            var projectId = Guid.Parse(tag.Substring(TagProject.Length));
            menu.Items.Add("Open", null, (s, e) => SelectTreeNodeByTag(tag));
            menu.Items.Add(new ToolStripSeparator());
            AddProjectMenuItems(menu, projectId, () => RenameViaDialog(tag));
        }
        else if (tag.StartsWith(TagCollection))
        {
            var parts = tag.Substring(TagCollection.Length).Split(':');
            var projectId = Guid.Parse(parts[0]);
            var collectionId = Guid.Parse(parts[1]);
            menu.Items.Add("Open", null, (s, e) => SelectTreeNodeByTag(tag));
            menu.Items.Add(new ToolStripSeparator());
            AddCollectionMenuItems(menu, projectId, collectionId, () => RenameViaDialog(tag));
        }
        else if (tag.StartsWith(TagFolderRef))
        {
            var parts = tag.Substring(TagFolderRef.Length).Split(':');
            menu.Items.Add("Open", null, (s, e) => SelectTreeNodeByTag(tag));
            menu.Items.Add(new ToolStripSeparator());
            AddFolderReferenceMenuItems(menu, Guid.Parse(parts[0]), Guid.Parse(parts[1]));
        }
        else if (tag.StartsWith(TagWebResource))
        {
            var parts = tag.Substring(TagWebResource.Length).Split(':');
            AddWebResourceMenuItems(menu, Guid.Parse(parts[0]), Guid.Parse(parts[1]), tag);
        }
        else if (tag.StartsWith(TagFileRef))
        {
            var parts = tag.Substring(TagFileRef.Length).Split(':');
            AddFileReferenceMenuItems(menu, Guid.Parse(parts[0]), Guid.Parse(parts[1]));
        }
        else if (tag.StartsWith(TagRealFolder))
        {
            var path = tag.Substring(TagRealFolder.Length);
            menu.Items.Add("Open", null, (s, e) =>
            {
                _backStack.Push(_currentPath);
                _forwardStack.Clear();
                NavigateToPath(path);
            });
            AddRealFolderMenuItems(menu, path);
        }
        else if (tag.StartsWith("File:"))
        {
            var filePath = tag.Substring("File:".Length);
            menu.Items.Add("Open", null, (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true }); }
                catch (Exception ex) { MessageBox.Show($"Failed to open file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            });
            menu.Items.Add("Open Containing Folder", null, (s, e) =>
            {
                if (File.Exists(filePath))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
            });
            menu.Items.Add("Copy Path", null, (s, e) => Clipboard.SetText(filePath));
            menu.Items.Add("Properties", null, (s, e) =>
            {
                if (File.Exists(filePath)) _shellPropertiesProvider.ShowPropertiesDialog(filePath, this.Handle);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Add as File Resource...", null, async (s, e) =>
            {
                if (_currentProject == null) return;
                if (!CheckLeafLimit("Add File")) return;
                await _projectManager.AddFileReferenceAsync(_currentProject.Id, filePath);
                RefreshLicense();
                UpdateLicenseUi();
                RefreshTreeView();
            });
        }

        if (menu.Items.Count > 0)
            menu.Show(listView, location);
    }

    /// <summary>
    /// The ListView has no inline label editing (unlike the TreeView), so
    /// Project/Collection renames triggered from a ListView context menu go
    /// through this dialog instead of TreeNode.BeginEdit().
    /// </summary>
    private async void RenameViaDialog(string tag)
    {
        if (tag.StartsWith(TagProject))
        {
            var projectId = Guid.Parse(tag.Substring(TagProject.Length));
            var project = _projectManager.GetProject(projectId);
            if (project == null) return;

            using var dlg = new InputDialog("Rename Project", "Project name:", project.Name);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                var name = dlg.InputText.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    await _projectManager.RenameProjectAsync(projectId, name);
                    RefreshTreeView();
                }
            }
        }
        else if (tag.StartsWith(TagCollection))
        {
            var parts = tag.Substring(TagCollection.Length).Split(':');
            var projectId = Guid.Parse(parts[0]);
            var collectionId = Guid.Parse(parts[1]);
            var collection = _projectManager.GetProject(projectId)?.FindCollection(collectionId);
            if (collection == null) return;

            using var dlg = new InputDialog("Rename Collection", "Collection name:", collection.Name);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                var name = dlg.InputText.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    await _projectManager.RenameCollectionAsync(projectId, collectionId, name);
                    RefreshTreeView();
                }
            }
        }
    }

    private void AddViewSubmenu(ContextMenuStrip menu)
    {
        var viewMenu = new ToolStripMenuItem("View");
        viewMenu.DropDownItems.Add("Details", null, (s, e) => SetViewMode(AppView.Details));
        viewMenu.DropDownItems.Add("Extra Large Icons", null, (s, e) => SetViewMode(AppView.ExtraLargeIcon));
        viewMenu.DropDownItems.Add("Large Icons", null, (s, e) => SetViewMode(AppView.LargeIcon));
        viewMenu.DropDownItems.Add("Small Icons", null, (s, e) => SetViewMode(AppView.SmallIcon));
        viewMenu.DropDownItems.Add("List", null, (s, e) => SetViewMode(AppView.List));
        viewMenu.DropDownItems.Add("Tile", null, (s, e) => SetViewMode(AppView.Tile));
        menu.Items.Add(viewMenu);
    }

    // ── View Modes ──

    private enum AppView { Details, ExtraLargeIcon, LargeIcon, SmallIcon, List, Tile }

    private void SetViewMode(AppView mode)
    {
        _currentView = mode;

        // "Extra Large Icons" is not a distinct WinForms View value; Windows
        // achieves it by using the LargeIcon view with a bigger LargeImageList.
        // Swap the large image list accordingly, then set the underlying View.
        listView.LargeImageList = mode == AppView.ExtraLargeIcon ? imageListExtraLarge : imageListLarge;

        // Option B: image files show their picture thumbnail in large views and
        // their file-type icon in compact views. Toggle each tracked image item.
        var showThumbs = IsLargeView(mode);
        if (_imageItemKeys.Count > 0)
        {
            listView.BeginUpdate();
            foreach (var (item, keys) in _imageItemKeys)
            {
                if (item.ListView == null) continue;
                item.ImageKey = showThumbs ? keys.ThumbKey : keys.IconKey;
            }
            listView.EndUpdate();
        }

        listView.View = mode switch
        {
            AppView.Details => System.Windows.Forms.View.Details,
            AppView.ExtraLargeIcon => System.Windows.Forms.View.LargeIcon,
            AppView.LargeIcon => System.Windows.Forms.View.LargeIcon,
            AppView.SmallIcon => System.Windows.Forms.View.SmallIcon,
            AppView.List => System.Windows.Forms.View.List,
            AppView.Tile => System.Windows.Forms.View.Tile,
            _ => System.Windows.Forms.View.Details
        };

        // Update menu checkmarks
        menuViewDetails.Checked = mode == AppView.Details;
        menuViewExtraLargeIcons.Checked = mode == AppView.ExtraLargeIcon;
        menuViewLargeIcons.Checked = mode == AppView.LargeIcon;
        menuViewSmallIcons.Checked = mode == AppView.SmallIcon;
        menuViewList.Checked = mode == AppView.List;
        menuViewTile.Checked = mode == AppView.Tile;
    }

    // ── Menu Actions ──

    private async void MenuFileNewProject_Click(object? sender, EventArgs e)
    {
        if (!CheckProjectLimit()) return;

        using var dialog = new InputDialog("Create New Project", "Project name:", "New Project");
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var name = dialog.InputText.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                await _projectManager.CreateProjectAsync(name);
                RefreshLicense();
                UpdateLicenseUi();
                RefreshTreeView();
            }
        }
    }

    private async void MenuProjectNewCollection_Click(object? sender, EventArgs e)
    {
        if (_currentProject == null)
        {
            MessageBox.Show("Please select a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new InputDialog("Create New Collection", "Collection name:", "New Collection");
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var name = dialog.InputText.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                await _projectManager.CreateCollectionAsync(_currentProject.Id, name);
                RefreshTreeView();
            }
        }
    }

    private async void MenuProjectAddFolder_Click(object? sender, EventArgs e)
    {
        if (_currentProject == null)
        {
            MessageBox.Show("Please select a project first.", "No Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CheckLeafLimit("Add Folder")) return;

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder to add to the project",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await _projectManager.AddFolderReferenceAsync(_currentProject.Id, dialog.SelectedPath);
            RefreshLicense();
            UpdateLicenseUi();
            RefreshTreeView();
        }
    }

    private async Task ShowAddWebResourceDialog(Guid projectId, Guid? parentCollectionId)
    {
        if (!CheckLeafLimit("Add Web Resource")) return;

        using var dialog = new WebResourceDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var url = dialog.ResourceUrl.Trim();
            var name = string.IsNullOrWhiteSpace(dialog.ResourceName) ? null : dialog.ResourceName.Trim();
            var description = string.IsNullOrWhiteSpace(dialog.ResourceDescription) ? null : dialog.ResourceDescription.Trim();

            if (!string.IsNullOrEmpty(url))
            {
                await _projectManager.AddWebResourceAsync(projectId, url, name, description, parentCollectionId);
                RefreshLicense();
                UpdateLicenseUi();
                RefreshTreeView();
            }
        }
    }

    private async Task ShowAddFileResourceDialog(Guid projectId, Guid? parentCollectionId)
    {
        if (!CheckLeafLimit("Add File")) return;

        using var dialog = new FileResourceDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var path = dialog.ResourceFilePath.Trim();
            var name = string.IsNullOrWhiteSpace(dialog.ResourceName) ? null : dialog.ResourceName.Trim();
            var description = string.IsNullOrWhiteSpace(dialog.ResourceDescription) ? null : dialog.ResourceDescription.Trim();

            if (!string.IsNullOrEmpty(path))
            {
                await _projectManager.AddFileReferenceAsync(projectId, path, name, description, parentCollectionId);
                RefreshLicense();
                UpdateLicenseUi();
                RefreshTreeView();
            }
        }
    }

    // ── Context Menu ──
    //
    // Item-type menus are built by the shared Add*MenuItems helpers below so the
    // TreeView and ListView context menus can never drift out of parity with
    // each other — each helper is called from both ShowTreeViewContextMenu and
    // ShowListViewContextMenu.

    private void AddNewChildMenuItems(ContextMenuStrip menu, Guid projectId, Guid? parentCollectionId)
    {
        menu.Items.Add(parentCollectionId == null ? "New Collection..." : "New Sub-Collection...", null, async (s, e) =>
        {
            using var dlg = new InputDialog(
                parentCollectionId == null ? "Create New Collection" : "Create Sub-Collection",
                "Collection name:", "New Collection");
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                var name = dlg.InputText.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    await _projectManager.CreateCollectionAsync(projectId, name, parentCollectionId);
                    RefreshTreeView();
                }
            }
        });
        menu.Items.Add("Add Folder...", null, async (s, e) =>
        {
            if (!CheckLeafLimit("Add Folder")) return;
            using var dlg = new FolderBrowserDialog { Description = "Select folder to add" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                await _projectManager.AddFolderReferenceAsync(projectId, dlg.SelectedPath, parentCollectionId);
                RefreshLicense();
                UpdateLicenseUi();
                RefreshTreeView();
            }
        });
        menu.Items.Add("Add Web Resource...", null, async (s, e) => await ShowAddWebResourceDialog(projectId, parentCollectionId));
        menu.Items.Add("Add File...", null, async (s, e) => await ShowAddFileResourceDialog(projectId, parentCollectionId));
    }

    /// <summary>
    /// Move Up/Move Down for a top-level Project — a precision-free alternative to dragging
    /// for repositioning siblings, since Projects/Collections/etc. only get a few pixels of
    /// "insertion line" hit zone during drag-and-drop.
    /// </summary>
    private void AddProjectMoveMenuItems(ContextMenuStrip menu, Guid projectId)
    {
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Move Up", null, async (s, e) =>
        {
            await _projectManager.MoveProjectUpAsync(projectId);
            RefreshTreeView();
        });
        menu.Items.Add("Move Down", null, async (s, e) =>
        {
            await _projectManager.MoveProjectDownAsync(projectId);
            RefreshTreeView();
        });
    }

    /// <summary>Move Up/Move Down for any ProjectChild (Collection/FolderReference/WebResource/FileReference).</summary>
    private void AddChildMoveMenuItems(ContextMenuStrip menu, Guid projectId, Guid childId)
    {
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Move Up", null, async (s, e) =>
        {
            await _projectManager.MoveChildUpAsync(projectId, childId);
            RefreshTreeView();
        });
        menu.Items.Add("Move Down", null, async (s, e) =>
        {
            await _projectManager.MoveChildDownAsync(projectId, childId);
            RefreshTreeView();
        });
    }

    private void AddProjectMenuItems(ContextMenuStrip menu, Guid projectId, Action rename)
    {
        AddNewChildMenuItems(menu, projectId, null);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Rename", null, (s, e) => rename());
        menu.Items.Add("Edit Description...", null, async (s, e) =>
        {
            var project = _projectManager.GetProject(projectId);
            if (project != null)
            {
                using var dlg = new InputDialog("Edit Project Description", "Description:", project.Description ?? "");
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var description = dlg.InputText.Trim();
                    await _projectManager.UpdateProjectAsync(
                        projectId, newDescription: string.IsNullOrEmpty(description) ? null : description);
                    RefreshTreeView();
                }
            }
        });
        AddProjectMoveMenuItems(menu, projectId);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete Project", null, async (s, e) =>
        {
            var project = _projectManager.GetProject(projectId);
            if (project == null) return;
            var result = MessageBox.Show($"Delete project '{project.Name}'?", "Confirm Delete",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                await _projectManager.DeleteProjectAsync(projectId);
                _currentProject = null;
                RefreshTreeView();
            }
        });
    }

    private void AddCollectionMenuItems(ContextMenuStrip menu, Guid projectId, Guid collectionId, Action rename)
    {
        AddNewChildMenuItems(menu, projectId, collectionId);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Rename", null, (s, e) => rename());
        menu.Items.Add("Edit Description...", null, async (s, e) =>
        {
            var collection = _projectManager.GetProject(projectId)?.FindCollection(collectionId);
            if (collection != null)
            {
                using var dlg = new InputDialog("Edit Collection Description", "Description:", collection.Description ?? "");
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var description = dlg.InputText.Trim();
                    await _projectManager.UpdateCollectionAsync(
                        projectId, collectionId, newDescription: string.IsNullOrEmpty(description) ? null : description);
                    RefreshTreeView();
                }
            }
        });
        AddChildMoveMenuItems(menu, projectId, collectionId);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Delete Collection", null, async (s, e) =>
        {
            var result = MessageBox.Show("Delete this collection and remove its folder references from this project?",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                await _projectManager.DeleteCollectionAsync(projectId, collectionId);
                RefreshTreeView();
            }
        });
    }

    /// <summary>
    /// Prepends the "unavailable" warning header plus Check Now / Locate / Stop-Resume-Retry
    /// actions to a resource's context menu, when it's currently unavailable. No-ops otherwise.
    /// </summary>
    private void AddAvailabilityMenuItems(ContextMenuStrip menu, Guid projectId, ProjectChild child, string relinkLabel, Func<Task>? relinkAction)
    {
        if (!_availabilityCache.TryGetValue(child.Id, out var result) || result.Status != AvailabilityStatus.Unavailable)
            return;

        menu.Items.Add(new ToolStripMenuItem($"⚠ Unavailable — {DescribeUnavailableShort(result.LocationKind)}") { Enabled = false });
        menu.Items.Add("Check Availability Now", null, async (s, e) => await ForceCheckAvailabilityAsync(child));

        if (relinkAction != null)
            menu.Items.Add(relinkLabel, null, async (s, e) => await relinkAction());

        if (result.LocationKind != ResourceLocationKind.LocalDisk)
        {
            var suppressed = child.Metadata.TryGetValue(ResourceAvailabilityChecker.SuppressAutoRetryMetadataKey, out var v) && v == "true";
            menu.Items.Add(suppressed ? "Resume Auto-Retry" : "Stop Auto-Retry", null, async (s, e) =>
            {
                await _projectManager.SetChildMetadataAsync(
                    projectId, child.Id, ResourceAvailabilityChecker.SuppressAutoRetryMetadataKey, suppressed ? null : "true");
            });
        }

        menu.Items.Add(new ToolStripSeparator());
    }

    private async Task LocateFolderReferenceAsync(Guid projectId, FolderReference folderRef)
    {
        using var dlg = new FolderBrowserDialog { Description = $"Select the new location for \"{folderRef.EffectiveName}\"" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        await _projectManager.UpdateFolderReferenceAsync(projectId, folderRef.Id, newPath: dlg.SelectedPath);
        await ForceCheckAvailabilityAsync(folderRef);
        RefreshTreeView();
    }

    private async Task LocateFileReferenceAsync(Guid projectId, FileReference fileRef)
    {
        using var dlg = new OpenFileDialog { Title = $"Select the new location for \"{fileRef.EffectiveName}\"", CheckFileExists = true };
        if (!string.IsNullOrEmpty(fileRef.Extension))
        {
            var ext = fileRef.Extension.TrimStart('.').ToUpperInvariant();
            dlg.Filter = $"{ext} files (*{fileRef.Extension})|*{fileRef.Extension}|All files (*.*)|*.*";
        }
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        await _projectManager.UpdateFileReferenceAsync(projectId, fileRef.Id, newPath: dlg.FileName);
        await ForceCheckAvailabilityAsync(fileRef);
        RefreshTreeView();
    }

    private void AddFolderReferenceMenuItems(ContextMenuStrip menu, Guid projectId, Guid folderRefId)
    {
        var existingFr = FindFolderRef(_projectManager.GetProject(projectId), folderRefId);
        if (existingFr != null)
        {
            AddAvailabilityMenuItems(menu, projectId, existingFr, "Locate Folder...",
                async () => await LocateFolderReferenceAsync(projectId, existingFr));
        }

        menu.Items.Add("Edit Description...", null, async (s, e) =>
        {
            var project = _projectManager.GetProject(projectId);
            var fr = FindFolderRef(project, folderRefId);
            if (fr != null)
            {
                using var dlg = new InputDialog("Edit Folder Description", "Description:", fr.Description ?? "");
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var description = dlg.InputText.Trim();
                    await _projectManager.UpdateFolderReferenceAsync(
                        projectId, folderRefId, newDescription: string.IsNullOrEmpty(description) ? null : description);
                    RefreshTreeView();
                }
            }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open in Explorer", null, (s, e) =>
        {
            var fr = FindFolderRef(_projectManager.GetProject(projectId), folderRefId);
            if (fr != null && Directory.Exists(fr.RealPath))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fr.RealPath) { UseShellExecute = true });
        });
        menu.Items.Add("Open Command Prompt Here", null, (s, e) =>
        {
            var fr = FindFolderRef(_projectManager.GetProject(projectId), folderRefId);
            if (fr != null) LaunchTerminal(fr.RealPath, usePowerShell: false);
        });
        menu.Items.Add("Open PowerShell Here", null, (s, e) =>
        {
            var fr = FindFolderRef(_projectManager.GetProject(projectId), folderRefId);
            if (fr != null) LaunchTerminal(fr.RealPath, usePowerShell: true);
        });
        menu.Items.Add("Copy Path", null, (s, e) =>
        {
            var fr = FindFolderRef(_projectManager.GetProject(projectId), folderRefId);
            if (fr != null && !string.IsNullOrEmpty(fr.RealPath)) Clipboard.SetText(fr.RealPath);
        });
        menu.Items.Add("Properties", null, (s, e) =>
        {
            var fr = FindFolderRef(_projectManager.GetProject(projectId), folderRefId);
            if (fr != null && Directory.Exists(fr.RealPath)) _shellPropertiesProvider.ShowPropertiesDialog(fr.RealPath, this.Handle);
        });
        AddChildMoveMenuItems(menu, projectId, folderRefId);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove from Project", null, async (s, e) =>
        {
            var result = MessageBox.Show("Remove this folder reference from the project?",
                "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                await _projectManager.RemoveFolderReferenceAsync(projectId, folderRefId);
                RefreshTreeView();
            }
        });
    }

    private void AddWebResourceMenuItems(ContextMenuStrip menu, Guid projectId, Guid resourceId, string tag)
    {
        var existingWr = FindWebResource(_projectManager.GetProject(projectId), resourceId);
        if (existingWr != null)
            AddAvailabilityMenuItems(menu, projectId, existingWr, relinkLabel: "", relinkAction: null);

        menu.Items.Add("Open in External Browser", null, (s, e) => LaunchWebResource(tag));
        menu.Items.Add("Copy URL", null, (s, e) =>
        {
            var wr = FindWebResource(_projectManager.GetProject(projectId), resourceId);
            if (wr != null && !string.IsNullOrEmpty(wr.Url)) Clipboard.SetText(wr.Url);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Edit...", null, async (s, e) =>
        {
            var wr = FindWebResource(_projectManager.GetProject(projectId), resourceId);
            if (wr != null)
            {
                using var dlg = new WebResourceDialog("Edit Web Resource", wr.DisplayName ?? "", wr.Url, wr.Description ?? "");
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    await _projectManager.UpdateWebResourceAsync(
                        projectId, resourceId,
                        string.IsNullOrWhiteSpace(dlg.ResourceName) ? null : dlg.ResourceName.Trim(),
                        dlg.ResourceUrl.Trim(),
                        string.IsNullOrWhiteSpace(dlg.ResourceDescription) ? null : dlg.ResourceDescription.Trim());
                    RefreshTreeView();
                }
            }
        });
        AddChildMoveMenuItems(menu, projectId, resourceId);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove from Project", null, async (s, e) =>
        {
            var result = MessageBox.Show("Remove this web resource from the project?",
                "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                await _projectManager.RemoveWebResourceAsync(projectId, resourceId);
                RefreshTreeView();
            }
        });
    }

    private void AddFileReferenceMenuItems(ContextMenuStrip menu, Guid projectId, Guid fileRefId)
    {
        var existingFile = FindFileRef(_projectManager.GetProject(projectId), fileRefId);
        if (existingFile != null)
        {
            AddAvailabilityMenuItems(menu, projectId, existingFile, "Locate File...",
                async () => await LocateFileReferenceAsync(projectId, existingFile));
        }

        menu.Items.Add("Open", null, (s, e) =>
        {
            var fr = FindFileRef(_projectManager.GetProject(projectId), fileRefId);
            if (fr != null) OpenFileReference(fr);
        });
        menu.Items.Add("Open Containing Folder", null, (s, e) =>
        {
            var fr = FindFileRef(_projectManager.GetProject(projectId), fileRefId);
            if (fr != null && File.Exists(fr.FilePath))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{fr.FilePath}\"") { UseShellExecute = true });
        });
        menu.Items.Add("Copy Path", null, (s, e) =>
        {
            var fr = FindFileRef(_projectManager.GetProject(projectId), fileRefId);
            if (fr != null && !string.IsNullOrEmpty(fr.FilePath)) Clipboard.SetText(fr.FilePath);
        });
        menu.Items.Add("Properties", null, (s, e) =>
        {
            var fr = FindFileRef(_projectManager.GetProject(projectId), fileRefId);
            if (fr != null && File.Exists(fr.FilePath)) _shellPropertiesProvider.ShowPropertiesDialog(fr.FilePath, this.Handle);
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Edit...", null, async (s, e) =>
        {
            var fr = FindFileRef(_projectManager.GetProject(projectId), fileRefId);
            if (fr != null)
            {
                using var dlg = new FileResourceDialog("Edit File", fr.DisplayName ?? "", fr.FilePath, fr.Description ?? "");
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    await _projectManager.UpdateFileReferenceAsync(
                        projectId, fileRefId,
                        newDisplayName: string.IsNullOrWhiteSpace(dlg.ResourceName) ? null : dlg.ResourceName.Trim(),
                        newPath: dlg.ResourceFilePath.Trim(),
                        newDescription: string.IsNullOrWhiteSpace(dlg.ResourceDescription) ? null : dlg.ResourceDescription.Trim());
                    RefreshTreeView();
                }
            }
        });
        AddChildMoveMenuItems(menu, projectId, fileRefId);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove from Project", null, async (s, e) =>
        {
            var result = MessageBox.Show("Remove this file reference from the project?",
                "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                await _projectManager.RemoveFileReferenceAsync(projectId, fileRefId);
                RefreshTreeView();
            }
        });
    }

    private void AddRealFolderMenuItems(ContextMenuStrip menu, string path)
    {
        menu.Items.Add("Open in Explorer", null, (s, e) =>
        {
            if (Directory.Exists(path))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        });
        menu.Items.Add("Open Command Prompt Here", null, (s, e) => LaunchTerminal(path, usePowerShell: false));
        menu.Items.Add("Open PowerShell Here", null, (s, e) => LaunchTerminal(path, usePowerShell: true));
        menu.Items.Add("Copy Path", null, (s, e) => Clipboard.SetText(path));
        menu.Items.Add("Properties", null, (s, e) =>
        {
            if (Directory.Exists(path)) _shellPropertiesProvider.ShowPropertiesDialog(path, this.Handle);
        });
    }

    private void ShowTreeViewContextMenu(TreeNode node, Point location)
    {
        var menu = new ContextMenuStrip();
        var tag = node.Tag?.ToString() ?? "";

        if (tag == TagProjectsRoot || tag.StartsWith(TagProject))
        {
            menu.Items.Add("New Project...", null, MenuFileNewProject_Click);
        }

        if (tag.StartsWith(TagProject))
        {
            var projectId = Guid.Parse(tag.Substring(TagProject.Length));
            AddProjectMenuItems(menu, projectId, () => node.BeginEdit());
        }

        if (tag.StartsWith(TagCollection))
        {
            var parts = tag.Substring(TagCollection.Length).Split(':');
            var projectId = Guid.Parse(parts[0]);
            var collectionId = Guid.Parse(parts[1]);
            AddCollectionMenuItems(menu, projectId, collectionId, () => node.BeginEdit());
        }

        if (tag.StartsWith(TagFolderRef))
        {
            var parts = tag.Substring(TagFolderRef.Length).Split(':');
            AddFolderReferenceMenuItems(menu, Guid.Parse(parts[0]), Guid.Parse(parts[1]));
        }

        if (tag.StartsWith(TagWebResource))
        {
            var parts = tag.Substring(TagWebResource.Length).Split(':');
            AddWebResourceMenuItems(menu, Guid.Parse(parts[0]), Guid.Parse(parts[1]), tag);
        }

        if (tag.StartsWith(TagFileRef))
        {
            var parts = tag.Substring(TagFileRef.Length).Split(':');
            AddFileReferenceMenuItems(menu, Guid.Parse(parts[0]), Guid.Parse(parts[1]));
        }

        if (tag.StartsWith(TagRealFolder))
        {
            AddRealFolderMenuItems(menu, tag.Substring(TagRealFolder.Length));
        }

        if (menu.Items.Count > 0)
        {
            menu.Show(treeView, location);
        }
    }

    // ── Tree State Preservation ──

    private void RefreshTreeView()
    {
        var expandedTags = CaptureExpandedTags();
        var selectedTag = treeView.SelectedNode?.Tag?.ToString();
        InitializeTreeView();
        treeView.BeginUpdate();
        RestoreTreeState(treeView.Nodes, expandedTags, selectedTag);
        treeView.EndUpdate();
        treeView.SelectedNode?.EnsureVisible();
    }

    private HashSet<string> CaptureExpandedTags()
    {
        var tags = new HashSet<string>();
        CollectExpandedTags(treeView.Nodes, tags);
        return tags;
    }

    private static void CollectExpandedTags(TreeNodeCollection nodes, HashSet<string> tags)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.IsExpanded && node.Tag is string tag)
                tags.Add(tag);
            CollectExpandedTags(node.Nodes, tags);
        }
    }

    private void RestoreTreeState(TreeNodeCollection nodes, HashSet<string> expandedTags, string? selectedTag)
    {
        foreach (TreeNode node in nodes)
        {
            var tag = node.Tag?.ToString();
            if (tag != null && expandedTags.Contains(tag))
                node.Expand();
            if (tag != null && tag == selectedTag)
                treeView.SelectedNode = node;
            if (node.Nodes.Count > 0)
                RestoreTreeState(node.Nodes, expandedTags, selectedTag);
        }
    }

    private void SaveTreeState()
    {
        try
        {
            var settings = new TreeUiSettings
            {
                ExpandedTags = CaptureExpandedTags().ToList(),
                SelectedTag = treeView.SelectedNode?.Tag?.ToString()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(_uiSettingsPath)!);
            File.WriteAllText(_uiSettingsPath, JsonSerializer.Serialize(settings));
        }
        catch { /* non-critical */ }
    }

    private TreeUiSettings LoadTreeState()
    {
        try
        {
            if (File.Exists(_uiSettingsPath))
            {
                var json = File.ReadAllText(_uiSettingsPath);
                return JsonSerializer.Deserialize<TreeUiSettings>(json) ?? new TreeUiSettings();
            }
        }
        catch { /* non-critical */ }
        return new TreeUiSettings();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _availabilityRetryTimer.Stop();
        // Deliberately NOT disposing _unavailableTreeFont/_unavailableListFont here: they're still
        // assigned to TreeNode.NodeFont/ListViewItem.Font on every unavailable row, and the TreeView
        // can still receive a WM_NOTIFY/NM_CUSTOMDRAW repaint between OnFormClosing and the window
        // actually being destroyed. Disposing them here left those controls pointing at a Font whose
        // GDI handle was already gone, and TreeView.CustomDraw's Font.ToHfont() call would throw
        // ArgumentException on that last repaint. The process exit reclaims these either way.
        SaveTreeState();
        SaveWindowBounds();
        base.OnFormClosing(e);
    }

    private void MenuFileSettings_Click(object? sender, EventArgs e)
    {
        var settings = _appSettingsManager.Load();
        using var dlg = new SettingsForm(settings);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            settings.FocusOnRun = dlg.SelectedFocusOnRun;
            _appSettingsManager.Save(settings);
        }
    }

    // ── Drag and Drop ──

    // Where the cursor sits over a row, driving whether we show an insertion line
    // (reorder as a sibling) or highlight the whole row (drop into it as a container).
    private enum DropZone { None, Before, Into, After }

    private enum DropKind { ReparentOrReorder, ReorderProjects, ConvertProjectToCollection, ConvertCollectionToProject }

    private sealed class DropPlan
    {
        public required DropKind Kind { get; init; }
        public required TreeNode HighlightNode { get; init; }
        public required DropZone Zone { get; init; }

        // ReparentOrReorder + ConvertCollectionToProject: the dragged item's current project/child.
        public Guid ProjectId { get; init; }
        public Guid ChildId { get; init; }

        // ConvertProjectToCollection: the project being dragged, and the project hosting the target collection.
        public Guid TargetProjectId { get; init; }

        // ReparentOrReorder only.
        public Guid? DestParentCollectionId { get; init; }
        public Guid? BeforeSiblingId { get; init; }
    }

    // Screen-coordinate insertion line, drawn with ControlPaint.DrawReversibleLine (XOR-painted
    // directly onto the screen DC) so it can be erased just by drawing it again — no Invalidate/
    // repaint bookkeeping needed for a control as heavy to redraw as a TreeView.
    private (Point Start, Point End)? _insertionLine;

    private void TreeView_ItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not TreeNode node) return;
        var tag = node.Tag?.ToString() ?? "";
        if (!tag.StartsWith(TagCollection) && !tag.StartsWith(TagFolderRef) && !tag.StartsWith(TagWebResource) && !tag.StartsWith(TagProject))
            return;
        treeView.DoDragDrop(node, DragDropEffects.Move);
    }

    private void TreeView_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(TreeNode)) == true
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void TreeView_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode draggedNode)
        {
            e.Effect = DragDropEffects.None;
            ClearDragHighlight();
            ClearInsertionLine();
            return;
        }

        var pt = treeView.PointToClient(new Point(e.X, e.Y));
        var (targetNode, zone) = GetDropZone(draggedNode, pt);
        var plan = targetNode != null ? ComputeDropPlan(draggedNode, targetNode, zone) : null;

        if (plan == null)
        {
            e.Effect = DragDropEffects.None;
            ClearDragHighlight();
            ClearInsertionLine();
            return;
        }

        e.Effect = DragDropEffects.Move;

        if (plan.Zone == DropZone.Into)
        {
            ClearInsertionLine();
            if (!ReferenceEquals(plan.HighlightNode, _dragHighlightNode))
            {
                ClearDragHighlight();
                _dragHighlightNode = plan.HighlightNode;
                plan.HighlightNode.BackColor = SystemColors.Highlight;
                plan.HighlightNode.ForeColor = SystemColors.HighlightText;
            }
        }
        else
        {
            ClearDragHighlight();
            DrawInsertionLine(targetNode!, plan.Zone);
        }
    }

    private void TreeView_DragLeave(object? sender, EventArgs e)
    {
        ClearDragHighlight();
        ClearInsertionLine();
    }

    private async void TreeView_DragDrop(object? sender, DragEventArgs e)
    {
        ClearDragHighlight();
        ClearInsertionLine();

        if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode draggedNode) return;

        var pt = treeView.PointToClient(new Point(e.X, e.Y));
        var (targetNode, zone) = GetDropZone(draggedNode, pt);
        var plan = targetNode != null ? ComputeDropPlan(draggedNode, targetNode, zone) : null;
        if (plan == null) return;

        var dragTag = draggedNode.Tag?.ToString() ?? "";

        try
        {
            switch (plan.Kind)
            {
                case DropKind.ReparentOrReorder:
                    await _projectManager.MoveChildAsync(plan.ProjectId, plan.ChildId, plan.DestParentCollectionId, plan.BeforeSiblingId);
                    RefreshTreeView();
                    SelectTreeNodeByTag(dragTag);
                    break;

                case DropKind.ReorderProjects:
                    await _projectManager.MoveProjectAsync(plan.ProjectId, plan.BeforeSiblingId);
                    RefreshTreeView();
                    SelectTreeNodeByTag(dragTag);
                    break;

                case DropKind.ConvertProjectToCollection:
                    await _projectManager.ConvertProjectToCollectionAsync(plan.ProjectId, plan.TargetProjectId, plan.DestParentCollectionId);
                    RefreshTreeView();
                    SelectTreeNodeByTag(TagCollection + $"{plan.TargetProjectId}:{plan.ProjectId}");
                    break;

                case DropKind.ConvertCollectionToProject:
                    await _projectManager.ConvertCollectionToProjectAsync(plan.ProjectId, plan.ChildId);
                    RefreshTreeView();
                    SelectTreeNodeByTag(TagProject + plan.ChildId);
                    break;
            }
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Cannot Move", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ClearDragHighlight()
    {
        if (_dragHighlightNode != null)
        {
            _dragHighlightNode.BackColor = Color.Empty;
            _dragHighlightNode.ForeColor = Color.Empty;
            _dragHighlightNode = null;
        }
    }

    private void DrawInsertionLine(TreeNode targetNode, DropZone zone)
    {
        var bounds = targetNode.Bounds;
        var y = zone == DropZone.Before ? bounds.Top : bounds.Bottom;
        var start = treeView.PointToScreen(new Point(bounds.Left, y));
        var end = treeView.PointToScreen(new Point(treeView.ClientSize.Width, y));

        if (_insertionLine is { } current && current.Start == start && current.End == end)
            return;

        ClearInsertionLine();
        ControlPaint.DrawReversibleLine(start, end, SystemColors.Highlight);
        _insertionLine = (start, end);
    }

    private void ClearInsertionLine()
    {
        if (_insertionLine is { } line)
        {
            ControlPaint.DrawReversibleLine(line.Start, line.End, SystemColors.Highlight);
            _insertionLine = null;
        }
    }

    /// <summary>
    /// Determines which row the cursor is over and which zone of its height it's in.
    /// Project rows are Into-only for anything except another Project (Projects have no
    /// "into" of their own — they never nest under each other, only under the Projects
    /// root); dragging one Project onto another gets Before/After only, to reorder them.
    /// Collections get Before/Into/After. Leaf rows (FolderReference/WebResource/
    /// FileReference) get Before/After only, since they can't contain children.
    /// </summary>
    private (TreeNode? Node, DropZone Zone) GetDropZone(TreeNode draggedNode, Point clientPt)
    {
        // Hit-test at a fixed near-left X rather than the cursor's actual X: TreeNode.Bounds
        // (and the row highlight it drives) only spans the icon+label, so a cursor sitting to
        // the right of a short or deeply-indented label — a very normal place for it to be
        // mid-drag — would otherwise miss the row entirely and read as "no valid target here".
        var targetNode = treeView.GetNodeAt(new Point(2, clientPt.Y));
        if (targetNode == null) return (null, DropZone.None);

        var dragTag = draggedNode.Tag?.ToString() ?? "";
        var tag = targetNode.Tag?.ToString() ?? "";

        if (tag == TagProjectsRoot)
            return (targetNode, DropZone.Into);

        if (tag.StartsWith(TagProject))
        {
            if (!dragTag.StartsWith(TagProject))
                return (targetNode, DropZone.Into);

            var pBounds = targetNode.Bounds;
            var pFrac = pBounds.Height == 0 ? 0.5 : (double)(clientPt.Y - pBounds.Top) / pBounds.Height;
            return (targetNode, pFrac < 0.5 ? DropZone.Before : DropZone.After);
        }

        var bounds = targetNode.Bounds;
        var frac = bounds.Height == 0 ? 0.5 : (double)(clientPt.Y - bounds.Top) / bounds.Height;

        if (tag.StartsWith(TagCollection))
        {
            if (frac < 0.2) return (targetNode, DropZone.Before);
            if (frac > 0.8) return (targetNode, DropZone.After);
            return (targetNode, DropZone.Into);
        }

        return (targetNode, frac < 0.5 ? DropZone.Before : DropZone.After);
    }

    private DropPlan? ComputeDropPlan(TreeNode draggedNode, TreeNode targetNode, DropZone zone)
    {
        if (ReferenceEquals(draggedNode, targetNode)) return null;
        if (IsAncestorOf(draggedNode, targetNode)) return null;

        var dragTag = draggedNode.Tag?.ToString() ?? "";
        var targetTag = targetNode.Tag?.ToString() ?? "";

        if (dragTag.StartsWith(TagProject))
        {
            var draggedProjectId = GetProjectIdFromTag(dragTag);

            // Drag a Project onto another Project: reorder them (Projects never nest under
            // each other, so this is never an "into" — see GetDropZone).
            if (targetTag.StartsWith(TagProject) && zone != DropZone.Into)
            {
                var beforeProjectId = zone == DropZone.Before
                    ? GetProjectIdFromTag(targetTag)
                    : NextSiblingIdSkipping(targetNode, draggedNode, GetProjectIdFromTag);
                return new DropPlan
                {
                    Kind = DropKind.ReorderProjects,
                    HighlightNode = targetNode,
                    Zone = zone,
                    ProjectId = draggedProjectId,
                    BeforeSiblingId = beforeProjectId
                };
            }

            // Drag a Project onto a Collection's middle zone: convert the project into a
            // collection nested there.
            if (zone == DropZone.Into && targetTag.StartsWith(TagCollection))
            {
                var hostProjectId = GetProjectIdFromTag(targetTag);
                if (hostProjectId == draggedProjectId) return null; // can't nest a project inside its own collection
                return new DropPlan
                {
                    Kind = DropKind.ConvertProjectToCollection,
                    HighlightNode = targetNode,
                    Zone = DropZone.Into,
                    ProjectId = draggedProjectId,
                    TargetProjectId = hostProjectId,
                    DestParentCollectionId = GetCollectionIdFromTag(targetTag)
                };
            }

            return null;
        }

        // Drag a Collection onto the Projects root: convert it into a new top-level project.
        if (targetTag == TagProjectsRoot)
        {
            if (zone != DropZone.Into || !dragTag.StartsWith(TagCollection)) return null;
            return new DropPlan
            {
                Kind = DropKind.ConvertCollectionToProject,
                HighlightNode = targetNode,
                Zone = DropZone.Into,
                ProjectId = GetProjectIdFromTag(dragTag),
                ChildId = GetChildIdFromTag(dragTag)
            };
        }

        // Reparent / reorder: Collection, FolderReference, or WebResource within the same project.
        if (!dragTag.StartsWith(TagCollection) && !dragTag.StartsWith(TagFolderRef) && !dragTag.StartsWith(TagWebResource))
            return null;

        var projectId = GetProjectIdFromTag(dragTag);
        if (projectId != GetProjectIdFromTag(targetTag)) return null;

        TreeNode highlightNode;
        Guid? destParentId;
        Guid? beforeSiblingId;

        if (zone == DropZone.Into)
        {
            if (!targetTag.StartsWith(TagCollection) && !targetTag.StartsWith(TagProject)) return null;
            highlightNode = targetNode;
            destParentId = GetCollectionIdFromTag(targetTag);
            beforeSiblingId = null; // append at end
        }
        else
        {
            var parentNode = targetNode.Parent;
            var parentTag = parentNode?.Tag?.ToString() ?? "";
            if (parentNode == null || (!parentTag.StartsWith(TagCollection) && !parentTag.StartsWith(TagProject)))
                return null;
            if (ReferenceEquals(parentNode, draggedNode)) return null;

            highlightNode = parentNode;
            destParentId = GetCollectionIdFromTag(parentTag);

            beforeSiblingId = zone == DropZone.Before
                ? GetChildIdFromTag(targetTag)
                : NextSiblingIdSkipping(targetNode, draggedNode, GetChildIdFromTag);
        }

        return new DropPlan
        {
            Kind = DropKind.ReparentOrReorder,
            HighlightNode = highlightNode,
            Zone = zone,
            ProjectId = projectId,
            ChildId = GetChildIdFromTag(dragTag),
            DestParentCollectionId = destParentId,
            BeforeSiblingId = beforeSiblingId
        };
    }

    /// <summary>
    /// For an "After" zone drop: the Id of whichever sibling currently follows targetNode,
    /// skipping over draggedNode itself if it happens to be that very next sibling (dragging
    /// an item to sit "after" its own immediately-preceding sibling is a same-position no-op,
    /// not "move to the end"). Null means "append at the end" (targetNode is last).
    /// </summary>
    private static Guid? NextSiblingIdSkipping(TreeNode targetNode, TreeNode draggedNode, Func<string, Guid> parseId)
    {
        var next = targetNode.NextNode;
        while (next != null && ReferenceEquals(next, draggedNode))
            next = next.NextNode;
        return next?.Tag is string nextTag ? parseId(nextTag) : null;
    }

    private static bool IsAncestorOf(TreeNode possibleAncestor, TreeNode node)
    {
        var cur = node.Parent;
        while (cur != null)
        {
            if (ReferenceEquals(cur, possibleAncestor)) return true;
            cur = cur.Parent;
        }
        return false;
    }

    private static Guid GetProjectIdFromTag(string tag)
    {
        if (tag.StartsWith(TagProject)) return Guid.Parse(tag[TagProject.Length..]);
        if (tag.StartsWith(TagCollection)) return Guid.Parse(tag[TagCollection.Length..].Split(':')[0]);
        if (tag.StartsWith(TagFolderRef)) return Guid.Parse(tag[TagFolderRef.Length..].Split(':')[0]);
        if (tag.StartsWith(TagWebResource)) return Guid.Parse(tag[TagWebResource.Length..].Split(':')[0]);
        if (tag.StartsWith(TagFileRef)) return Guid.Parse(tag[TagFileRef.Length..].Split(':')[0]);
        return Guid.Empty;
    }

    private static Guid GetChildIdFromTag(string tag)
    {
        if (tag.StartsWith(TagCollection)) return Guid.Parse(tag[TagCollection.Length..].Split(':')[1]);
        if (tag.StartsWith(TagFolderRef)) return Guid.Parse(tag[TagFolderRef.Length..].Split(':')[1]);
        if (tag.StartsWith(TagWebResource)) return Guid.Parse(tag[TagWebResource.Length..].Split(':')[1]);
        if (tag.StartsWith(TagFileRef)) return Guid.Parse(tag[TagFileRef.Length..].Split(':')[1]);
        return Guid.Empty;
    }

    private static Guid? GetCollectionIdFromTag(string tag)
    {
        if (tag.StartsWith(TagCollection))
            return Guid.Parse(tag[TagCollection.Length..].Split(':')[1]);
        return null;
    }

    // ── Helpers ──

    private void LaunchWebResource(string tag)
    {
        var parts = tag.Substring(TagWebResource.Length).Split(':');
        if (parts.Length < 2) return;

        var projectId = Guid.Parse(parts[0]);
        var resourceId = Guid.Parse(parts[1]);

        var project = _projectManager.GetProject(projectId);
        var webResource = FindWebResource(project, resourceId);
        if (webResource != null) LaunchWebResource(webResource);
    }

    private void LaunchWebResource(WebResource webResource)
    {
        if (string.IsNullOrWhiteSpace(webResource.Url)) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(webResource.Url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch URL: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private WebResource? FindWebResource(Project? project, Guid resourceId)
    {
        if (project == null) return null;
        return FindWebResourceIn(project.Children, resourceId);
    }

    private static WebResource? FindWebResourceIn(List<ProjectChild> children, Guid id)
    {
        foreach (var child in children)
        {
            if (child is WebResource wr && wr.Id == id)
                return wr;
            if (child is Collection coll)
            {
                var found = FindWebResourceIn(coll.Children, id);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the ImageList key to use for a FileReference in the ListView,
    /// registering the extension's shell icon on demand.
    /// </summary>
    private string GetFileRefListImageKey(FileReference fileRef)
    {
        var ext = fileRef.Extension;
        if (!string.IsNullOrEmpty(ext))
        {
            var iconKey = $"ext_{ext}";
            if (!imageListSmall.Images.ContainsKey(iconKey))
            {
                try
                {
                    imageListSmall.Images.Add(iconKey, _shellIconProvider.GetIconByExtension(ext, IconSize.Small));
                    imageListLarge.Images.Add(iconKey, _shellIconProvider.GetIconByExtension(ext, IconSize.Large));
                    imageListExtraLarge.Images.Add(iconKey, _shellIconProvider.GetIconByExtension(ext, IconSize.Large));
                }
                catch { /* fall through to generic glyph */ }
            }
            if (imageListSmall.Images.ContainsKey(iconKey))
                return iconKey;
        }
        return "FileReference";
    }

    private void OpenFileReference(FileReference fileRef)
    {
        if (string.IsNullOrWhiteSpace(fileRef.FilePath)) return;

        if (!File.Exists(fileRef.FilePath))
        {
            MessageBox.Show(
                $"The file could not be found:\n{fileRef.FilePath}\n\nIt may have been moved, renamed, or deleted.",
                "File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Image file references open in the built-in viewer.
        if (ImageFileHelper.IsImageFile(fileRef.FilePath))
        {
            OpenImageViewer(fileRef.FilePath);
            return;
        }

        try
        {
            // UseShellExecute opens the file with its associated application (by file-type).
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fileRef.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private FileReference? FindFileRef(Project? project, Guid fileRefId)
    {
        if (project == null) return null;
        return FindFileRefIn(project.Children, fileRefId);
    }

    private static FileReference? FindFileRefIn(List<ProjectChild> children, Guid id)
    {
        foreach (var child in children)
        {
            if (child is FileReference fr && fr.Id == id)
                return fr;
            if (child is Collection coll)
            {
                var found = FindFileRefIn(coll.Children, id);
                if (found != null) return found;
            }
        }
        return null;
    }

    private FolderReference? FindFolderRef(Project? project, Guid folderRefId)
    {
        if (project == null) return null;
        return FindFolderRefIn(project.Children, folderRefId);
    }

    private static FolderReference? FindFolderRefIn(List<ProjectChild> children, Guid id)
    {
        foreach (var child in children)
        {
            if (child is FolderReference fr && fr.Id == id)
                return fr;
            if (child is Collection coll)
            {
                var found = FindFolderRefIn(coll.Children, id);
                if (found != null) return found;
            }
        }
        return null;
    }

    private void SelectTreeNodeByTag(string tag)
    {
        var node = FindTreeNode(treeView.Nodes, tag);
        if (node != null)
        {
            treeView.SelectedNode = node;
            node.Expand();
        }
    }

    private static TreeNode? FindTreeNode(TreeNodeCollection nodes, string tag)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag?.ToString() == tag) return node;
            var found = FindTreeNode(node.Nodes, tag);
            if (found != null) return found;
        }
        return null;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < suffixes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {suffixes[order]}";
    }

    private static string GetFileTypeDescription(string extension)
    {
        // Basic file type descriptions — shell integration will enhance this later
        return extension.ToLowerInvariant() switch
        {
            ".txt" => "Text Document",
            ".cs" => "C# Source File",
            ".csproj" => "C# Project File",
            ".sln" => "Visual Studio Solution",
            ".exe" => "Application",
            ".dll" => "Application Extension",
            ".pdf" => "PDF Document",
            ".doc" or ".docx" => "Microsoft Word Document",
            ".xls" or ".xlsx" => "Microsoft Excel Spreadsheet",
            ".png" => "PNG Image",
            ".jpg" or ".jpeg" => "JPEG Image",
            ".gif" => "GIF Image",
            ".bmp" => "Bitmap Image",
            ".mp3" => "MP3 Audio File",
            ".mp4" => "MP4 Video File",
            ".zip" => "ZIP Archive",
            ".json" => "JSON File",
            ".xml" => "XML File",
            ".html" or ".htm" => "HTML Document",
            ".css" => "CSS Stylesheet",
            ".js" => "JavaScript File",
            ".razor" => "Razor Component",
            _ => $"{extension.TrimStart('.').ToUpper()} File"
        };
    }
}
