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

                // Correction (premier démarrage sécurisé) : cet écran est désormais la toute première
                // chose vue par un nouvel utilisateur (la session administrateur est déjà ouverte
                // automatiquement par App.xaml.cs, SANS mot de passe, uniquement pour ce tout premier
                // lancement) — le titre doit donc explicitement indiquer qu'il s'agit de sécuriser le
                // compte, pas simplement de "changer" un mot de passe déjà connu de l'utilisateur.
                RootWindow.Title = "CIMP — Sécurisez votre compte administrateur";
                HeaderTitleText.Text = "Sécurisez votre compte administrateur";
                HeaderSubtitleText.Text =
                    "Bienvenue dans CIMP. Votre compte administrateur vient d'être créé automatiquement et " +
                    "aucun mot de passe ne vous a été communiqué : définissez dès maintenant votre propre " +
                    "mot de passe pour pouvoir accéder à l'application.";
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
