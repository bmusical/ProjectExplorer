using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ProjectExplorer.Core.Services;

namespace ProjectExplorer.WinForms;

/// <summary>
/// Shown in the right-hand panel in place of the ListView when the TreeView
/// selection lands on a single FileReference. Renders an inline preview for
/// file types we know how to display (images, text, HTML, Markdown) and always
/// offers Open/Properties so every file — previewable or not — has a next action.
/// </summary>
public sealed class FilePreviewPanel : Panel
{
    private readonly PictureBox _iconBox;
    private readonly Label _nameLabel;
    private readonly Label _pathLabel;
    private readonly Panel _contentHost;

    // Lazily initialised — only created the first time an HTML or Markdown file is previewed.
    private WebView2? _webView;
    private bool _webViewCoreReady;
    private string? _pendingHtmlContent;
    private string? _pendingNavigateUrl;

    public event EventHandler? OpenRequested;
    public event EventHandler? PropertiesRequested;

    public FilePreviewPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = SystemColors.Window;

        var header = new Panel { Dock = DockStyle.Top, Height = 60 };
        _iconBox = new PictureBox
        {
            Size = new Size(32, 32),
            Location = new Point(12, 12),
            SizeMode = PictureBoxSizeMode.CenterImage
        };
        _nameLabel = new Label
        {
            AutoSize = false,
            Location = new Point(54, 8),
            Height = 22,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _pathLabel = new Label
        {
            AutoSize = false,
            Location = new Point(54, 30),
            Height = 20,
            Font = new Font("Segoe UI", 9f),
            ForeColor = SystemColors.GrayText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        header.Resize += (_, _) =>
        {
            _nameLabel.Width = Math.Max(0, header.Width - _nameLabel.Left - 12);
            _pathLabel.Width = Math.Max(0, header.Width - _pathLabel.Left - 12);
        };
        header.Controls.Add(_iconBox);
        header.Controls.Add(_nameLabel);
        header.Controls.Add(_pathLabel);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(12, 6, 12, 6)
        };
        var btnOpen = new Button { Text = "Open", AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        var btnProperties = new Button { Text = "Properties", AutoSize = true, Padding = new Padding(12, 4, 12, 4), Margin = new Padding(8, 0, 0, 0) };
        btnOpen.Click += (_, e) => OpenRequested?.Invoke(this, e);
        btnProperties.Click += (_, e) => PropertiesRequested?.Invoke(this, e);
        footer.Controls.Add(btnOpen);
        footer.Controls.Add(btnProperties);

        _contentHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };

        Controls.Add(_contentHost);
        Controls.Add(footer);
        Controls.Add(header);
    }

    /// <summary>
    /// Displays the preview for the given file path: an inline image/text/HTML/Markdown
    /// preview when the format is supported, otherwise a fallback message.
    /// Open/Properties are always available via the events above.
    /// </summary>
    public void ShowFile(string filePath, string? description, Icon? icon)
    {
        _nameLabel.Text = System.IO.Path.GetFileName(filePath);
        _pathLabel.Text = filePath;
        _iconBox.Image?.Dispose();
        _iconBox.Image = icon?.ToBitmap();

        _contentHost.SuspendLayout();
        var oldControls = _contentHost.Controls.Cast<Control>().ToArray();
        _contentHost.Controls.Clear();
        foreach (var c in oldControls)
        {
            if (c is PictureBox pb) pb.Image?.Dispose();
            if (c is WebView2) { /* keep the shared instance — don't dispose it */ }
            else c.Dispose();
        }

        if (!File.Exists(filePath))
        {
            AddMessage("This file could not be found. It may have been moved, renamed, or deleted.");
        }
        else
        {
            switch (FilePreviewHelper.GetPreviewKind(filePath))
            {
                case FilePreviewKind.Image:
                    ShowImagePreview(filePath);
                    break;
                case FilePreviewKind.Html:
                    ShowHtmlPreview(filePath);
                    break;
                case FilePreviewKind.Markdown:
                    ShowMarkdownPreview(filePath);
                    break;
                case FilePreviewKind.Text:
                    ShowTextPreview(filePath);
                    break;
                default:
                    AddMessage(string.IsNullOrWhiteSpace(description)
                        ? "No preview is available for this file type."
                        : description);
                    break;
            }
        }

        _contentHost.ResumeLayout();
    }

    private void ShowImagePreview(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var image = Image.FromStream(fs);
            var pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = image,
                BackColor = Color.FromArgb(32, 32, 32)
            };
            _contentHost.Controls.Add(pictureBox);
        }
        catch (Exception ex)
        {
            AddMessage($"Could not load image preview: {ex.Message}");
        }
    }

    private void ShowHtmlPreview(string filePath)
    {
        var fileUri = new Uri(filePath).AbsoluteUri;
        ShowInWebView(navigateUrl: fileUri, htmlContent: null);
    }

    private void ShowMarkdownPreview(string filePath)
    {
        try
        {
            string mdText;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var length = (int)Math.Min(fs.Length, FilePreviewHelper.MaxPreviewBytes);
                var buffer = new byte[length];
                var read = fs.Read(buffer, 0, length);
                mdText = Encoding.UTF8.GetString(buffer, 0, read);
                if (fs.Length > length)
                    mdText += "\n\n*\\[preview truncated — use Open to view the full file\\]*";
            }

            var html = MarkdownToHtml(mdText);
            ShowInWebView(navigateUrl: null, htmlContent: html);
        }
        catch (Exception ex)
        {
            AddMessage($"Could not load Markdown preview: {ex.Message}");
        }
    }

    private void ShowInWebView(string? navigateUrl, string? htmlContent)
    {
        EnsureWebView();
        _contentHost.Controls.Add(_webView!);

        if (!_webViewCoreReady)
        {
            _pendingNavigateUrl = navigateUrl;
            _pendingHtmlContent = htmlContent;
            InitWebViewCoreAsync();
            return;
        }

        ApplyWebViewContent(navigateUrl, htmlContent);
    }

    private async void InitWebViewCoreAsync()
    {
        if (_webView == null) return;
        try
        {
            var userDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProjectExplorer", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);
            _webViewCoreReady = true;

            if (_pendingNavigateUrl != null || _pendingHtmlContent != null)
            {
                ApplyWebViewContent(_pendingNavigateUrl, _pendingHtmlContent);
                _pendingNavigateUrl = null;
                _pendingHtmlContent = null;
            }
        }
        catch (Exception ex)
        {
            AddMessage($"The embedded browser preview is unavailable ({ex.Message}). Use Open to view this file.");
        }
    }

    private void ApplyWebViewContent(string? navigateUrl, string? htmlContent)
    {
        if (_webView?.CoreWebView2 == null) return;
        if (navigateUrl != null)
            _webView.CoreWebView2.Navigate(navigateUrl);
        else if (htmlContent != null)
            _webView.CoreWebView2.NavigateToString(htmlContent);
    }

    private void EnsureWebView()
    {
        if (_webView != null) return;
        _webView = new WebView2 { Dock = DockStyle.Fill };
    }

    private void ShowTextPreview(string filePath)
    {
        try
        {
            string text;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var length = (int)Math.Min(fs.Length, FilePreviewHelper.MaxPreviewBytes);
                var buffer = new byte[length];
                var read = fs.Read(buffer, 0, length);
                text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
                if (fs.Length > length)
                    text += "\r\n\r\n[... preview truncated; use Open to view the full file ...]";
            }

            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                Text = text
            };
            _contentHost.Controls.Add(textBox);
        }
        catch (Exception ex)
        {
            AddMessage($"Could not load text preview: {ex.Message}");
        }
    }

    private void AddMessage(string message)
    {
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = message,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText
        };
        _contentHost.Controls.Add(label);
    }

    // ── Minimal Markdown → HTML converter ───────────────────────────────────
    // Handles headings, bold, italic, inline code, fenced code blocks, links,
    // unordered/ordered lists, horizontal rules, and paragraph wrapping.
    // This is intentionally simple — for complex documents users can Open the file.
    private static string MarkdownToHtml(string markdown)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8">
            <style>
              body { font-family: 'Segoe UI', sans-serif; font-size: 14px; line-height: 1.6;
                     color: #24292e; background: #fff; padding: 16px 24px; max-width: 860px; margin: 0 auto; }
              h1,h2,h3,h4,h5,h6 { margin-top: 1em; margin-bottom: .25em; font-weight: 600; }
              h1 { font-size: 2em; border-bottom: 1px solid #e1e4e8; padding-bottom: .3em; }
              h2 { font-size: 1.5em; border-bottom: 1px solid #e1e4e8; padding-bottom: .2em; }
              code { background: #f6f8fa; border-radius: 3px; padding: 2px 5px; font-family: monospace; font-size: 90%; }
              pre  { background: #f6f8fa; border-radius: 6px; padding: 12px 16px; overflow-x: auto; }
              pre code { background: none; padding: 0; }
              a    { color: #0366d6; text-decoration: none; }
              a:hover { text-decoration: underline; }
              hr   { border: none; border-top: 1px solid #e1e4e8; margin: 1em 0; }
              ul,ol{ padding-left: 2em; }
              blockquote { border-left: 4px solid #dfe2e5; margin: 0; padding: 0 1em; color: #6a737d; }
              p    { margin-top: .5em; margin-bottom: .5em; }
            </style></head><body>
            """);

        var lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var i = 0;
        var inUl = false;
        var inOl = false;
        var inParagraph = false;

        void CloseLists()
        {
            if (inUl) { sb.Append("</ul>\n"); inUl = false; }
            if (inOl) { sb.Append("</ol>\n"); inOl = false; }
        }
        void CloseParagraph()
        {
            if (inParagraph) { sb.Append("</p>\n"); inParagraph = false; }
        }

        while (i < lines.Length)
        {
            var line = lines[i];

            // Fenced code block
            if (line.StartsWith("```"))
            {
                CloseLists(); CloseParagraph();
                var lang = line.Substring(3).Trim();
                sb.Append("<pre><code>");
                i++;
                while (i < lines.Length && !lines[i].StartsWith("```"))
                {
                    sb.Append(EscapeHtml(lines[i])).Append('\n');
                    i++;
                }
                sb.Append("</code></pre>\n");
                i++;
                continue;
            }

            // Headings
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                CloseLists(); CloseParagraph();
                var level = headingMatch.Groups[1].Length;
                sb.Append($"<h{level}>{InlineFormat(headingMatch.Groups[2].Value)}</h{level}>\n");
                i++;
                continue;
            }

            // Horizontal rule
            if (Regex.IsMatch(line, @"^(\*{3,}|-{3,}|_{3,})\s*$"))
            {
                CloseLists(); CloseParagraph();
                sb.Append("<hr>\n");
                i++;
                continue;
            }

            // Unordered list
            var ulMatch = Regex.Match(line, @"^[\*\-\+]\s+(.+)$");
            if (ulMatch.Success)
            {
                CloseParagraph();
                if (!inUl) { if (inOl) { sb.Append("</ol>\n"); inOl = false; } sb.Append("<ul>\n"); inUl = true; }
                sb.Append($"<li>{InlineFormat(ulMatch.Groups[1].Value)}</li>\n");
                i++;
                continue;
            }

            // Ordered list
            var olMatch = Regex.Match(line, @"^\d+\.\s+(.+)$");
            if (olMatch.Success)
            {
                CloseParagraph();
                if (!inOl) { if (inUl) { sb.Append("</ul>\n"); inUl = false; } sb.Append("<ol>\n"); inOl = true; }
                sb.Append($"<li>{InlineFormat(olMatch.Groups[1].Value)}</li>\n");
                i++;
                continue;
            }

            // Blank line
            if (string.IsNullOrWhiteSpace(line))
            {
                CloseLists(); CloseParagraph();
                i++;
                continue;
            }

            // Regular paragraph line
            CloseLists();
            if (!inParagraph) { sb.Append("<p>"); inParagraph = true; }
            else sb.Append("<br>");
            sb.Append(InlineFormat(line));
            i++;
        }

        CloseLists();
        CloseParagraph();
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string InlineFormat(string text)
    {
        text = EscapeHtml(text);
        // Inline code — must come before bold/italic to avoid double-escaping
        text = Regex.Replace(text, @"`([^`]+)`", "<code>$1</code>");
        // Bold **text** or __text__
        text = Regex.Replace(text, @"\*\*(.+?)\*\*|__(.+?)__", m =>
            $"<strong>{(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}</strong>");
        // Italic *text* or _text_
        text = Regex.Replace(text, @"\*(.+?)\*|_(.+?)_", m =>
            $"<em>{(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}</em>");
        // Links [text](url)
        text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", "<a href=\"$2\">$1</a>");
        return text;
    }

    private static string EscapeHtml(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
