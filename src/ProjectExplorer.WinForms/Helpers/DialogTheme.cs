using System.Reflection;

namespace ProjectExplorer.WinForms.Helpers;

/// <summary>
/// Shared visual language for the app's dialogs — brand palette, fonts, a branded header band,
/// modern flat buttons, and consistent field controls — so every dialog looks cohesive, spacious,
/// and current instead of cramped default-gray Win32.
///
/// All dialogs are built as AutoSize forms stacking a <see cref="BuildHeader"/> band on top of an
/// auto-sizing content <see cref="TableLayoutPanel"/>, so nothing is ever clipped at any DPI while
/// still looking designed.
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
    public static readonly Color Divider       = Color.FromArgb(228, 230, 234);
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

    /// <summary>Applies the shared dialog chrome (white surface, DPI auto-scale, fixed-tool border).</summary>
    public static void InitDialog(Form form, int minWidth, bool resizable = false)
    {
        form.FormBorderStyle = resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.CenterParent;
        form.BackColor = Surface;
        form.Font = BodyFont;
        form.AutoScaleDimensions = new SizeF(7F, 15F);
        form.AutoScaleMode = AutoScaleMode.Font;
        if (!resizable)
        {
            form.AutoSize = true;
            form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }
        form.MinimumSize = new Size(minWidth, 0);
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
            Height = subtitle == null ? 72 : 84,
            BackColor = Accent,
            Padding = new Padding(20, 0, 20, 0)
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
            Size = new Size(40, 40),
            Margin = new Padding(0, 0, 14, 0),
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

        var eyebrow = new Label
        {
            Text = "PROJECT NEST",
            Font = EyebrowFont,
            ForeColor = Eyebrow,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 1)
        };
        var lblTitle = new Label
        {
            Text = title,
            Font = TitleFont,
            ForeColor = Color.White,
            AutoSize = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        textStack.Controls.Add(eyebrow);
        textStack.Controls.Add(lblTitle);
        if (subtitle != null)
        {
            textStack.Controls.Add(new Label
            {
                Text = subtitle,
                Font = SubtitleFont,
                ForeColor = BannerSubtle,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 2, 0, 0)
            });
        }

        grid.Controls.Add(logo, 0, 0);
        grid.Controls.Add(textStack, 1, 0);
        banner.Controls.Add(grid);
        return banner;
    }

    /// <summary>The padded, auto-sizing content column that sits under the header.</summary>
    public static TableLayoutPanel BuildBody()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Surface,
            Padding = new Padding(24, 20, 24, 18)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return body;
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
            Margin = new Padding(2, 0, 2, 16)
        };
        if (placeholder != null) tb.PlaceholderText = placeholder;
        if (multiline)
        {
            tb.Height = height > 0 ? height : 76;
            tb.ScrollBars = ScrollBars.Vertical;
        }
        return tb;
    }

    /// <summary>A thin horizontal rule for separating sections / the button bar.</summary>
    public static Panel DividerLine(int topMargin = 4, int bottomMargin = 14) => new()
    {
        Height = 1,
        BackColor = Divider,
        Dock = DockStyle.Fill,
        Margin = new Padding(0, topMargin, 0, bottomMargin)
    };

    /// <summary>A right-aligned button bar. Primary (accent) sits at the far right.</summary>
    public static FlowLayoutPanel ButtonBar(params Button[] rightToLeft)
    {
        var bar = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        foreach (var b in rightToLeft)
            bar.Controls.Add(b);
        return bar;
    }

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
        Size = new Size(116, 38),
        Font = ButtonFont,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        UseVisualStyleBackColor = false,
        Margin = new Padding(10, 0, 0, 0)
    };
}
