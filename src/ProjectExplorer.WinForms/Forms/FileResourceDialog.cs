namespace ProjectExplorer.WinForms;

/// <summary>
/// Dialog for adding or editing a file resource (a reference to a single file on disk,
/// with an optional name and description). Works like <see cref="WebResourceDialog"/>
/// but targets a specific file that is opened by its associated application (file-type).
/// </summary>
public class FileResourceDialog : Form
{
    private readonly Label lblName;
    private readonly TextBox txtName;
    private readonly Label lblPath;
    private readonly TextBox txtPath;
    private readonly Button btnBrowse;
    private readonly Label lblDescription;
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
        this.StartPosition = FormStartPosition.CenterParent;
        this.Size = new Size(480, 300);

        lblName = new Label
        {
            Text = "Name (optional):",
            Location = new Point(12, 15),
            AutoSize = true
        };

        txtName = new TextBox
        {
            Text = name,
            Location = new Point(12, 38),
            Size = new Size(440, 25),
            PlaceholderText = "Leave blank to use the file name"
        };

        lblPath = new Label
        {
            Text = "File:",
            Location = new Point(12, 73),
            AutoSize = true
        };

        txtPath = new TextBox
        {
            Text = filePath,
            Location = new Point(12, 96),
            Size = new Size(350, 25),
            PlaceholderText = @"C:\path\to\file.ext"
        };

        btnBrowse = new Button
        {
            Text = "Browse...",
            Location = new Point(372, 95),
            Size = new Size(80, 27)
        };
        btnBrowse.Click += BtnBrowse_Click;

        lblDescription = new Label
        {
            Text = "Description (optional):",
            Location = new Point(12, 131),
            AutoSize = true
        };

        txtDescription = new TextBox
        {
            Text = description,
            Location = new Point(12, 154),
            Size = new Size(440, 50),
            Multiline = true,
            PlaceholderText = "What is this file for?"
        };

        btnOK = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(280, 220),
            Size = new Size(80, 30)
        };

        btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(372, 220),
            Size = new Size(80, 30)
        };

        this.Controls.AddRange(new Control[] {
            lblName, txtName,
            lblPath, txtPath, btnBrowse,
            lblDescription, txtDescription,
            btnOK, btnCancel
        });
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
