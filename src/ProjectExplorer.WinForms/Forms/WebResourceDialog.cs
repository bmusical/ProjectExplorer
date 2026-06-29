namespace ProjectExplorer.WinForms;

/// <summary>
/// Dialog for adding or editing a web resource (URL with name and description)
/// </summary>
public class WebResourceDialog : Form
{
    private readonly Label lblName;
    private readonly TextBox txtName;
    private readonly Label lblUrl;
    private readonly TextBox txtUrl;
    private readonly Label lblDescription;
    private readonly TextBox txtDescription;
    private readonly Button btnOK;
    private readonly Button btnCancel;

    public string ResourceName => txtName.Text;
    public string ResourceUrl => txtUrl.Text;
    public string ResourceDescription => txtDescription.Text;

    public WebResourceDialog(string title = "Add Web Resource", string name = "", string url = "", string description = "")
    {
        this.Text = title;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Size = new Size(480, title == "Edit Web Resource" ? 290 : 280);

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
            PlaceholderText = "Leave blank to use hostname"
        };

        lblUrl = new Label
        {
            Text = "URL:",
            Location = new Point(12, 73),
            AutoSize = true
        };

        txtUrl = new TextBox
        {
            Text = url,
            Location = new Point(12, 96),
            Size = new Size(440, 25),
            PlaceholderText = "https://example.com"
        };

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
            PlaceholderText = "What is this resource for?"
        };

        btnOK = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(280, 215),
            Size = new Size(80, 30)
        };

        btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(372, 215),
            Size = new Size(80, 30)
        };

        this.Controls.AddRange(new Control[] {
            lblName, txtName,
            lblUrl, txtUrl,
            lblDescription, txtDescription,
            btnOK, btnCancel
        });
        this.AcceptButton = btnOK;
        this.CancelButton = btnCancel;

        this.Load += (s, e) => txtUrl.Focus();
    }

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
