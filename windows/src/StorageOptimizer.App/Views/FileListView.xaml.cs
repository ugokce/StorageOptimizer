using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace StorageOptimizer.App.Views;

public partial class FileListView : UserControl
{
    public FileListView()
    {
        InitializeComponent();
        // WPF can leave star-sized columns at their minimum width when the table is first measured
        // before it has its final width (seen right after a theme change). Re-applying the widths once
        // the table is laid out makes the columns share its width again.
        FilesGrid.Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RefreshStarColumns);
    }

    private void RefreshStarColumns()
    {
        foreach (var column in FilesGrid.Columns)
        {
            if (column.Width.IsStar)
            {
                var width = column.Width;
                column.Width = DataGridLength.Auto;
                column.Width = width;
            }
        }
    }
}
