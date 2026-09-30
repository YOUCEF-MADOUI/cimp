using System.Windows;
using ImportCostAlgeria.Presentation.ViewModels;

namespace ImportCostAlgeria.Presentation.Views;

public partial class ExcelImportWizardWindow : Window
{
    public ExcelImportWizardWindow()
    {
        InitializeComponent();
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ExcelImportWizardViewModel vm && vm.ImportedLines.Count == 0)
        {
            MessageBox.Show("Veuillez d'abord valider le mapping (étape 3bis) avant d'importer.", "CIMP",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
