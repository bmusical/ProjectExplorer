using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

/// <summary>
/// Simple input dialog for entering names/descriptions (projects, collections, etc.).
/// Built from the shared <see cref="DialogTheme"/> so it matches the rest of the app's dialogs
/// and is sized from its measured content (never clipped, at any DPI).
/// </summary>
public class InputDialog : Form
{
    private readonly TextBox txtInput;

    public string InputText => txtInput.Text;

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        DialogTheme.InitDialog(this);
        this.Text = title;

        txtInput = DialogTheme.Input(defaultValue);

        var btnOK = DialogTheme.PrimaryButton("OK", DialogResult.OK);
        var btnCancel = DialogTheme.SecondaryButton("Cancel", DialogResult.Cancel);

        var body = DialogTheme.BuildBody();
        body.Controls.Add(DialogTheme.FieldLabel(prompt));
        body.Controls.Add(txtInput);

        DialogTheme.Compose(this, width: 580, DialogTheme.BuildHeader(title), body, btnOK, btnCancel);

        this.AcceptButton = btnOK;
        this.CancelButton = btnCancel;

        this.Load += (s, e) => { txtInput.SelectAll(); txtInput.Focus(); };
    }
}
