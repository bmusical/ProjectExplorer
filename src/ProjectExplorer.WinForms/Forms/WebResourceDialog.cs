using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

/// <summary>
/// Dialog for adding or editing a web resource (URL with name and description).
/// Built from the shared <see cref="DialogTheme"/>: branded header, spacious fields, and a
/// bottom-docked accent button bar. Sized from measured content so nothing (including the
/// "open external" checkbox and the button row) is ever clipped.
/// </summary>
public class WebResourceDialog : Form
{
    private readonly TextBox txtName;
    private readonly TextBox txtUrl;
    private readonly TextBox txtDescription;
    private readonly CheckBox chkOpenExternalOnly;

    public string ResourceName => txtName.Text;
    public string ResourceUrl => txtUrl.Text;
    public string ResourceDescription => txtDescription.Text;
    public bool OpenExternalOnly => chkOpenExternalOnly.Checked;

    public WebResourceDialog(string title = "Add Web Resource", string name = "", string url = "", string description = "", bool openExternalOnly = false)
    {
        DialogTheme.InitDialog(this);
        this.Text = title;

        txtName = DialogTheme.Input(name, "Leave blank to use the hostname");
        txtUrl = DialogTheme.Input(url, "https://example.com");
        txtDescription = DialogTheme.Input(description, "What is this resource for?", multiline: true);

        chkOpenExternalOnly = new CheckBox
        {
            Text = "Always open in an external browser (skip the inline preview)",
            Checked = openExternalOnly,
            AutoSize = true,
            Font = DialogTheme.BodyFont,
            ForeColor = DialogTheme.TextPrimary,
            Margin = new Padding(2, 2, 2, 2)
        };

        var btnOK = DialogTheme.PrimaryButton("OK", DialogResult.OK);
        var btnCancel = DialogTheme.SecondaryButton("Cancel", DialogResult.Cancel);

        var body = DialogTheme.BuildBody();
        body.Controls.Add(DialogTheme.FieldLabel("Name (optional)"));
        body.Controls.Add(txtName);
        body.Controls.Add(DialogTheme.FieldLabel("URL"));
        body.Controls.Add(txtUrl);
        body.Controls.Add(DialogTheme.FieldLabel("Description (optional)"));
        body.Controls.Add(txtDescription);
        body.Controls.Add(chkOpenExternalOnly);

        DialogTheme.Compose(this, width: 700,
            DialogTheme.BuildHeader(title, "Bring a project-related web page into your nest."),
            body, btnOK, btnCancel);

        this.AcceptButton = btnOK;
        this.CancelButton = btnCancel;

        this.Load += (s, e) => txtUrl.Focus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (this.DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(txtUrl.Text))
        {
            MessageBox.Show("Please enter a URL.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
            txtUrl.Focus();
        }
        base.OnFormClosing(e);
    }
}
