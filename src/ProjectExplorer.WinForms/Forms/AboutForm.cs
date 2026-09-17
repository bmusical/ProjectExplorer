using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

public class AboutForm : Form
{
    public AboutForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        DialogTheme.InitDialog(this, minWidth: 460);
        this.Text = "About Project Nest Explorer";

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                      ?? new Version(1, 0, 0);

        var lblVersion = DialogTheme.BodyText(
            $"Version {version.Major}.{version.Minor}.{version.Build}",
            DialogTheme.TextPrimary,
            new Font("Segoe UI", 11F, FontStyle.Bold));
        lblVersion.Margin = new Padding(2, 0, 0, 10);

        var lblCompany = DialogTheme.BodyText("HxM Blazor Software LLC", DialogTheme.TextPrimary, DialogTheme.LabelFont);
        lblCompany.Margin = new Padding(2, 0, 0, 2);

        var lblCopyright = DialogTheme.BodyText(
            $"© {DateTime.Now.Year} HxM Blazor Software LLC. All rights reserved.",
            DialogTheme.TextMuted,
            new Font("Segoe UI", 8.5F));
        lblCopyright.Margin = new Padding(2, 0, 0, 0);

        var lblDataHeading = DialogTheme.BodyText("Your data is stored locally in", DialogTheme.TextMuted, new Font("Segoe UI", 8.5F));
        lblDataHeading.Margin = new Padding(2, 0, 0, 2);
        var lblDataPath = DialogTheme.BodyText(@"%APPDATA%\ProjectExplorer\projects.db", DialogTheme.TextPrimary, new Font("Consolas", 9F));
        lblDataPath.Margin = new Padding(2, 0, 0, 0);

        var btnClose = DialogTheme.PrimaryButton("Close", DialogResult.OK);

        var body = DialogTheme.BuildBody();
        body.Controls.Add(lblVersion);
        body.Controls.Add(lblCompany);
        body.Controls.Add(lblCopyright);
        body.Controls.Add(DialogTheme.DividerLine(topMargin: 16, bottomMargin: 14));
        body.Controls.Add(lblDataHeading);
        body.Controls.Add(lblDataPath);
        body.Controls.Add(DialogTheme.DividerLine(topMargin: 16, bottomMargin: 12));
        body.Controls.Add(DialogTheme.ButtonBar(btnClose));

        this.Controls.Add(body);
        this.Controls.Add(DialogTheme.BuildHeader("Project Nest Explorer", "All your projects, one place."));

        this.AcceptButton = btnClose;
        this.CancelButton = btnClose;
    }
}
