using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

/// <summary>
/// Dialog for adding or editing a file resource (a reference to a single file on disk,
/// with an optional name and description). Built from the shared <see cref="DialogTheme"/> to
/// match <see cref="WebResourceDialog"/> and sized from its measured content.
/// </summary>
public class FileResourceDialog : Form
{
    private readonly TextBox txtName;
    private readonly TextBox txtPath;
    private readonly TextBox txtDescription;

    public string ResourceName => txtName.Text;
    public string ResourceFilePath => txtPath.Text;
    public string ResourceDescription => txtDescription.Text;

    public FileResourceDialog(string title = "Add File", string name = "", string filePath = "", string description = "")
    {
        DialogTheme.InitDialog(this);
        this.Text = title;

        txtName = DialogTheme.Input(name, "Leave blank to use the file name");

        // File row: path textbox + Browse button, side by side.
        txtPath = new TextBox
        {
            Text = filePath,
            Font = DialogTheme.InputFont,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            PlaceholderText = @"C:\path\to\file.ext",
            Margin = new Padding(2, 1, 8, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        var btnBrowse = DialogTheme.SecondaryButton("Browse…");
        btnBrowse.Size = new Size(112, 30);
        btnBrowse.Margin = new Padding(0);
        btnBrowse.Click += BtnBrowse_Click;

        var pathRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 18),
            BackColor = DialogTheme.Surface
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(txtPath, 0, 0);
        pathRow.Controls.Add(btnBrowse, 1, 0);

        txtDescription = DialogTheme.Input(description, "What is this file for?", multiline: true);

        var btnOK = DialogTheme.PrimaryButton("OK", DialogResult.OK);
        var btnCancel = DialogTheme.SecondaryButton("Cancel", DialogResult.Cancel);

        var body = DialogTheme.BuildBody();
        body.Controls.Add(DialogTheme.FieldLabel("Name (optional)"));
        body.Controls.Add(txtName);
        body.Controls.Add(DialogTheme.FieldLabel("File"));
        body.Controls.Add(pathRow);
        body.Controls.Add(DialogTheme.FieldLabel("Description (optional)"));
        body.Controls.Add(txtDescription);

        DialogTheme.Compose(this, width: 700,
            DialogTheme.BuildHeader(title, "Reference a single file, opened with its default app."),
            body, btnOK, btnCancel);

        this.AcceptButton = btnOK;
        this.CancelButton = btnCancel;

        this.Load += (s, e) => txtPath.Focus();
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select a file to reference",
            CheckFileExists = true,
            Filter = "All files (*.*)|*.*"
        };

        if (!string.IsNullOrWhiteSpace(txtPath.Text))
        {
            try
            {
                var dir = Path.GetDirectoryName(txtPath.Text);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    dlg.InitialDirectory = dir;
            }
            catch { /* ignore malformed paths */ }
        }

        if (dlg.ShowDialog(this) == DialogResult.OK)
            txtPath.Text = dlg.FileName;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (this.DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(txtPath.Text))
        {
            MessageBox.Show("Please select a file.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            txtPath.Focus();
        }
        base.OnFormClosing(e);
    }
}
