namespace ProjectExplorer.WinForms;

// Content here mirrors docs/HELP.md — keep the two in sync when either changes.
public class HelpForm : Form
{
    private RichTextBox rtb = null!;

    public HelpForm()
    {
        InitializeComponent();
        BuildContent();
    }

    private void InitializeComponent()
    {
        this.Text = "Project Nest Explorer — Help";
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MinimizeBox = false;
        this.MaximizeBox = true;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Size = new Size(640, 620);
        this.MinimumSize = new Size(480, 400);

        // ── Banner ──
        var banner = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            BackColor = Color.FromArgb(30, 80, 160)
        };
        var logo = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(48, 48),
            Location = new Point(16, 16)
        };
        try
        {
            using var logoStream = System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetManifestResourceStream("ProjectExplorer.WinForms.Assets.logo.png");
            if (logoStream != null)
                logo.Image = Image.FromStream(logoStream);
        }
        catch { /* logo is decorative — ignore load failures */ }

        var lblProduct = new Label
        {
            Text = "PROJECT NEST",
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(150, 185, 240),
            AutoSize = true,
            Location = new Point(78, 12)
        };
        var lblTitle = new Label
        {
            Text = "Help — Project Nest Explorer",
            Font = new Font("Segoe UI", 15f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(76, 30)
        };
        var lblSub = new Label
        {
            Text = "What it does, and what it deliberately doesn't.",
            Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
            ForeColor = Color.FromArgb(200, 220, 255),
            AutoSize = true,
            Location = new Point(78, 58)
        };
        banner.Controls.AddRange(new Control[] { logo, lblProduct, lblTitle, lblSub });

        // ── Body ──
        rtb = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 9.5f),
            DetectUrls = true,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.White
        };
        rtb.LinkClicked += (s, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.LinkText ?? "") { UseShellExecute = true });
            }
            catch { /* malformed or unsupported link — ignore */ }
        };

        var bodyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 12)
        };
        bodyPanel.Controls.Add(rtb);

        // ── Footer ──
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52
        };
        var btnClose = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            Size = new Size(96, 32),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        footer.Controls.Add(btnClose);
        footer.Layout += (s, e) =>
        {
            btnClose.Location = new Point(footer.ClientSize.Width - btnClose.Width - 16, 10);
        };

        this.Controls.Add(bodyPanel);
        this.Controls.Add(footer);
        this.Controls.Add(banner);
        this.AcceptButton = btnClose;
        this.CancelButton = btnClose;
    }

    private void BuildContent()
    {
        AppendHeading("Please read this first: what this app does NOT do", Color.FromArgb(170, 40, 20));
        AppendParagraph(
            "Project Nest Explorer only organizes references to folders, files, and web pages — it " +
            "doesn't own or manage the things themselves.");
        AppendBullet(
            "Adding a folder, file, or URL to a project never moves, copies, renames, or modifies " +
            "anything on disk (or on the web). It just remembers where it is.");
        AppendBullet(
            "Removing something from a project — or deleting a whole Project or Collection — never " +
            "deletes the real folder or file, and never affects any website. It only removes the " +
            "reference from this app.");
        AppendBullet(
            "The only thing Project Nest Explorer ever writes to your computer is one data file: " +
            "%APPDATA%\\ProjectExplorer\\projects.json (plus a .bak backup made automatically on " +
            "every save). A small license.json alongside it stores your license state if you've " +
            "registered. Nothing else on your computer is touched by normal use.");
        AppendParagraph(
            "If you ever want a clean slate, or to move to another computer: that one JSON file " +
            "(and its .bak) is the entire footprint.");

        AppendHeading("Quick start");
        AppendBullet("File ▸ New Project… (or Ctrl+N) to create your first project.");
        AppendBullet(
            "Right-click the project and choose Add Folder…, Add File…, or Add Web " +
            "Resource… to bring in references. Use New Collection… to group related " +
            "references — Collections can nest inside each other.");
        AppendBullet(
            "Click anything in the tree to browse it on the right; double-click a folder, file, or " +
            "web resource row to open or preview it.");

        AppendHeading("Core concepts");
        AppendBullet("Project — the top-level container, one per thing you're working on.");
        AppendBullet(
            "Collection — a nestable grouping inside a project. Collections can contain other " +
            "Collections.");
        AppendBullet("Folder Reference — a pointer to a real folder on disk.");
        AppendBullet(
            "File Reference — a pointer to a single real file on disk, opened with whatever app " +
            "Windows normally uses for it.");
        AppendBullet("Web Resource — a URL.");
        AppendParagraph("Every one of these is a reference — think bookmarks, not storage.");

        AppendHeading("Navigating and previewing");
        AppendBullet(
            "The tree (left) is the project hierarchy; selecting a Project, Collection, or Folder " +
            "Reference lists its contents on the right.");
        AppendBullet(
            "Selecting a File Reference shows an inline preview — images and common text formats " +
            "render directly; anything else still offers Open and Properties buttons.");
        AppendBullet(
            "Selecting a Web Resource shows an inline browser preview (via the WebView2 runtime) " +
            "with an \"Open in External Browser\" button. If WebView2 isn't installed, you'll see " +
            "that button instead of the preview.");
        AppendBullet(
            "View ▸ Details / Extra Large Icons / Large Icons / Small Icons / List / Tile " +
            "switches how folder contents are displayed.");

        AppendHeading("Unavailable folders, files, and web resources");
        AppendParagraph(
            "A Folder Reference or File Reference can point at something that's gone missing — a " +
            "network/removable drive got disconnected, or a local file was moved or deleted. A Web " +
            "Resource shows this only when the site itself actively returns an error (a 404, a " +
            "500, etc.) — never just because a check couldn't connect, since that's just as likely " +
            "a passing network blip as a dead link. When one of these happens, the item shows " +
            "greyed out with a strikethrough in both the tree and list, and its tooltip explains why.");
        AppendBullet(
            "Local disk — not found; likely moved, renamed, or deleted. Use \"Locate Folder…\"/" +
            "\"Locate File…\" on its right-click menu to point it at the new location, or remove it.");
        AppendBullet(
            "Network/removable drive — not reachable right now, which may just be temporary. It's " +
            "automatically re-checked every 20 seconds and reverts to normal as soon as it's " +
            "reachable again — no action needed.");
        AppendBullet(
            "Web resource — the site returned an error the last time it was checked. It's " +
            "automatically re-checked every 20 seconds and reverts to normal as soon as it loads " +
            "successfully again — no action needed.");
        AppendBullet(
            "\"Stop Auto-Retry\" on a network/removable or web resource's right-click menu turns " +
            "off the automatic re-check for one you know is gone for good (\"Resume Auto-Retry\" " +
            "turns it back on). \"Check Availability Now\" on any unavailable item re-checks it " +
            "immediately instead of waiting for the next automatic retry.");
        AppendBullet(
            "A Web Resource's \"Refresh\" (its equivalent of \"Check Availability Now\") is always " +
            "on its right-click menu, not just when it's flagged unavailable. That's partly for " +
            "sites that keep failing the automated check (e.g. one that blocks non-browser " +
            "requests) even though they load fine for you, but it's really just a normal, " +
            "everyday action you can use on any Web Resource any time, for any reason.");

        AppendHeading("Everyday actions (right-click menu)");
        AppendBullet(
            "Project — New Collection…, Add Folder…/File…/Web Resource…, " +
            "Rename, Edit Description…, Move Up/Down, Delete Project.");
        AppendBullet(
            "Collection — New Sub-Collection…, Add Folder…/File…/Web " +
            "Resource…, Rename, Edit Description…, Move Up/Down, Delete Collection.");
        AppendBullet(
            "Folder Reference — Open in Explorer, Open Command Prompt Here, Open PowerShell Here, " +
            "Copy Path, Properties, Edit Description…, Move Up/Down, Remove from Project.");
        AppendBullet(
            "File Reference — Open, Open Containing Folder, Copy Path, Properties, Edit…, " +
            "Move Up/Down, Remove from Project.");
        AppendBullet(
            "Web Resource — Open in External Browser, Copy URL, Edit…, Move Up/Down, " +
            "Remove from Project, Refresh.");
        AppendBullet(
            "When a Folder Reference or File Reference is unavailable, its menu also adds Check " +
            "Availability Now and Locate Folder…/Locate File… to retarget the moved item. " +
            "Network/removable/web resources also add Stop-Resume Auto-Retry when unavailable.");
        AppendParagraph(
            "\"Remove from Project\" and \"Delete Project/Collection\" only remove entries from " +
            "projects.json — see the note at the top of this page. Drag-and-drop in the tree is " +
            "the same: it only changes placement inside that one file. Drop onto a Collection or " +
            "Project to move something inside it, or drop just above/below a row (watch for the " +
            "insertion line) to reorder it among its siblings — including Projects, dragged onto " +
            "one another. Two gestures convert between container types: drag a Project onto a " +
            "Collection to turn it into a collection nested there, or drag a Collection onto the " +
            "\"Projects\" root to turn it into its own top-level project. If a drop position is " +
            "fiddly to hit exactly, every item's Move Up/Move Down menu items do the same " +
            "repositioning without dragging.");

        AppendHeading("Keyboard shortcuts");
        AppendBullet("Ctrl+N — New Project…");
        AppendBullet("F2 — Rename the selected Project or Collection (works from either the tree or the list).");

        AppendHeading("Settings");
        AppendParagraph(
            "File ▸ Settings… currently has one option, Focus on Run: \"Prevent multiple copies\" " +
            "(default) switches to the already-running window instead of opening a second one; " +
            "\"Allow multiple copies\" always opens a new window. Either way, if the main window's " +
            "saved position has drifted off every screen you currently have connected, it's moved " +
            "back onto your primary screen the next time it becomes visible.");

        AppendHeading("Free tier & licensing");
        AppendParagraph(
            "Free for up to 3 projects and 25 folder/file/web references total (Collections don't " +
            "count against the limit). A one-time license key removes that limit.");
        AppendBullet(
            "Help ▸ Register / License… to enter a key or see your current free-tier usage.");
        AppendBullet("Purchase at https://blaznaccess.com/landing/project-nest");
        AppendBullet("License verification is fully offline — no account or connection needed to activate or keep using a key.");

        AppendHeading("Checking for updates");
        AppendParagraph(
            "Help ▸ Check for Updates… checks for a newer version. Besides that, loading Web " +
            "Resource previews you've added, and checking whether a Web Resource is currently " +
            "reachable (see \"Unavailable folders, files, and web resources\" above), everything " +
            "else works fully offline.");

        AppendHeading("Getting help");
        AppendParagraph("Questions, bugs, or anything not covered here: support@blaznaccess.com");

        rtb.SelectionStart = 0;
        rtb.ScrollToCaret();
    }

    private void AppendHeading(string text, Color? color = null)
    {
        if (rtb.TextLength > 0) AppendFormatted("\n\n", rtb.Font, Color.Black, bullet: false);
        AppendFormatted(text + "\n", new Font("Segoe UI", 11f, FontStyle.Bold), color ?? Color.FromArgb(30, 80, 160), bullet: false);
    }

    private void AppendParagraph(string text)
    {
        AppendFormatted(text + "\n", new Font("Segoe UI", 9.5f), Color.Black, bullet: false);
    }

    private void AppendBullet(string text)
    {
        AppendFormatted(text + "\n", new Font("Segoe UI", 9.5f), Color.Black, bullet: true);
    }

    // Positions the caret at the end first, THEN sets the pending insertion
    // format, so AppendText actually picks it up (setting Selection* before
    // moving the caret gets silently discarded).
    private void AppendFormatted(string text, Font font, Color color, bool bullet)
    {
        rtb.SelectionStart = rtb.TextLength;
        rtb.SelectionLength = 0;
        rtb.SelectionFont = font;
        rtb.SelectionColor = color;
        rtb.SelectionBullet = bullet;
        rtb.AppendText(text);
    }
}
