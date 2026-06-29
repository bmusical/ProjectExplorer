using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;

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
        this.Text = "Project Nest Explorer — Registration";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Size = new Size(480, 320);

        // ── Banner ──
        var banner = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = Color.FromArgb(30, 80, 160)
        };
        var lblTitle = new Label
        {
            Text = "Project Nest Explorer",
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(14, 8)
        };
        var lblSub = new Label
        {
            Text = "HxM Blazor Software LLC",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(200, 220, 255),
            AutoSize = true,
            Location = new Point(16, 38)
        };
        banner.Controls.AddRange(new Control[] { lblTitle, lblSub });

        // ── Body ──
        lblStatus = new Label
        {
            Font = new Font("Segoe UI", 9f),
            Location = new Point(18, 80),
            Size = new Size(430, 48),
            AutoSize = false
        };

        var sep = new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Location = new Point(18, 134),
            Size = new Size(430, 2)
        };

        var lblEnter = new Label
        {
            Text = "Enter license key:",
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Location = new Point(18, 146)
        };

        txtKey = new TextBox
        {
            Font = new Font("Consolas", 9f),
            Location = new Point(18, 168),
            Size = new Size(430, 24),
            PlaceholderText = "Paste your license key here"
        };

        btnActivate = new Button
        {
            Text = "Activate",
            Size = new Size(96, 30),
            Location = new Point(18, 204)
        };
        btnActivate.Click += BtnActivate_Click;

        lnkBuy = new LinkLabel
        {
            Text = "Purchase a license →",
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Location = new Point(130, 210)
        };
        lnkBuy.LinkClicked += (s, e) =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://hxm.com/projectnest",   // update with real URL
                UseShellExecute = true
            });

        btnClose = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Size = new Size(88, 30),
            Location = new Point(360, 246)
        };

        this.Controls.AddRange(new Control[]
        {
            banner, lblStatus, sep, lblEnter, txtKey, btnActivate, lnkBuy, btnClose
        });
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
                "If you need help, contact support@hxm.com.",
                "Invalid Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RefreshStatus(LicenseInfo info)
    {
        switch (info.State)
        {
            case LicenseState.Licensed:
                lblStatus.Text = $"✔  Licensed to: {info.Email}\n" +
                                 $"   Activated on: {info.LicensedOn:MMMM d, yyyy}";
                lblStatus.ForeColor = Color.FromArgb(0, 130, 0);
                txtKey.Enabled = false;
                btnActivate.Enabled = false;
                break;

            case LicenseState.Free:
                lblStatus.Text = $"⭐  Free version — {info.ProjectCount}/{info.ProjectLimit} projects, " +
                                 $"{info.LeafNodeCount}/{info.LeafNodeLimit} references used.\n" +
                                  "   Enter a license key below to unlock unlimited access.";
                lblStatus.ForeColor = Color.FromArgb(180, 100, 0);
                break;

            case LicenseState.LimitReached:
                lblStatus.Text = $"⚠  Free limit reached — {info.LeafNodeCount} references across {info.ProjectCount} projects.\n" +
                                  "   Purchase a license key to add more projects and references.";
                lblStatus.ForeColor = Color.Firebrick;
                break;

            case LicenseState.Invalid:
                lblStatus.Text = "✘  The stored license key is invalid.\n" +
                                  "   Please re-enter your key or contact support.";
                lblStatus.ForeColor = Color.Firebrick;
                break;
        }
    }
}
