using System.Windows;
using ImportCostAlgeria.Presentation.ViewModels;

namespace ImportCostAlgeria.Presentation.Views;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
    }

    private LoginViewModel ViewModel => (LoginViewModel)DataContext;

    private void OnLoginClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.TryLogin(PasswordBoxControl.Password))
        {
            DialogResult = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
