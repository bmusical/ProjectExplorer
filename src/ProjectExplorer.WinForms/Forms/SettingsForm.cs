using ProjectExplorer.Core.Models;

namespace ProjectExplorer.WinForms;

/// <summary>
/// App-wide Settings dialog (Help ▸ ... no — File ▸ Settings...). Currently just the
/// "Focus on Run" behavior; more app-wide preferences can join it here as they're added.
/// </summary>
public class SettingsForm : Form
{
    private readonly RadioButton rbPrevent;
    private readonly RadioButton rbAllow;
    private readonly Button btnOK;
    private readonly Button btnCancel;

    public FocusOnRunMode SelectedFocusOnRun =>
        rbAllow.Checked ? FocusOnRunMode.AllowMultipleCopies : FocusOnRunMode.PreventMultipleCopies;

    public SettingsForm(AppSettings current)
    {
        this.Text = "Settings";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Size = new Size(440, 230);

        var lblGroup = new Label
        {
            Text = "Focus on Run",
            Location = new Point(12, 12),
            AutoSize = true,
            Font = new Font(this.Font, FontStyle.Bold)
        };

        var lblDescription = new Label
        {
            Text = "Controls what happens when Project Nest Explorer is launched while it's already running.",
            Location = new Point(12, 34),
            Size = new Size(400, 32)
        };

        rbPrevent = new RadioButton
        {
            Text = "Prevent multiple copies\n(switch to the running window instead of opening a new one)",
            Location = new Point(12, 76),
            Size = new Size(400, 36),
            Checked = current.FocusOnRun == FocusOnRunMode.PreventMultipleCopies
        };

        rbAllow = new RadioButton
        {
            Text = "Allow multiple copies\n(always open a new window)",
            Location = new Point(12, 118),
            Size = new Size(400, 36),
            Checked = current.FocusOnRun == FocusOnRunMode.AllowMultipleCopies
        };

        btnOK = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(242, 165),
            Size = new Size(80, 30)
        };

        btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(334, 165),
            Size = new Size(80, 30)
        };

        this.Controls.AddRange(new Control[] { lblGroup, lblDescription, rbPrevent, rbAllow, btnOK, btnCancel });
        this.AcceptButton = btnOK;
        this.CancelButton = btnCancel;
    }
}
