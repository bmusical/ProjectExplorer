using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ProjectExplorer.WinForms;

/// <summary>
/// Shown in the right-hand panel in place of the ListView when the TreeView
/// selection lands on a single WebResource (or a ListView row for one is
/// activated). Renders the URL inline via WebView2 and always offers an
/// explicit "Open in External Browser" action, since the embedded preview
/// can be unavailable (WebView2 Runtime not installed) or simply not what
/// the user wants for this particular page.
/// </summary>
public sealed class WebResourcePreviewPanel : Panel
{
    private readonly PictureBox _iconBox;
    private readonly Label _nameLabel;
    private readonly Label _urlLabel;
    private readonly WebView2 _webView;
    private readonly Label _unavailableLabel;

    private bool _coreReady;
    private string? _pendingUrl;

    public event EventHandler? OpenExternalRequested;

    public WebResourcePreviewPanel()
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
        _urlLabel = new Label
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
            _urlLabel.Width = Math.Max(0, header.Width - _urlLabel.Left - 12);
        };
        header.Controls.Add(_iconBox);
        header.Controls.Add(_nameLabel);
        header.Controls.Add(_urlLabel);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(12, 6, 12, 6)
        };
        var btnOpenExternal = new Button { Text = "Open in External Browser", AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        btnOpenExternal.Click += (_, e) => OpenExternalRequested?.Invoke(this, e);
        footer.Controls.Add(btnOpenExternal);

        _webView = new WebView2 { Dock = DockStyle.Fill, Visible = false };
        _unavailableLabel = new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            Visible = false
        };

        var contentHost = new Panel { Dock = DockStyle.Fill };
        contentHost.Controls.Add(_webView);
        contentHost.Controls.Add(_unavailableLabel);

        Controls.Add(contentHost);
        Controls.Add(footer);
        Controls.Add(header);
    }

    /// <summary>
    /// Displays the given web resource: updates the header and navigates the
    /// embedded browser to its URL (initializing WebView2 lazily on first use).
    /// </summary>
    public void ShowWebResource(string url, string displayName, Image? icon)
    {
        _nameLabel.Text = displayName;
        _urlLabel.Text = url;
        _iconBox.Image?.Dispose();
        _iconBox.Image = (Image?)icon?.Clone();

        Navigate(url);
    }

    private async void Navigate(string url)
    {
        if (!TryGetNavigableUri(url, out var uri))
        {
            ShowUnavailable("This URL could not be parsed.");
            return;
        }

        _unavailableLabel.Visible = false;

        if (!_coreReady)
        {
            _pendingUrl = uri.ToString();
            await EnsureCoreAsync();
            return;
        }

        NavigateCore(uri.ToString());
    }

    private static bool TryGetNavigableUri(string url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out uri!) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        return Uri.TryCreate("https://" + url, UriKind.Absolute, out uri!);
    }

    private async Task EnsureCoreAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProjectExplorer", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);
            _coreReady = true;

            if (_pendingUrl != null)
            {
                NavigateCore(_pendingUrl);
                _pendingUrl = null;
            }
        }
        catch (Exception ex)
        {
            ShowUnavailable($"The embedded browser preview is unavailable ({ex.Message}). Use \"Open in External Browser\" instead.");
        }
    }

    private void NavigateCore(string url)
    {
        try
        {
            _webView.Visible = true;
            _unavailableLabel.Visible = false;
            _webView.CoreWebView2.Navigate(url);
        }
        catch (Exception ex)
        {
            ShowUnavailable($"Could not load this page: {ex.Message}");
        }
    }

    private void ShowUnavailable(string message)
    {
        _webView.Visible = false;
        _unavailableLabel.Text = message;
        _unavailableLabel.Visible = true;
    }
}
