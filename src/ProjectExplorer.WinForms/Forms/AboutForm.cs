namespace ProjectExplorer.WinForms;

public class AboutForm : Form
{
    public AboutForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        this.Text = "About Project Nest Explorer";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Size = new Size(420, 300);
        this.Padding = new Padding(0);

        // ── Banner panel ──
        var banner = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = Color.FromArgb(30, 80, 160)
        };

        var lblAppName = new Label
        {
            Text = "Project Nest Explorer",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(16, 12)
        };

        var lblTagline = new Label
        {
            Text = "All your projects, one place.",
            Font = new Font("Segoe UI", 9f, FontStyle.Italic),
            ForeColor = Color.FromArgb(200, 220, 255),
            AutoSize = true,
            Location = new Point(18, 50)
        };

        banner.Controls.AddRange(new Control[] { lblAppName, lblTagline });

        // ── Body ──
        var body = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 16, 20, 0)
        };

        var version = System.Reflection.Assembly
            .GetExecutingAssembly()
            .GetName()
            .Version ?? new Version(1, 0, 0);

        var info = new Label
        {
            Text = $"Version {version.Major}.{version.Minor}.{version.Build}",
            Font = new Font("Segoe UI", 9f),
            AutoSize = true,
            Location = new Point(20, 16)
        };

        var company = new Label
        {
            Text = "HxM Blazor Software LLC",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 40)
        };

        var copyright = new Label
        {
            Text = $"© {DateTime.Now.Year} HxM Blazor Software LLC. All rights reserved.",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(20, 62)
        };

        var separator = new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Location = new Point(20, 90),
            Size = new Size(360, 2)
        };

        var lblDataNote = new Label
        {
            Text = "Project data is stored in:\n%APPDATA%\\ProjectExplorer\\projects.json",
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.DimGray,
            AutoSize = true,
            Location = new Point(20, 100)
        };

        var btnClose = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Size = new Size(88, 32),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        btnClose.Location = new Point(this.ClientSize.Width - btnClose.Width - 20,
                                      this.ClientSize.Height - btnClose.Height - 16);

        body.Controls.AddRange(new Control[] { info, company, copyright, separator, lblDataNote, btnClose });

        this.Controls.AddRange(new Control[] { body, banner });
        this.AcceptButton = btnClose;
        this.CancelButton = btnClose;

        // Reposition close button after layout is known
        this.Load += (s, e) =>
        {
            btnClose.Location = new Point(
                body.ClientSize.Width - btnClose.Width - 4,
                body.ClientSize.Height - btnClose.Height - 8);
        };
    }
}
