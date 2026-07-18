using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;

namespace ProjectExplorer.WinForms;

/// <summary>
/// Modeless "Comprehensive Search" window (Ctrl+F / toolbar magnifier). Re-runs
/// SearchService.Search on every keystroke — ProjectManager already holds the full project tree
/// in memory, so this is cheap even for a large/deep nest. Double-click or Enter on a result
/// invokes onResultActivated; this form has no idea how tree navigation works, it just reports
/// which result was picked and leaves jumping to it up to the owner (MainForm).
/// </summary>
public class SearchForm : Form
{
    private readonly ProjectManager _projectManager;
    private readonly Action<SearchResult> _onResultActivated;
    private readonly TextBox txtQuery;
    private readonly ListView listResults;

    public SearchForm(ProjectManager projectManager, Action<SearchResult> onResultActivated)
    {
        _projectManager = projectManager;
        _onResultActivated = onResultActivated;

        Text = "Search";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 480);
        MinimumSize = new Size(420, 300);
        ShowIcon = false;
        ShowInTaskbar = false;

        txtQuery = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11f),
            PlaceholderText = "Search every project by name, description, path, URL, or metadata..."
        };

        // A padded panel hosts the textbox so it doesn't sit flush against the window edges the
        // way a bare Dock=Top control would.
        var topPanel = new Panel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(8, 6, 8, 6) };
        topPanel.Controls.Add(txtQuery);

        listResults = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false
        };
        listResults.Columns.Add("Name", 200);
        listResults.Columns.Add("Type", 90);
        listResults.Columns.Add("Project", 130);
        listResults.Columns.Add("Matched in", 180);

        Controls.Add(listResults);
        Controls.Add(topPanel);

        txtQuery.TextChanged += (s, e) => RunSearch();
        txtQuery.KeyDown += TxtQuery_KeyDown;
        listResults.DoubleClick += (s, e) => ActivateSelected();
        listResults.KeyDown += ListResults_KeyDown;

        Load += (s, e) => txtQuery.Focus();
    }

    private void TxtQuery_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down && listResults.Items.Count > 0)
        {
            listResults.Focus();
            listResults.Items[0].Selected = true;
            listResults.Items[0].Focused = true;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
    }

    private void ListResults_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ActivateSelected();
            e.Handled = true;
        }
    }

    private void RunSearch()
    {
        listResults.Items.Clear();
        var query = txtQuery.Text.Trim();
        if (query.Length == 0) return;

        foreach (var result in SearchService.Search(_projectManager.Projects, query))
        {
            var item = new ListViewItem(result.DisplayText) { Tag = result };
            item.SubItems.Add(TypeLabel(result));
            item.SubItems.Add(result.ProjectName);
            item.SubItems.Add($"{result.MatchedField}: {Truncate(result.MatchedSnippet, 60)}");
            listResults.Items.Add(item);
        }
    }

    private static string TypeLabel(SearchResult result) => result.ChildType switch
    {
        null => "Project",
        ChildType.Collection => "Collection",
        ChildType.FolderReference => "Folder",
        ChildType.WebResource => "Web Resource",
        ChildType.FileReference => "File",
        _ => "Item"
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";

    private void ActivateSelected()
    {
        if (listResults.SelectedItems.Count == 0) return;
        if (listResults.SelectedItems[0].Tag is SearchResult result)
            _onResultActivated(result);
    }
}
