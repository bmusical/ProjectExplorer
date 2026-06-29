using System.IO;
using System.Text.Json;
using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;
using ProjectExplorer.Shell;
using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

public partial class MainForm : Form
{
    private readonly ProjectManager _projectManager;
    private readonly IShellIconProvider _shellIconProvider;

    // Navigation history
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    // Current state
    private Project? _currentProject;
    private string _currentPath = string.Empty;

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
    private const string TagRealFolder = "RealFolder:";

    public MainForm(ProjectManager projectManager, IShellIconProvider shellIconProvider)
    {
        _projectManager = projectManager;
        _shellIconProvider = shellIconProvider;

        InitializeComponent();

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

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Configure SplitContainer constraints here - the form is now laid out
        // and splitMain has its actual width, so validation won't fail.
        // Order matters: set SplitterDistance to a safe value FIRST, then min sizes.
        splitMain.SplitterDistance = Math.Max(300, splitMain.Width / 4);
        splitMain.Panel1MinSize = 150;
        splitMain.Panel2MinSize = 150;
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
        try
        {
            // Project icon (use folder icon as placeholder)
            var folderIcon = _shellIconProvider.GetFolderIcon(IconSize.Small);
            imageListSmall.Images.Add("Folder", folderIcon);
            imageListSmall.Images.Add("FolderOpen", _shellIconProvider.GetFolderIcon(IconSize.Small, open: true));

            // Large icons
            imageListLarge.Images.Add("Folder", _shellIconProvider.GetFolderIcon(IconSize.Large));
            imageListLarge.Images.Add("FolderOpen", _shellIconProvider.GetFolderIcon(IconSize.Large, open: true));

            // Project and Collection placeholders (use different system icons)
            imageListSmall.Images.Add("Project", SystemIcons.Application.ToBitmap());
            imageListSmall.Images.Add("Collection", SystemIcons.WinLogo.ToBitmap());
            imageListLarge.Images.Add("Project", SystemIcons.Application.ToBitmap());
            imageListLarge.Images.Add("Collection", SystemIcons.WinLogo.ToBitmap());

            // WebResource icon (use globe/network icon)
            imageListSmall.Images.Add("WebResource", SystemIcons.Shield.ToBitmap());
            imageListLarge.Images.Add("WebResource", SystemIcons.Shield.ToBitmap());
        }
        catch
        {
            // If shell icon loading fails (e.g., on non-Windows), use system icons as fallback
            imageListSmall.Images.Add("Folder", SystemIcons.Information.ToBitmap());
            imageListSmall.Images.Add("FolderOpen", SystemIcons.Information.ToBitmap());
            imageListSmall.Images.Add("Project", SystemIcons.Application.ToBitmap());
            imageListSmall.Images.Add("Collection", SystemIcons.WinLogo.ToBitmap());
            imageListSmall.Images.Add("WebResource", SystemIcons.Shield.ToBitmap());
            imageListLarge.Images.Add("Folder", SystemIcons.Information.ToBitmap());
            imageListLarge.Images.Add("FolderOpen", SystemIcons.Information.ToBitmap());
            imageListLarge.Images.Add("Project", SystemIcons.Application.ToBitmap());
            imageListLarge.Images.Add("Collection", SystemIcons.WinLogo.ToBitmap());
            imageListLarge.Images.Add("WebResource", SystemIcons.Shield.ToBitmap());
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
        foreach (var project in _projectManager.Projects)
        {
            var item = new ListViewItem(project.Name, "Project")
            {
                Tag = TagProject + project.Id
            };
            item.SubItems.Add("");
            item.SubItems.Add("Project");
            item.SubItems.Add(project.Modified.ToString("g"));
            listView.Items.Add(item);
        }
    }

    private void PopulateProjectContents()
    {
        if (_currentProject == null) return;

        foreach (var child in _currentProject.Children.OrderBy(c => c.SortOrder))
        {
            if (child is Collection coll)
            {
                var item = new ListViewItem(coll.Name, "Collection")
                {
                    Tag = TagCollection + $"{_currentProject.Id}:{coll.Id}"
                };
                item.SubItems.Add(coll.Description ?? "");
                item.SubItems.Add("Collection");
                item.SubItems.Add("");
                listView.Items.Add(item);
            }
            else if (child is FolderReference fr)
            {
                var item = new ListViewItem(fr.EffectiveName, "Folder")
                {
                    Tag = TagFolderRef + $"{_currentProject.Id}:{fr.Id}"
                };
                item.SubItems.Add(fr.Description ?? "");
                item.SubItems.Add("Folder Reference");
                item.SubItems.Add("");
                listView.Items.Add(item);
            }
            else if (child is WebResource wr)
            {
                var item = new ListViewItem(wr.EffectiveName, "WebResource")
                {
                    Tag = TagWebResource + $"{_currentProject.Id}:{wr.Id}"
                };
                item.SubItems.Add(wr.Description ?? "");
                item.SubItems.Add("Web Resource");
                item.SubItems.Add(wr.Url);
                listView.Items.Add(item);
            }
        }
    }

    private void PopulateCollectionContents(Guid collectionId)
    {
        if (_currentProject == null) return;
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
                item.SubItems.Add(coll.Description ?? "");
                item.SubItems.Add("Collection");
                item.SubItems.Add("");
                listView.Items.Add(item);
            }
            else if (child is FolderReference fr)
            {
                var item = new ListViewItem(fr.EffectiveName, "Folder")
                {
                    Tag = TagFolderRef + $"{_currentProject.Id}:{fr.Id}"
                };
                item.SubItems.Add(fr.Description ?? "");
                item.SubItems.Add("Folder Reference");
                item.SubItems.Add("");
                listView.Items.Add(item);
            }
            else if (child is WebResource wr)
            {
                var item = new ListViewItem(wr.EffectiveName, "WebResource")
                {
                    Tag = TagWebResource + $"{_currentProject.Id}:{wr.Id}"
                };
                item.SubItems.Add(wr.Description ?? "");
                item.SubItems.Add("Web Resource");
                item.SubItems.Add(wr.Url);
                listView.Items.Add(item);
            }
        }
    }

    private void PopulateFileList(string path)
    {
        if (!Directory.Exists(path)) return;

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
            }
        }
        catch (UnauthorizedAccessException)
        {
            listView.Items.Add(new ListViewItem("(Access denied)") { ForeColor = Color.Gray });
        }
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
        _currentPath = path;
        listView.BeginUpdate();
        listView.Items.Clear();
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
        var count = listView.Items.Count;
        lblStatus.Text = count == 1 ? "1 item" : $"{count} items";
        if (!string.IsNullOrEmpty(_currentPath))
            lblStatus.Text += $"  |  {_currentPath}";
    }

    private void UpdateToolbarButtons()
    {
        // Enable Explorer button when viewing a real folder path
        btnOpenExplorer.Enabled = !string.IsNullOrEmpty(_currentPath) && Directory.Exists(_currentPath);

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
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
            }
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
        if (e.Button == MouseButtons.Right)
        {
            // Future: show shell context menu for real files
        }
    }

    // ── View Modes ──

    private enum AppView { Details, LargeIcon, SmallIcon, List, Tile }

    private void SetViewMode(AppView mode)
    {
        listView.View = mode switch
        {
            AppView.Details => System.Windows.Forms.View.Details,
            AppView.LargeIcon => System.Windows.Forms.View.LargeIcon,
            AppView.SmallIcon => System.Windows.Forms.View.SmallIcon,
            AppView.List => System.Windows.Forms.View.List,
            AppView.Tile => System.Windows.Forms.View.Tile,
            _ => System.Windows.Forms.View.Details
        };

        // Update menu checkmarks
        menuViewDetails.Checked = mode == AppView.Details;
        menuViewLargeIcons.Checked = mode == AppView.LargeIcon;
        menuViewSmallIcons.Checked = mode == AppView.SmallIcon;
        menuViewList.Checked = mode == AppView.List;
        menuViewTile.Checked = mode == AppView.Tile;
    }

    // ── Menu Actions ──

    private async void MenuFileNewProject_Click(object? sender, EventArgs e)
    {
        using var dialog = new InputDialog("Create New Project", "Project name:", "New Project");
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var name = dialog.InputText.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                await _projectManager.CreateProjectAsync(name);
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

        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a folder to add to the project",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await _projectManager.AddFolderReferenceAsync(_currentProject.Id, dialog.SelectedPath);
            RefreshTreeView();
        }
    }

    private async Task ShowAddWebResourceDialog(Guid projectId, Guid? parentCollectionId)
    {
        using var dialog = new WebResourceDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var url = dialog.ResourceUrl.Trim();
            var name = string.IsNullOrWhiteSpace(dialog.ResourceName) ? null : dialog.ResourceName.Trim();
            var description = string.IsNullOrWhiteSpace(dialog.ResourceDescription) ? null : dialog.ResourceDescription.Trim();

            if (!string.IsNullOrEmpty(url))
            {
                await _projectManager.AddWebResourceAsync(projectId, url, name, description, parentCollectionId);
                RefreshTreeView();
            }
        }
    }

    // ── Context Menu ──

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
            menu.Items.Add("New Collection...", null, MenuProjectNewCollection_Click);
            menu.Items.Add("Add Folder...", null, MenuProjectAddFolder_Click);
            menu.Items.Add("Add Web Resource...", null, async (s, e) =>
            {
                var projectId = Guid.Parse(tag.Substring(TagProject.Length));
                await ShowAddWebResourceDialog(projectId, null);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Rename", null, (s, e) => node.BeginEdit());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Delete Project", null, async (s, e) =>
            {
                var projectId = Guid.Parse(tag.Substring(TagProject.Length));
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

        if (tag.StartsWith(TagCollection))
        {
            var parts = tag.Substring(TagCollection.Length).Split(':');
            var projectId = Guid.Parse(parts[0]);
            var collectionId = Guid.Parse(parts[1]);

            menu.Items.Add("New Sub-Collection...", null, async (s, e) =>
            {
                using var dlg = new InputDialog("Create Sub-Collection", "Name:", "New Collection");
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    var name = dlg.InputText.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        await _projectManager.CreateCollectionAsync(projectId, name, collectionId);
                        RefreshTreeView();
                    }
                }
            });
            menu.Items.Add("Add Folder...", null, async (s, e) =>
            {
                using var dlg = new FolderBrowserDialog { Description = "Select folder to add" };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    await _projectManager.AddFolderReferenceAsync(projectId, dlg.SelectedPath, collectionId);
                    RefreshTreeView();
                }
            });
            menu.Items.Add("Add Web Resource...", null, async (s, e) =>
            {
                await ShowAddWebResourceDialog(projectId, collectionId);
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Rename", null, (s, e) => node.BeginEdit());
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

        if (tag.StartsWith(TagFolderRef))
        {
            var parts = tag.Substring(TagFolderRef.Length).Split(':');
            var projectId = Guid.Parse(parts[0]);
            var folderRefId = Guid.Parse(parts[1]);

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
                            projectId,
                            folderRefId,
                            newDescription: string.IsNullOrEmpty(description) ? null : description
                        );
                        RefreshTreeView();
                    }
                }
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
            menu.Items.Add("Open in Explorer", null, (s, e) =>
            {
                var project = _projectManager.GetProject(projectId);
                var fr = FindFolderRef(project, folderRefId);
                if (fr != null && Directory.Exists(fr.RealPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fr.RealPath) { UseShellExecute = true });
                }
            });
        }

        if (tag.StartsWith(TagWebResource))
        {
            var parts = tag.Substring(TagWebResource.Length).Split(':');
            var projectId = Guid.Parse(parts[0]);
            var resourceId = Guid.Parse(parts[1]);

            menu.Items.Add("Edit...", null, async (s, e) =>
            {
                var project = _projectManager.GetProject(projectId);
                var wr = FindWebResource(project, resourceId);
                if (wr != null)
                {
                    using var dlg = new WebResourceDialog("Edit Web Resource", wr.DisplayName ?? "", wr.Url, wr.Description ?? "");
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        await _projectManager.UpdateWebResourceAsync(
                            projectId,
                            resourceId,
                            string.IsNullOrWhiteSpace(dlg.ResourceName) ? null : dlg.ResourceName.Trim(),
                            dlg.ResourceUrl.Trim(),
                            string.IsNullOrWhiteSpace(dlg.ResourceDescription) ? null : dlg.ResourceDescription.Trim()
                        );
                        RefreshTreeView();
                    }
                }
            });
            menu.Items.Add("Launch", null, (s, e) => LaunchWebResource(tag));
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

        if (tag.StartsWith(TagRealFolder))
        {
            var path = tag.Substring(TagRealFolder.Length);
            menu.Items.Add("Open in Explorer", null, (s, e) =>
            {
                if (Directory.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
            });
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
