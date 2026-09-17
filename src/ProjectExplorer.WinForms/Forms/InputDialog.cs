namespace ProjectExplorer.WinForms;

/// <summary>
/// Simple input dialog for entering names/descriptions (projects, collections, etc.).
///
/// Layout note: the whole dialog is built from auto-sizing layout panels (a
/// <see cref="TableLayoutPanel"/> plus a right-aligned button <see cref="FlowLayoutPanel"/>)
/// and the form itself is <c>AutoSize</c>. This is deliberate: hardcoding pixel positions +
/// a fixed <c>ClientSize</c> breaks under the app's PerMonitorV2 high-DPI mode (child controls
/// get DPI-scaled but a fixed client area does not, so the button row gets clipped at 125–175%).
/// Letting the layout panels compute the size means the buttons are always fully visible.
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
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Font = new Font("Segoe UI", 9F);
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.AutoSize = true;
        this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        this.MinimumSize = new Size(440, 0);

        lblPrompt = new Label
        {
            Text = prompt,
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 6)
        };

        txtInput = new TextBox
        {
            Text = defaultValue,
            Width = 400,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(3, 0, 3, 0)
        };

        btnOK = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 30),
            Margin = new Padding(6, 0, 0, 0)
        };

        btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 30),
            Margin = new Padding(6, 0, 0, 0)
        };

        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
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
        layout.Controls.Add(lblPrompt);
        layout.Controls.Add(txtInput);
        layout.Controls.Add(buttonRow);

        this.Controls.Add(layout);
        this.AcceptButton = btnOK;
        this.CancelButton = btnCancel;

        this.Load += (s, e) => { txtInput.SelectAll(); txtInput.Focus(); };
    }
}
