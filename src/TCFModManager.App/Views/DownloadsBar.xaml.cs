using System.Windows;
using System.Windows.Controls;

namespace TCFModManager.App.Views;

// The downloads bar along the bottom of the window - see the header of the XAML.
public partial class DownloadsBar : UserControl
{
    public DownloadsBar()
    {
        InitializeComponent();
    }

    // Anywhere on the bar opens Downloads, as Steam's does.
    private void Bar_Click(object sender, RoutedEventArgs e) => AppNavigation.Navigate(typeof(DownloadsPage));
}
