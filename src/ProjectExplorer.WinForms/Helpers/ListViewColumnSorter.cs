using System.Collections;
using System.Windows.Forms;

namespace ProjectExplorer.WinForms.Helpers;

/// <summary>
/// Implements the IComparer interface for sorting ListView columns.
/// Supports text, numeric, and date sorting.
/// </summary>
public class ListViewColumnSorter : IComparer
{
    public int SortColumn { get; set; } = 0;
    public SortOrder Order { get; set; } = SortOrder.Ascending;

    public int Compare(object? x, object? y)
    {
        if (x is not ListViewItem itemX || y is not ListViewItem itemY)
            return 0;

        int compareResult;

        // Compare based on column
        if (SortColumn == 0)
        {
            // Name column — text compare
            compareResult = string.Compare(itemX.Text, itemY.Text, StringComparison.OrdinalIgnoreCase);
        }
        else if (SortColumn == 1)
        {
            // Size column — numeric compare
            var sizeX = ParseSize(itemX.SubItems[SortColumn].Text);
            var sizeY = ParseSize(itemY.SubItems[SortColumn].Text);
            compareResult = sizeX.CompareTo(sizeY);
        }
        else if (SortColumn == 3)
        {
            // Date column — date compare
            var dateX = DateTime.TryParse(itemX.SubItems[SortColumn].Text, out var dx) ? dx : DateTime.MinValue;
            var dateY = DateTime.TryParse(itemY.SubItems[SortColumn].Text, out var dy) ? dy : DateTime.MinValue;
            compareResult = DateTime.Compare(dateX, dateY);
        }
        else
        {
            // Other columns — text compare
            compareResult = string.Compare(
                itemX.SubItems[SortColumn].Text,
                itemY.SubItems[SortColumn].Text,
                StringComparison.OrdinalIgnoreCase);
        }

        // Apply sort order
        if (Order == SortOrder.Descending)
            compareResult = -compareResult;

        return compareResult;
    }

    private static long ParseSize(string sizeText)
    {
        if (string.IsNullOrWhiteSpace(sizeText)) return 0;

        var parts = sizeText.Split(' ');
        if (parts.Length != 2) return 0;

        if (!double.TryParse(parts[0], out var value)) return 0;

        var unit = parts[1].ToUpperInvariant();
        return unit switch
        {
            "B" => (long)value,
            "KB" => (long)(value * 1024),
            "MB" => (long)(value * 1024 * 1024),
            "GB" => (long)(value * 1024 * 1024 * 1024),
            "TB" => (long)(value * 1024L * 1024L * 1024L * 1024L),
            _ => 0
        };
    }
}
