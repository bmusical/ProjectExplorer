using System.Reflection;

namespace ProjectExplorer.WinForms.Helpers;

/// <summary>
/// Shared visual language for the app's dialogs — brand palette, fonts, a branded header band,
/// modern flat buttons, and consistent field controls — so every dialog looks cohesive, spacious,
/// and current instead of cramped default-gray Win32.
///
/// Layout model (see <see cref="Compose"/>): each dialog is a fixed-width form with a header
/// docked to the top, a button bar docked to the BOTTOM (so buttons can never be clipped), and the
/// field content filling the middle. The form's height is computed from the measured content at
/// runtime, so nothing is ever cut off at any DPI — this replaces the AutoSize-form approach,
/// which under-measured height and clipped the button row.
/// </summary>
internal static class DialogTheme
{
    // ── Brand palette ──
    public static readonly Color Accent       = Color.FromArgb(30, 80, 160);
    public static readonly Color AccentHover   = Color.FromArgb(42, 98, 188);
    public static readonly Color AccentDown    = Color.FromArgb(22, 62, 128);
    public static readonly Color Eyebrow       = Color.FromArgb(158, 190, 242);
    public static readonly Color BannerSubtle  = Color.FromArgb(205, 223, 255);
    public static readonly Color Surface       = Color.White;
    public static readonly Color TextPrimary   = Color.FromArgb(28, 30, 34);
    public static readonly Color TextMuted      = Color.FromArgb(104, 108, 116);
    public static readonly Color DividerColor  = Color.FromArgb(228, 230, 234);
    public static readonly Color ButtonBorder  = Color.FromArgb(203, 206, 212);
    public static readonly Color ButtonHover   = Color.FromArgb(244, 246, 249);
    public static readonly Color ButtonDown    = Color.FromArgb(233, 236, 240);

    // ── Fonts (created once, shared) ──
    public static readonly Font TitleFont  = new("Segoe UI", 15F, FontStyle.Bold);
    public static readonly Font EyebrowFont = new("Segoe UI", 8F, FontStyle.Bold);
    public static readonly Font SubtitleFont = new("Segoe UI", 9F, FontStyle.Italic);
    public static readonly Font LabelFont  = new("Segoe UI", 9F, FontStyle.Bold);
    public static readonly Font InputFont  = new("Segoe UI", 10.5F);
    public static readonly Font BodyFont   = new("Segoe UI", 9.5F);
    public static readonly Font ButtonFont = new("Segoe UI", 9.75F);

    private const int FooterHeight = 66;

    /// <summary>Applies the shared dialog chrome. Height is set later by <see cref="Compose"/>.</summary>
    public static void InitDialog(Form form)
    {
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.BackColor = Surface;
        form.Font = BodyFont;
        form.AutoScaleDimensions = new SizeF(7F, 15F);
        form.AutoScaleMode = AutoScaleMode.Font;
    }

    /// <summary>
    /// Assembles a dialog: header docked top, button bar docked bottom, content filling the middle
    /// (scrollable if it ever exceeds the screen). Computes and sets the form's client size from the
    /// measured content once the handle exists, then centers on the owner. Buttons live in the
    /// bottom bar, so they are always fully visible regardless of any measurement rounding.
    /// </summary>
    public static void Compose(Form form, int width, Panel header, TableLayoutPanel body, params Button[] buttonsRightToLeft)
    {
        var footer = BuildFooter(buttonsRightToLeft);

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Surface };
        body.Dock = DockStyle.Top;
        host.Controls.Add(body);

        // Docking order: Fill first, then edges (Bottom, then Top which claims the top strip).
        form.Controls.Add(host);
        form.Controls.Add(footer);
        form.Controls.Add(header);

        form.ClientSize = new Size(width, 240);

        void SizeAndCenter()
        {
            int contentHeight = body.PreferredSize.Height;
            int need = header.Height + contentHeight + footer.Height;
            var wa = Screen.FromControl(form).WorkingArea;
            int h = Math.Min(need, wa.Height - 48);
            form.ClientSize = new Size(width, h);

            var owner = form.Owner ?? Form.ActiveForm;
            if (owner != null && owner != form && owner.Visible)
            {
                form.Location = new Point(
                    owner.Left + (owner.Width - form.Width) / 2,
                    owner.Top + (owner.Height - form.Height) / 2);
            }
            else
            {
                var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
                form.Location = new Point(
                    area.X + (area.Width - form.Width) / 2,
                    area.Y + (area.Height - form.Height) / 2);
            }
        }

        form.Load += (s, e) => SizeAndCenter();
    }

    /// <summary>
    /// The branded header band: a blue panel with the small logo, the "PROJECT NEST" eyebrow,
    /// and the dialog title (plus an optional italic subtitle). Docked to the top of the form.
    /// </summary>
    public static Panel BuildHeader(string title, string? subtitle = null)
    {
        var banner = new Panel
        {
            Dock = DockStyle.Top,
            Height = subtitle == null ? 74 : 88,
            BackColor = Accent,
            Padding = new Padding(22, 0, 22, 0)
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var logo = new PictureBox
        {
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(42, 42),
            Margin = new Padding(0, 0, 16, 0),
            Anchor = AnchorStyles.None, // vertically centered in its cell
            BackColor = Color.Transparent
        };
        try
        {
            using var logoStream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("ProjectExplorer.WinForms.Assets.logo.png");
            if (logoStream != null)
                logo.Image = Image.FromStream(logoStream);
        }
        catch { /* logo is decorative — ignore load failures */ }

        var textStack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Left,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        textStack.Controls.Add(new Label
        {
            Text = "PROJECT NEST",
            Font = EyebrowFont,
            ForeColor = Eyebrow,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 1)
        });
        textStack.Controls.Add(new Label
        {
            Text = title,
            Font = TitleFont,
            ForeColor = Color.White,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        });
        if (subtitle != null)
        {
            textStack.Controls.Add(new Label
            {
                Text = subtitle,
                Font = SubtitleFont,
                ForeColor = BannerSubtle,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 3, 0, 0)
            });
        }

        grid.Controls.Add(logo, 0, 0);
        grid.Controls.Add(textStack, 1, 0);
        banner.Controls.Add(grid);
        return banner;
    }

    /// <summary>The padded, auto-sizing content column that fills the middle of the dialog.</summary>
    public static TableLayoutPanel BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Surface,
            Padding = new Padding(26, 22, 26, 20)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return body;
    }

    private static Panel BuildFooter(Button[] buttonsRightToLeft)
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = FooterHeight,
            BackColor = Surface,
            Padding = new Padding(26, 0, 26, 0)
        };
        footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = DividerColor });

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = Surface
        };
        foreach (var b in buttonsRightToLeft)
            bar.Controls.Add(b);
        footer.Controls.Add(bar);
        return footer;
    }

    public static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = LabelFont,
        ForeColor = TextPrimary,
        Margin = new Padding(2, 0, 0, 5)
    };

    public static Label BodyText(string text, Color? color = null, Font? font = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = font ?? BodyFont,
        ForeColor = color ?? TextPrimary,
        Margin = new Padding(2, 0, 0, 0)
    };

    public static TextBox Input(string text = "", string? placeholder = null, bool multiline = false, int height = 0)
    {
        var tb = new TextBox
        {
            Text = text,
            Font = InputFont,
            BorderStyle = BorderStyle.FixedSingle,
            Multiline = multiline,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(2, 0, 2, 18)
        };
        if (placeholder != null) tb.PlaceholderText = placeholder;
        if (multiline)
        {
            tb.Height = height > 0 ? height : 84;
            tb.ScrollBars = ScrollBars.Vertical;
        }
        return tb;
    }

    /// <summary>A thin horizontal rule for separating sections within the body.</summary>
    public static Panel DividerLine(int topMargin = 4, int bottomMargin = 14) => new()
    {
        Height = 1,
        BackColor = DividerColor,
        Dock = DockStyle.Fill,
        Margin = new Padding(0, topMargin, 0, bottomMargin)
    };

    public static Button PrimaryButton(string text, DialogResult result = DialogResult.None)
    {
        var b = BaseButton(text, result);
        b.BackColor = Accent;
        b.ForeColor = Color.White;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = AccentHover;
        b.FlatAppearance.MouseDownBackColor = AccentDown;
        return b;
    }

    public static Button SecondaryButton(string text, DialogResult result = DialogResult.None)
    {
        var b = BaseButton(text, result);
        b.BackColor = Surface;
        b.ForeColor = TextPrimary;
        b.FlatAppearance.BorderColor = ButtonBorder;
        b.FlatAppearance.BorderSize = 1;
        b.FlatAppearance.MouseOverBackColor = ButtonHover;
        b.FlatAppearance.MouseDownBackColor = ButtonDown;
        return b;
    }

    private static Button BaseButton(string text, DialogResult result) => new()
    {
        Text = text,
        DialogResult = result,
        AutoSize = false,
        Size = new Size(120, 38),
        Font = ButtonFont,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        UseVisualStyleBackColor = false,
        Margin = new Padding(10, 0, 0, 0)
    };
}
