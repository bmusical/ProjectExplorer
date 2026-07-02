using System.Drawing;
using System.Drawing.Drawing2D;
using ProjectExplorer.Core.Services;

namespace ProjectExplorer.WinForms;

/// <summary>
/// A lightweight in-app image viewer. Opens an image with fit-to-window
/// display and supports next/previous navigation across the other images in
/// the same folder, zoom in/out, fit/actual size, and rotate. Esc closes.
/// </summary>
public sealed class ImageViewerForm : Form
{
    private readonly ImageViewerModel _model;
    private readonly PictureBox _picture;
    private readonly ToolStrip _toolbar;
    private readonly StatusStrip _status;
    private readonly ToolStripStatusLabel _statusLabel;

    private Image? _image;
    private float _zoom = 1.0f;
    private bool _fitToWindow = true;
    private int _rotation; // degrees, multiples of 90

    public ImageViewerForm(IEnumerable<string> imagesInFolder, string currentImagePath)
    {
        _model = new ImageViewerModel(imagesInFolder, currentImagePath);

        Text = "Image Viewer";
        Width = 900;
        Height = 700;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        BackColor = Color.FromArgb(32, 32, 32);

        _picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(32, 32, 32)
        };

        _toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        _toolbar.Items.Add("◀ Prev", null, (_, _) => ShowImage(_model.Previous()));
        _toolbar.Items.Add("Next ▶", null, (_, _) => ShowImage(_model.Next()));
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add("Zoom +", null, (_, _) => Zoom(1.25f));
        _toolbar.Items.Add("Zoom −", null, (_, _) => Zoom(0.8f));
        _toolbar.Items.Add("Fit", null, (_, _) => SetFit(true));
        _toolbar.Items.Add("100%", null, (_, _) => { SetFit(false); _zoom = 1.0f; ApplyZoom(); });
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add("Rotate ⟳", null, (_, _) => Rotate());

        _status = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _status.Items.Add(_statusLabel);

        Controls.Add(_picture);
        Controls.Add(_toolbar);
        Controls.Add(_status);

        KeyDown += ImageViewerForm_KeyDown;
        FormClosed += (_, _) => _image?.Dispose();

        ShowImage(_model.Current);
    }

    private void ImageViewerForm_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape: Close(); break;
            case Keys.Right:
            case Keys.Space: ShowImage(_model.Next()); break;
            case Keys.Left: ShowImage(_model.Previous()); break;
            case Keys.Oemplus:
            case Keys.Add: Zoom(1.25f); break;
            case Keys.OemMinus:
            case Keys.Subtract: Zoom(0.8f); break;
            case Keys.R: Rotate(); break;
        }
    }

    private void ShowImage(string? path)
    {
        _image?.Dispose();
        _image = null;
        _rotation = 0;
        _zoom = 1.0f;
        _fitToWindow = true;
        _picture.SizeMode = PictureBoxSizeMode.Zoom;

        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            _picture.Image = null;
            _statusLabel.Text = "(image not found)";
            return;
        }

        try
        {
            // Load without locking the file on disk.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            _image = Image.FromStream(fs);
            _picture.Image = _image;

            var name = System.IO.Path.GetFileName(path);
            _statusLabel.Text = $"{name}   —   {_image.Width} × {_image.Height} px   ({_model.Index + 1} of {_model.Count})";
            Text = $"Image Viewer — {name}";
        }
        catch (Exception ex)
        {
            _picture.Image = null;
            _statusLabel.Text = $"(could not open image: {ex.Message})";
        }
    }

    private void SetFit(bool fit)
    {
        _fitToWindow = fit;
        _picture.SizeMode = fit ? PictureBoxSizeMode.Zoom : PictureBoxSizeMode.CenterImage;
        if (fit) { _zoom = 1.0f; _picture.Image = _image; }
    }

    private void Zoom(float factor)
    {
        if (_image == null) return;
        SetFit(false);
        _zoom = Math.Clamp(_zoom * factor, 0.1f, 10f);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (_image == null) return;
        var w = Math.Max(1, (int)(_image.Width * _zoom));
        var h = Math.Max(1, (int)(_image.Height * _zoom));
        var scaled = new Bitmap(w, h);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_image, 0, 0, w, h);
        }
        _picture.SizeMode = PictureBoxSizeMode.CenterImage;
        _picture.Image = scaled;
    }

    private void Rotate()
    {
        if (_image == null) return;
        _rotation = (_rotation + 90) % 360;
        _image.RotateFlip(RotateFlipType.Rotate90FlipNone);
        _picture.Image = null;
        _picture.SizeMode = _fitToWindow ? PictureBoxSizeMode.Zoom : PictureBoxSizeMode.CenterImage;
        _picture.Image = _image;
    }
}
