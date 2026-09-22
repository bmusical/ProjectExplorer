using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Sharing;
using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

/// <summary>
/// Sends the selected project to the phase-1 sharing server and shows the code
/// the other computer types into Receive Shared Project.
/// </summary>
public class ShareProjectDialog : Form
{
    private readonly Project _project;
    private readonly string _appVersion;
    private readonly Action<string, string> _saveSettings;
    private readonly NestShareClient _client = NestShareClient.CreateDefault();
    private readonly TextBox _server;
    private readonly TextBox _machine;
    private readonly TextBox _code;
    private readonly TextBox _activity;
    private readonly Button _share;
    private readonly Button _refresh;
    private readonly Button _revoke;
    private readonly Button _copy;

    public ShareProjectDialog(Project project, string serverUrl, string machineLabel, string appVersion, Action<string, string> saveSettings)
    {
        _project = project;
        _appVersion = appVersion;
        _saveSettings = saveSettings;

        DialogTheme.InitDialog(this);
        Text = "Share Project";

        _server = DialogTheme.Input(string.IsNullOrWhiteSpace(serverUrl) ? "http://localhost:5088" : serverUrl, "http://192.168.1.20:5088");
        _machine = DialogTheme.Input(string.IsNullOrWhiteSpace(machineLabel) ? Environment.MachineName : machineLabel, "This computer's name");
        _code = DialogTheme.Input();
        _code.ReadOnly = true;
        _activity = DialogTheme.Input(multiline: true, height: 110);
        _activity.ReadOnly = true;

        var intro = DialogTheme.BodyText(
            "Sends this one project to your sharing server. The other computer uses the code to import a copy. " +
            "Folder and file paths are copied as stored — they are not uploaded. Don't share a project that has secrets in its names or URLs.");
        intro.MaximumSize = new Size(620, 0);

        _share = DialogTheme.PrimaryButton("Share");
        var test = DialogTheme.SecondaryButton("Test connection");
        _copy = DialogTheme.SecondaryButton("Copy code");
        _refresh = DialogTheme.SecondaryButton("Refresh activity");
        _revoke = DialogTheme.SecondaryButton("Revoke code");
        _copy.Enabled = false;
        _refresh.Enabled = false;
        _revoke.Enabled = false;

        var actions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12)
        };
        test.Width = 160;
        actions.Controls.Add(_share);
        actions.Controls.Add(test);

        var followUp = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8)
        };
        _refresh.Width = 160;
        _revoke.Width = 140;
        followUp.Controls.Add(_copy);
        followUp.Controls.Add(_refresh);
        followUp.Controls.Add(_revoke);

        var body = DialogTheme.BuildBody();
        body.Controls.Add(intro);
        body.Controls.Add(DialogTheme.FieldLabel("Project"));
        body.Controls.Add(DialogTheme.BodyText(project.Name));
        body.Controls.Add(DialogTheme.FieldLabel("Sharing server"));
        body.Controls.Add(_server);
        body.Controls.Add(DialogTheme.FieldLabel("This computer"));
        body.Controls.Add(_machine);
        body.Controls.Add(actions);
        body.Controls.Add(DialogTheme.FieldLabel("Share code"));
        body.Controls.Add(_code);
        body.Controls.Add(followUp);
        body.Controls.Add(DialogTheme.FieldLabel("Activity"));
        body.Controls.Add(_activity);

        var close = DialogTheme.SecondaryButton("Close", DialogResult.Cancel);
        DialogTheme.Compose(this, 680,
            DialogTheme.BuildHeader("Share Project", "Hand one project to another computer."),
            body, close);
        CancelButton = close;
        AcceptButton = _share;

        _share.Click += async (_, _) => await ShareAsync();
        test.Click += async (_, _) => await TestAsync();
        _copy.Click += (_, _) => CopyCode();
        _refresh.Click += async (_, _) => await RefreshActivityAsync();
        _revoke.Click += async (_, _) => await RevokeAsync();
    }

    private async Task ShareAsync()
    {
        if (!TrySaveSettings(out var server)) return;
        try
        {
            _share.Enabled = false;
            var egg = NestEggCodec.Create(_project, _machine.Text, _appVersion);
            var published = await _client.PublishAsync(server, egg, _machine.Text.Trim());
            _code.Text = published.Code;
            _copy.Enabled = true;
            _refresh.Enabled = true;
            _revoke.Enabled = true;
            _activity.Text =
                $"On the other computer, choose File ▸ Receive Shared Project and enter {published.Code}." + Environment.NewLine +
                $"The code works until {published.ExpiresUtc.ToLocalTime():g}.";
            await RefreshActivityAsync();
        }
        catch (Exception ex) when (ex is NestShareException or NestEggFormatException)
        {
            MessageBox.Show(this, ex.Message, "Share Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _share.Enabled = true;
        }
    }

    private async Task TestAsync()
    {
        if (!TrySaveSettings(out var server)) return;
        try
        {
            await _client.PingAsync(server);
            MessageBox.Show(this, "The sharing server answered.", "Share Project", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (NestShareException ex)
        {
            MessageBox.Show(this, ex.Message, "Share Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RefreshActivityAsync()
    {
        if (string.IsNullOrWhiteSpace(_code.Text) || !TrySaveSettings(out var server)) return;
        try
        {
            var events = await _client.GetEventsAsync(server, _code.Text);
            var lines = events.Select(e =>
                $"{e.OccurredUtc.ToLocalTime():HH:mm:ss}  {e.EventType}" +
                (string.IsNullOrWhiteSpace(e.MachineLabel) ? "" : "  " + e.MachineLabel) +
                (string.IsNullOrWhiteSpace(e.Detail) ? "" : "  " + e.Detail));
            var log = string.Join(Environment.NewLine, lines);
            if (!string.IsNullOrEmpty(log))
                _activity.Text = "On the other computer, enter " + _code.Text + "." + Environment.NewLine + Environment.NewLine + log;
        }
        catch (NestShareException ex)
        {
            MessageBox.Show(this, ex.Message, "Share Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RevokeAsync()
    {
        if (string.IsNullOrWhiteSpace(_code.Text) || !TrySaveSettings(out var server)) return;
        if (MessageBox.Show(this, "Revoke " + _code.Text + "? The other computer will no longer be able to fetch it.",
                "Share Project", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        try
        {
            await _client.RevokeAsync(server, _code.Text, _machine.Text);
            await RefreshActivityAsync();
        }
        catch (NestShareException ex)
        {
            MessageBox.Show(this, ex.Message, "Share Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyCode()
    {
        if (!string.IsNullOrWhiteSpace(_code.Text))
            Clipboard.SetText(_code.Text);
    }

    private bool TrySaveSettings(out Uri server)
    {
        if (!NestShareClient.TryParseServer(_server.Text, out server))
        {
            MessageBox.Show(this, "Enter the sharing server address, for example http://192.168.1.20:5088.",
                "Share Project", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _saveSettings(_server.Text.Trim(), _machine.Text.Trim());
        return true;
    }
}
