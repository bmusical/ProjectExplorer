namespace ProjectExplorer.WinForms;

/// <summary>
/// Simple input dialog for entering names (projects, collections, etc.)
/// </summary>
public class InputDialog : Form
{
    private readonly Label lblPrompt;
    private readonly TextBox txtInput;
    private readonly Button btnOK;
    private readonly Button btnCancel;

    public string InputText => txtInput.Text;

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        this.Text = title;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Size = new Size(400, 160);

        lblPrompt = new Label
        {
            Text = prompt,
            Location = new Point(12, 15),
            AutoSize = true
        };

        txtInput = new TextBox
        {
            Text = defaultValue,
            Location = new Point(12, 40),
            Size = new Size(360, 25)
        };
        txtInput.SelectAll();
        txtInput.Focus();

        btnOK = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(200, 75),
            Size = new Size(80, 30)
        };

        btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(292, 75),
            Size = new Size(80, 30)
        };

        this.Controls.AddRange(new Control[] { lblPrompt, txtInput, btnOK, btnCancel });
        this.AcceptButton = btnOK;
        this.CancelButton = btnCancel;

        this.Load += (s, e) => txtInput.SelectAll();
    }
}
