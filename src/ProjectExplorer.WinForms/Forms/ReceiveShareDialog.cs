using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Sharing;
using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

/// <summary>
/// Pulls a Nest Egg by code and imports it as a new project. The caller persists
/// the project and refreshes the tree when <see cref="Imported"/> is set.
/// </summary>
public class ReceiveShareDialog : Form
{
    private readonly string _initialMachine;
    private readonly Action<string, string> _saveSettings;
    private readonly Func<NestEggDocument, string, string, Task<Project>> _importAsync;
    private readonly NestShareClient _client = NestShareClient.CreateDefault();
    private readonly TextBox _server;
    private readonly TextBox _code;
    private readonly TextBox _machine;
    private readonly TextBox _summary;
    private readonly Button _import;
    private NestEggDocument? _egg;

    public Project? Imported { get; private set; }

    public ReceiveShareDialog(string serverUrl, string machineLabel, Action<string, string> saveSettings,
        Func<NestEggDocument, string, string, Task<Project>> importAsync)
    {
        _initialMachine = string.IsNullOrWhiteSpace(machineLabel) ? Environment.MachineName : machineLabel;
        _saveSettings = saveSettings;
        _importAsync = importAsync;

        DialogTheme.InitDialog(this);
        Text = "Receive Shared Project";

        _server = DialogTheme.Input(string.IsNullOrWhiteSpace(serverUrl) ? "http://localhost:5088" : serverUrl, "http://192.168.1.20:5088");
        _code = DialogTheme.Input("", "ABCD-EFGH");
        _machine = DialogTheme.Input(_initialMachine, "This computer's name");
        _summary = DialogTheme.Input(multiline: true, height: 180);
        _summary.ReadOnly = true;

        var intro = DialogTheme.BodyText(
            "Imports a copy of a project someone shared with a code. Paths stay exactly as the sender stored them, " +
            "so a folder that exists only on their computer will show as unavailable here. Web addresses should still open.");
        intro.MaximumSize = new Size(620, 0);

        var preview = DialogTheme.SecondaryButton("Preview");
        _import = DialogTheme.PrimaryButton("Import");
        var test = DialogTheme.SecondaryButton("Test connection");
        _import.Enabled = false;

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        test.Width = 160;
        actions.Controls.Add(preview);
        actions.Controls.Add(_import);
        actions.Controls.Add(test);

        var body = DialogTheme.BuildBody();
        body.Controls.Add(intro);
        body.Controls.Add(DialogTheme.FieldLabel("Sharing server"));
        body.Controls.Add(_server);
        body.Controls.Add(DialogTheme.FieldLabel("Share code"));
        body.Controls.Add(_code);
        body.Controls.Add(DialogTheme.FieldLabel("This computer"));
        body.Controls.Add(_machine);
        body.Controls.Add(actions);
        body.Controls.Add(DialogTheme.FieldLabel("What will arrive"));
        body.Controls.Add(_summary);

        var close = DialogTheme.SecondaryButton("Close", DialogResult.Cancel);
        DialogTheme.Compose(this, 680,
            DialogTheme.BuildHeader("Receive Shared Project", "Import a project another computer sent."),
            body, close);
        CancelButton = close;

        preview.Click += async (_, _) => await PreviewAsync();
        _import.Click += async (_, _) => await ImportAsync();
        test.Click += async (_, _) => await TestAsync();
        _code.TextChanged += (_, _) =>
        {
            _egg = null;
            _import.Enabled = false;
        };
    }

    private async Task PreviewAsync()
    {
        if (!TrySaveSettings(out var server) || !HasCode()) return;
        try
        {
            var preview = await _client.PreviewAsync(server, _code.Text, _machine.Text);
            _egg = await _client.FetchAsync(server, _code.Text, _machine.Text);
            _summary.Text = Describe(preview);
            _import.Enabled = true;
        }
        catch (Exception ex) when (ex is NestShareException or NestEggFormatException)
        {
            _egg = null;
            _import.Enabled = false;
            _summary.Text = ex.Message;
        }
    }

    private async Task ImportAsync()
    {
        if (_egg == null || !TrySaveSettings(out _) || !HasCode()) return;
        try
        {
            _import.Enabled = false;
            Imported = await _importAsync(_egg, _code.Text.Trim(), _machine.Text.Trim());
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _import.Enabled = true;
            MessageBox.Show(this, ex.Message, "Receive Shared Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task TestAsync()
    {
        if (!TrySaveSettings(out var server)) return;
        try
        {
            await _client.PingAsync(server);
            MessageBox.Show(this, "The sharing server answered.", "Receive Shared Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (NestShareException ex)
        {
            MessageBox.Show(this, ex.Message, "Receive Shared Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string Describe(SharePreview preview)
    {
        var lines = new List<string>
        {
            $"Project: {preview.ProjectName}",
            $"From: {preview.SenderLabel}",
            $"Collections: {preview.CollectionCount}",
            $"Folders: {preview.FolderCount}",
            $"Files: {preview.FileCount}",
            $"Web resources: {preview.WebCount}",
            ""
        };
        Append("Folder paths", preview.FolderPaths, lines);
        Append("File paths", preview.FilePaths, lines);
        Append("Web addresses", preview.Urls, lines);
        if (preview.PathsTruncated)
            lines.Add("The preview lists the first few paths. Import brings the rest.");
        lines.Add("Import adds this as a new project. It does not replace anything already on this computer.");
        return string.Join(Environment.NewLine, lines);
    }

    private static void Append(string title, IReadOnlyList<string> values, List<string> lines)
    {
        if (values.Count == 0) return;
        lines.Add(title + ":");
        lines.AddRange(values.Select(v => "  " + (string.IsNullOrWhiteSpace(v) ? "(blank)" : v)));
        lines.Add("");
    }

    private bool TrySaveSettings(out Uri server)
    {
        if (!NestShareClient.TryParseServer(_server.Text, out server))
        {
            MessageBox.Show(this, "Enter the sharing server address, for example http://192.168.1.20:5088.",
                "Receive Shared Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _saveSettings(_server.Text.Trim(), _machine.Text.Trim());
        return true;
    }

    private bool HasCode()
    {
        if (!string.IsNullOrWhiteSpace(_code.Text)) return true;
        MessageBox.Show(this, "Enter the share code from the other computer.",
            "Receive Shared Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }
}
