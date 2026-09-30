using System.Windows;

namespace ImportCostAlgeria.Presentation.Views;

public partial class LineDetailDialog : Window
{
    public LineDetailDialog()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
