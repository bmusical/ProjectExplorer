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

    // Navigation history
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    // Current state
    private Project? _currentProject;
    private string _currentPath = string.Empty;

    // The FileReference currently shown in filePreviewPanel, if any (null when listView is showing).
    private FileReference? _currentPreviewFileRef;

    // Drag-drop state
    private TreeNode? _dragHighlightNode;

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
                    LicenseManager licenseManager, LicenseInfo license)
    {
        _projectManager         = projectManager;
        _shellIconProvider      = shellIconProvider;
        _shellThumbnailProvider = shellThumbnailProvider;
        _shellPropertiesProvider = shellPropertiesProvider;
        _licenseManager         = licenseManager;
        _license                = license;

        InitializeComponent();

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
                ToolTipText = folderRef.Description ?? folderRef.RealPath,
                ImageIndex = GetImageIndex("Folder"),
                SelectedImageIndex = GetImageIndex("FolderOpen")
            };
            parent.Nodes.Add(refNode);

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
                ToolTipText = webResource.Description ?? webResource.Url,
                ImageIndex = GetImageIndex("WebResource"),
                SelectedImageIndex = GetImageIndex("WebResource")
            };
            parent.Nodes.Add(webNode);
        }
        else if (child is FileReference fileRef)
        {
            var fileNode = new TreeNode(fileRef.EffectiveName)
            {
                Tag = TagFileRef + $"{project.Id}:{fileRef.Id}",
                ToolTipText = fileRef.Description ?? fileRef.FilePath,
                ImageIndex = GetFileRefImageIndex(fileRef),
                SelectedImageIndex = GetFileRefImageIndex(fileRef)
            };
            parent.Nodes.Add(fileNode);
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
                ShowFileReferencePreview(fileRef);
            }

            UpdateAddressBar();
            UpdateStatusBar();
            UpdateToolbarButtons();
            return;
        }

        HideFileReferencePreview();

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
        txtAddress.Text = _currentPath;
    }

    private void UpdateStatusBar()
    {
        if (_currentPreviewFileRef != null)
        {
            lblStatus.Text = $"{_currentPreviewFileRef.EffectiveName}  |  {_currentPreviewFileRef.FilePath}";
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
            // Launch web resource in default browser
            LaunchWebResource(tag);
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

    private void AddFolderReferenceMenuItems(ContextMenuStrip menu, Guid projectId, Guid folderRefId)
    {
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
        menu.Items.Add("Launch", null, (s, e) => LaunchWebResource(tag));
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
        SaveTreeState();
        base.OnFormClosing(e);
    }

    // ── Drag and Drop ──

    private void TreeView_ItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not TreeNode node) return;
        var tag = node.Tag?.ToString() ?? "";
        if (!tag.StartsWith(TagCollection) && !tag.StartsWith(TagFolderRef) && !tag.StartsWith(TagWebResource))
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
            return;
        }

        var pt = treeView.PointToClient(new Point(e.X, e.Y));
        var targetNode = treeView.GetNodeAt(pt);

        if (targetNode == null || !IsValidDropTarget(draggedNode, targetNode))
        {
            e.Effect = DragDropEffects.None;
            ClearDragHighlight();
            return;
        }

        e.Effect = DragDropEffects.Move;

        if (!ReferenceEquals(targetNode, _dragHighlightNode))
        {
            ClearDragHighlight();
            _dragHighlightNode = targetNode;
            targetNode.BackColor = SystemColors.Highlight;
            targetNode.ForeColor = SystemColors.HighlightText;
        }
    }

    private void TreeView_DragLeave(object? sender, EventArgs e) => ClearDragHighlight();

    private async void TreeView_DragDrop(object? sender, DragEventArgs e)
    {
        ClearDragHighlight();

        if (e.Data?.GetData(typeof(TreeNode)) is not TreeNode draggedNode) return;

        var pt = treeView.PointToClient(new Point(e.X, e.Y));
        var targetNode = treeView.GetNodeAt(pt);
        if (targetNode == null || !IsValidDropTarget(draggedNode, targetNode)) return;

        var dragTag = draggedNode.Tag?.ToString() ?? "";
        var targetTag = targetNode.Tag?.ToString() ?? "";

        var projectId = GetProjectIdFromTag(dragTag);
        var childId = GetChildIdFromTag(dragTag);
        var newParentId = GetCollectionIdFromTag(targetTag);

        try
        {
            await _projectManager.MoveChildAsync(projectId, childId, newParentId);
            RefreshTreeView();
            SelectTreeNodeByTag(dragTag);
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

    private bool IsValidDropTarget(TreeNode draggedNode, TreeNode targetNode)
    {
        if (ReferenceEquals(draggedNode, targetNode)) return false;

        var dragTag = draggedNode.Tag?.ToString() ?? "";
        var targetTag = targetNode.Tag?.ToString() ?? "";

        if (!targetTag.StartsWith(TagCollection) && !targetTag.StartsWith(TagProject)) return false;
        if (!dragTag.StartsWith(TagCollection) && !dragTag.StartsWith(TagFolderRef) && !dragTag.StartsWith(TagWebResource)) return false;

        if (GetProjectIdFromTag(dragTag) != GetProjectIdFromTag(targetTag)) return false;

        // Prevent dropping onto the current parent (already there — no move would happen)
        if (ReferenceEquals(targetNode, draggedNode.Parent)) return false;

        // Prevent dropping onto a descendant of the dragged node
        var ancestor = targetNode.Parent;
        while (ancestor != null)
        {
            if (ReferenceEquals(ancestor, draggedNode)) return false;
            ancestor = ancestor.Parent;
        }

        return true;
    }

    private static Guid GetProjectIdFromTag(string tag)
    {
        if (tag.StartsWith(TagProject)) return Guid.Parse(tag[TagProject.Length..]);
        if (tag.StartsWith(TagCollection)) return Guid.Parse(tag[TagCollection.Length..].Split(':')[0]);
        if (tag.StartsWith(TagFolderRef)) return Guid.Parse(tag[TagFolderRef.Length..].Split(':')[0]);
        if (tag.StartsWith(TagWebResource)) return Guid.Parse(tag[TagWebResource.Length..].Split(':')[0]);
        return Guid.Empty;
    }

    private static Guid GetChildIdFromTag(string tag)
    {
        if (tag.StartsWith(TagCollection)) return Guid.Parse(tag[TagCollection.Length..].Split(':')[1]);
        if (tag.StartsWith(TagFolderRef)) return Guid.Parse(tag[TagFolderRef.Length..].Split(':')[1]);
        if (tag.StartsWith(TagWebResource)) return Guid.Parse(tag[TagWebResource.Length..].Split(':')[1]);
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

        if (webResource != null && !string.IsNullOrWhiteSpace(webResource.Url))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(webResource.Url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch URL: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
