using System.IO;
using ProjectExplorer.Core.Services;

namespace ProjectExplorer.WinForms;

/// <summary>
/// Shown in the right-hand panel in place of the ListView when the TreeView
/// selection lands on a single FileReference. Renders an inline preview for
/// file types we know how to display (images, text) and always offers
/// Open/Properties so every file — previewable or not — has a next action.
/// </summary>
public sealed class FilePreviewPanel : Panel
{
    private readonly PictureBox _iconBox;
    private readonly Label _nameLabel;
    private readonly Label _pathLabel;
    private readonly Panel _contentHost;

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
    /// Displays the preview for the given file path: an inline image/text
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
            c.Dispose();
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
}
