namespace ProjectExplorer.WinForms;

/// <summary>
/// Dialog for adding or editing a web resource (URL with name and description).
///
/// Built from auto-sizing layout panels + an <c>AutoSize</c> form on purpose — see the note in
/// <see cref="InputDialog"/> for why hardcoded pixel positions / a fixed ClientSize get clipped
/// under PerMonitorV2 high-DPI. The "Always open in external browser" checkbox lives in its own
/// row so it can't be pushed off the bottom edge.
/// </summary>
public class WebResourceDialog : Form
{
    private readonly TextBox txtName;
    private readonly TextBox txtUrl;
    private readonly TextBox txtDescription;
    private readonly CheckBox chkOpenExternalOnly;
    private readonly Button btnOK;
    private readonly Button btnCancel;

    public string ResourceName => txtName.Text;
    public string ResourceUrl => txtUrl.Text;
    public string ResourceDescription => txtDescription.Text;
    public bool OpenExternalOnly => chkOpenExternalOnly.Checked;

    public WebResourceDialog(string title = "Add Web Resource", string name = "", string url = "", string description = "", bool openExternalOnly = false)
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
            PlaceholderText = "Leave blank to use hostname",
            Margin = new Padding(3, 0, 3, 10)
        };

        var lblUrl = MakeLabel("URL:");
        txtUrl = new TextBox
        {
            Text = url,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Width = 470,
            PlaceholderText = "https://example.com",
            Margin = new Padding(3, 0, 3, 10)
        };

        var lblDescription = MakeLabel("Description (optional):");
        txtDescription = new TextBox
        {
            Text = description,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Width = 470,
            Height = 60,
            Multiline = true,
            PlaceholderText = "What is this resource for?",
            Margin = new Padding(3, 0, 3, 10)
        };

        chkOpenExternalOnly = new CheckBox
        {
            Text = "Always open in external browser (skip inline preview)",
            Checked = openExternalOnly,
            AutoSize = true,
            Margin = new Padding(3, 0, 3, 0)
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
        layout.Controls.Add(lblUrl);
        layout.Controls.Add(txtUrl);
        layout.Controls.Add(lblDescription);
        layout.Controls.Add(txtDescription);
        layout.Controls.Add(chkOpenExternalOnly);
        layout.Controls.Add(buttonRow);

        this.Controls.Add(layout);
        this.AcceptButton = btnOK;
        this.CancelButton = btnCancel;

        this.Load += (s, e) => txtUrl.Focus();
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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (this.DialogResult == DialogResult.OK)
        {
            if (string.IsNullOrWhiteSpace(txtUrl.Text))
            {
                MessageBox.Show("Please enter a URL.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                e.Cancel = true;
                txtUrl.Focus();
            }
        }
        base.OnFormClosing(e);
    }
}
