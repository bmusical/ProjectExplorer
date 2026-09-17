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
        DialogTheme.InitDialog(this);
        this.Text = "About Project Nest Explorer";

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version
                      ?? new Version(1, 0, 0);

        var lblVersion = DialogTheme.BodyText(
            $"Version {version.Major}.{version.Minor}.{version.Build}",
            DialogTheme.TextPrimary,
            new Font("Segoe UI", 11F, FontStyle.Bold));
        lblVersion.Margin = new Padding(2, 0, 0, 12);

        var lblCompany = DialogTheme.BodyText("HxM Blazor Software LLC", DialogTheme.TextPrimary, DialogTheme.LabelFont);
        lblCompany.Margin = new Padding(2, 0, 0, 2);

        var lblCopyright = DialogTheme.BodyText(
            $"© {DateTime.Now.Year} HxM Blazor Software LLC. All rights reserved.",
            DialogTheme.TextMuted,
            new Font("Segoe UI", 8.5F));
        lblCopyright.Margin = new Padding(2, 0, 0, 0);

        var lblDataHeading = DialogTheme.BodyText("Your data is stored locally in", DialogTheme.TextMuted, new Font("Segoe UI", 8.5F));
        lblDataHeading.Margin = new Padding(2, 0, 0, 3);
        var lblDataPath = DialogTheme.BodyText(@"%APPDATA%\ProjectExplorer\projects.db", DialogTheme.TextPrimary, new Font("Consolas", 9.5F));
        lblDataPath.Margin = new Padding(2, 0, 0, 0);

        var btnClose = DialogTheme.PrimaryButton("Close", DialogResult.OK);

        var body = DialogTheme.BuildBody();
        body.Controls.Add(lblVersion);
        body.Controls.Add(lblCompany);
        body.Controls.Add(lblCopyright);
        body.Controls.Add(DialogTheme.DividerLine(topMargin: 18, bottomMargin: 16));
        body.Controls.Add(lblDataHeading);
        body.Controls.Add(lblDataPath);

        DialogTheme.Compose(this, width: 560,
            DialogTheme.BuildHeader("Project Nest Explorer", "All your projects, one place."),
            body, btnClose);

        this.AcceptButton = btnClose;
        this.CancelButton = btnClose;
    }
}
