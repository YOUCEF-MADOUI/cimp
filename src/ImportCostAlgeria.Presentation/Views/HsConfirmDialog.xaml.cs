using System.Windows;
using ImportCostAlgeria.Presentation.ViewModels;

namespace ImportCostAlgeria.Presentation.Views;

public partial class HsConfirmDialog : Window
{
    public HsConfirmDialog()
    {
        InitializeComponent();
    }

    private HsConfirmDialogViewModel Vm => (HsConfirmDialogViewModel)DataContext;

    private void OnConfirmerClick(object sender, RoutedEventArgs e)
    {
        Vm.Confirmer();
        DialogResult = true;
        Close();
    }

    private void OnModifierClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Vm.ModifiedHsCode))
        {
            MessageBox.Show("Veuillez saisir le code SH modifié.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Vm.Modifier();
        DialogResult = true;
        Close();
    }

    private void OnRefuserClick(object sender, RoutedEventArgs e)
    {
        Vm.Refuser();
        DialogResult = true;
        Close();
    }

    private void OnAnnulerClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
