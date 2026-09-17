namespace ProjectExplorer.WinForms;

/// <summary>
/// Dialog for adding or editing a file resource (a reference to a single file on disk,
/// with an optional name and description). Works like <see cref="WebResourceDialog"/>
/// but targets a specific file that is opened by its associated application (file-type).
///
/// Built from auto-sizing layout panels + an <c>AutoSize</c> form — see the note in
/// <see cref="InputDialog"/> for why the old fixed-pixel/ClientSize layout clipped under
/// PerMonitorV2 high-DPI.
/// </summary>
public class FileResourceDialog : Form
{
    private readonly TextBox txtName;
    private readonly TextBox txtPath;
    private readonly Button btnBrowse;
    private readonly TextBox txtDescription;
    private readonly Button btnOK;
    private readonly Button btnCancel;

    public string ResourceName => txtName.Text;
    public string ResourceFilePath => txtPath.Text;
    public string ResourceDescription => txtDescription.Text;

    public FileResourceDialog(string title = "Add File", string name = "", string filePath = "", string description = "")
    {
        this.Text = title;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Font = new Font("Segoe UI", 9F);
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.AutoSize = true;
        this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        this.MinimumSize = new Size(520, 0);

        var lblName = MakeLabel("Name (optional):");
        txtName = new TextBox
        {
            Text = name,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Width = 470,
            PlaceholderText = "Leave blank to use the file name",
            Margin = new Padding(3, 0, 3, 10)
        };

        var lblPath = MakeLabel("File:");
        txtPath = new TextBox
        {
            Text = filePath,
            Dock = DockStyle.Fill,
            PlaceholderText = @"C:\path\to\file.ext",
            Margin = new Padding(0, 0, 6, 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        btnBrowse = new Button
        {
            Text = "Browse...",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(84, 27),
            Margin = new Padding(0)
        };
        btnBrowse.Click += BtnBrowse_Click;

        var pathRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 0, 3, 10)
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(txtPath, 0, 0);
        pathRow.Controls.Add(btnBrowse, 1, 0);

        var lblDescription = MakeLabel("Description (optional):");
        txtDescription = new TextBox
        {
            Text = description,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Width = 470,
            Height = 60,
            Multiline = true,
            PlaceholderText = "What is this file for?",
            Margin = new Padding(3, 0, 3, 10)
        };

        btnOK = MakeButton("OK", DialogResult.OK);
        btnCancel = MakeButton("Cancel", DialogResult.Cancel);

        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 14, 0, 0),
            Padding = new Padding(0)
        };
        buttonRow.Controls.Add(btnCancel);
        buttonRow.Controls.Add(btnOK);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 14, 14, 12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(lblName);
        layout.Controls.Add(txtName);
        layout.Controls.Add(lblPath);
        layout.Controls.Add(pathRow);
        layout.Controls.Add(lblDescription);
        layout.Controls.Add(txtDescription);
        layout.Controls.Add(buttonRow);

        this.Controls.Add(layout);
        this.AcceptButton = btnOK;
        this.CancelButton = btnCancel;

        this.Load += (s, e) => txtPath.Focus();
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 0, 3, 3)
    };

    private static Button MakeButton(string text, DialogResult result) => new()
    {
        Text = text,
        DialogResult = result,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(88, 30),
        Margin = new Padding(6, 0, 0, 0)
    };

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select a file to reference",
            CheckFileExists = true,
            Filter = "All files (*.*)|*.*"
        };

        // Pre-seed the dialog with the current directory if a valid path is present.
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
        {
            txtPath.Text = dlg.FileName;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (this.DialogResult == DialogResult.OK)
        {
            if (string.IsNullOrWhiteSpace(txtPath.Text))
            {
                MessageBox.Show("Please select a file.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                txtPath.Focus();
            }
        }
        base.OnFormClosing(e);
    }
}
