using System.Windows.Forms;

namespace ProjectExplorer.WinForms;

/// <summary>
/// A small modal "please wait" dialog with a marquee progress bar and a Cancel button, used to
/// keep the UI thread responsive (and cancellable) for operations that walk the file system or
/// otherwise do slow I/O, such as fully materializing a folder tree before an Expand All/Branch.
/// </summary>
internal sealed class ProgressDialog : Form
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Label _messageLabel;
    private readonly Button _cancelButton;

    public CancellationToken Token => _cts.Token;

    public ProgressDialog(string title, string message)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(360, 110);

        _messageLabel = new Label
        {
            Text = message,
            AutoSize = false,
            Location = new Point(12, 12),
            Size = new Size(336, 20)
        };

        var progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Location = new Point(12, 40),
            Size = new Size(336, 20)
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(273, 72),
            Size = new Size(75, 26),
            DialogResult = DialogResult.Cancel
        };
        _cancelButton.Click += CancelButton_Click;

        Controls.Add(_messageLabel);
        Controls.Add(progressBar);
        Controls.Add(_cancelButton);
        CancelButton = _cancelButton;
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        _cancelButton.Enabled = false;
        _messageLabel.Text = "Cancelling...";
        _cts.Cancel();
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a background thread, showing this dialog only while it's
    /// in progress. Returns the work's result, or default(T) if the user cancelled it.
    /// </summary>
    public static async Task<T?> RunAsync<T>(IWin32Window owner, string title, string message, Func<CancellationToken, T> work)
    {
        using var dialog = new ProgressDialog(title, message);
        T? result = default;
        Exception? error = null;

        dialog.Shown += async (s, e) =>
        {
            try
            {
                result = await Task.Run(() => work(dialog.Token), dialog.Token);
            }
            catch (OperationCanceledException)
            {
                // User cancelled — result stays default.
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                dialog.Close();
            }
        };

        dialog.ShowDialog(owner);

        if (error != null)
            throw error;

        return result;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cts.Dispose();
        base.Dispose(disposing);
    }
}
