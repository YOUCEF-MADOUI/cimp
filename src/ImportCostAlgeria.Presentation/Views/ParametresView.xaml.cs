using System.Windows.Controls;
using ImportCostAlgeria.Presentation.ViewModels;

namespace ImportCostAlgeria.Presentation.Views;

public partial class ParametresView : UserControl
{
    public ParametresView()
    {
        InitializeComponent();
    }

    // Convention de sécurité WPF : le contenu d'un PasswordBox n'est jamais bindé en XAML, il est lu
    // explicitement en code-behind (même règle que pour LoginWindow).
    private void OnNewPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ParametresViewModel vm)
            vm.NewPassword = NewPasswordBox.Password;
    }
}
