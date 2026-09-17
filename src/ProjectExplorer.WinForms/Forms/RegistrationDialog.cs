using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;
using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

public class RegistrationDialog : Form
{
    private readonly LicenseManager _licenseManager;
    private readonly LicenseInfo _current;

    private Label lblStatus = null!;
    private TextBox txtKey = null!;
    private Button btnActivate = null!;
    private Button btnClose = null!;
    private LinkLabel lnkBuy = null!;

    public LicenseInfo ResultLicense { get; private set; }

    public RegistrationDialog(LicenseManager licenseManager, LicenseInfo current)
    {
        _licenseManager = licenseManager;
        _current        = current;
        ResultLicense   = current;
        InitializeComponent();
        RefreshStatus(current);
    }

    private void InitializeComponent()
    {
        DialogTheme.InitDialog(this);
        this.Text = "Project Nest Explorer — Registration";

        lblStatus = new Label
        {
            AutoSize = true,
            Font = DialogTheme.BodyFont,
            BackColor = Color.FromArgb(244, 246, 250),
            Padding = new Padding(14, 12, 14, 12),
            Margin = new Padding(2, 0, 2, 4),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };

        txtKey = new TextBox
        {
            Font = new Font("Consolas", 10.5F),
            BorderStyle = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            PlaceholderText = "Paste your license key here",
            Margin = new Padding(2, 0, 2, 12)
        };

        lnkBuy = new LinkLabel
        {
            Text = "Don't have a key? Purchase a license →",
            Font = DialogTheme.BodyFont,
            LinkColor = DialogTheme.Accent,
            ActiveLinkColor = DialogTheme.AccentHover,
            AutoSize = true,
            Margin = new Padding(2, 0, 2, 0)
        };
        lnkBuy.LinkClicked += (s, e) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://bemusical.gumroad.com/l/project-nest",
                UseShellExecute = true
            });

        btnActivate = DialogTheme.PrimaryButton("Activate", DialogResult.None);
        btnActivate.Click += BtnActivate_Click;
        btnClose = DialogTheme.SecondaryButton("Close", DialogResult.OK);

        var body = DialogTheme.BuildBody();
        body.Controls.Add(lblStatus);
        body.Controls.Add(DialogTheme.DividerLine(topMargin: 14, bottomMargin: 16));
        body.Controls.Add(DialogTheme.FieldLabel("Enter your license key"));
        body.Controls.Add(txtKey);
        body.Controls.Add(lnkBuy);

        DialogTheme.Compose(this, width: 660,
            DialogTheme.BuildHeader("Registration", "Unlock unlimited projects and references."),
            body, btnActivate, btnClose);

        this.AcceptButton = btnActivate;
        this.CancelButton = btnClose;
    }

    private void BtnActivate_Click(object? sender, EventArgs e)
    {
        var key = txtKey.Text.Trim();
        if (string.IsNullOrEmpty(key)) return;

        btnActivate.Enabled = false;
        var result = _licenseManager.Activate(key, Enumerable.Empty<Project>());
        ResultLicense = result;
        RefreshStatus(result);
        btnActivate.Enabled = true;

        if (result.State == LicenseState.Licensed)
        {
            txtKey.Text = "";
            MessageBox.Show(
                $"Thank you! Project Nest Explorer is now activated for:\n{result.Email}",
                "Activated", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(
                "That key doesn't appear to be valid. Please check it and try again.\n" +
                "If you need help, contact support@blaznaccess.com.",
                "Invalid Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RefreshStatus(LicenseInfo info)
    {
        switch (info.State)
        {
            case LicenseState.Licensed:
                lblStatus.Text = $"✔  Licensed to: {info.Email}\n" +
                                 $"      Activated on: {info.LicensedOn:MMMM d, yyyy}";
                lblStatus.ForeColor = Color.FromArgb(0, 120, 60);
                txtKey.Enabled = false;
                btnActivate.Enabled = false;
                break;

            case LicenseState.Free:
                lblStatus.Text = $"★  Free version — {info.ProjectCount}/{info.ProjectLimit} projects, " +
                                 $"{info.LeafNodeCount}/{info.LeafNodeLimit} references used.\n" +
                                  "      Enter a license key below to unlock unlimited access.";
                lblStatus.ForeColor = Color.FromArgb(170, 95, 0);
                break;

            case LicenseState.LimitReached:
                lblStatus.Text = $"⚠  Free limit reached — {info.LeafNodeCount} references across {info.ProjectCount} projects.\n" +
                                  "      Purchase a license key to add more projects and references.";
                lblStatus.ForeColor = Color.Firebrick;
                break;

            case LicenseState.Invalid:
                lblStatus.Text = "✘  The stored license key is invalid.\n" +
                                  "      Please re-enter your key or contact support.";
                lblStatus.ForeColor = Color.Firebrick;
                break;
        }
    }
}
