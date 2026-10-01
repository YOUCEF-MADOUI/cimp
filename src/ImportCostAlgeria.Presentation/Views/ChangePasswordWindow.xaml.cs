using System.Windows;
using ImportCostAlgeria.Presentation.ViewModels;

namespace ImportCostAlgeria.Presentation.Views;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (ViewModel.IsMandatory)
            {
                // Section 18 : le changement est obligatoire après usage du mot de passe initial généré
                // aléatoirement — le bouton "Annuler" reste présent (ergonomie) mais équivaut à quitter
                // l'application plutôt qu'à continuer avec un mot de passe non changé.
                CancelButton.Content = "Quitter l'application";
            }
        };
    }

    private ChangePasswordViewModel ViewModel => (ChangePasswordViewModel)DataContext;

    private void OnValidateClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TryChangePassword(NewPasswordBox.Password, ConfirmPasswordBox.Password))
        {
            DialogResult = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
