using System.Windows;
using ImportCostAlgeria.Presentation.ViewModels;

namespace ImportCostAlgeria.Presentation.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
