using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using TCFModManager.Core.SpModApi;

namespace TCFModManager.App.Views;

// The Workshop banner over Home and Browse. See the header of the XAML.
public partial class WorkshopBanner : UserControl
{
    public WorkshopBanner()
    {
        InitializeComponent();
    }

    // Steam's "Learn More" explains the Workshop; the catalog site is what explains this one.
    private void LearnMore_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(new SpModApiOptions().BaseUrl) { UseShellExecute = true });
}
