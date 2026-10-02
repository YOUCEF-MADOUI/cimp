using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ImportCostAlgeria.Presentation.ViewModels;

namespace ImportCostAlgeria.Presentation.Views;

public partial class ImportDetailView : UserControl
{
    private ImportDetailViewModel? _subscribedViewModel;

    public ImportDetailView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Demande utilisateur (Section 8 — focus automatique sur "Montant" après "Ajouter ce frais") :
        // ImportDetailView est réutilisée pour chaque importation ouverte (CurrentView change), donc on
        // désabonne systématiquement l'ancien ViewModel avant de s'abonner au nouveau pour ne jamais
        // accumuler de gestionnaires d'événements multiples (fuite mémoire / focus déclenché plusieurs fois).
        if (_subscribedViewModel != null)
            _subscribedViewModel.FeeAdded -= OnFeeAdded;

        _subscribedViewModel = e.NewValue as ImportDetailViewModel;
        if (_subscribedViewModel != null)
            _subscribedViewModel.FeeAdded += OnFeeAdded;
    }

    private void OnFeeAdded(FeeRowViewModel addedRow)
    {
        // Différé en Background : le DataGrid doit d'abord régénérer ses conteneurs pour la ligne qui
        // vient d'être ajoutée (via le binding Fees) avant qu'on puisse la sélectionner/éditer.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            FeesDataGrid.UpdateLayout();
            FeesDataGrid.SelectedItem = addedRow;
            FeesDataGrid.ScrollIntoView(addedRow);
            FeesDataGrid.CurrentCell = new DataGridCellInfo(addedRow, FeesMontantColumn);
            FeesDataGrid.Focus();
            FeesDataGrid.BeginEdit();

            // BeginEdit() fait apparaître le TextBox d'édition dans la cellule mais ne déplace pas
            // nécessairement le focus CLAVIER à l'intérieur : on le fait explicitement ici pour que
            // l'utilisateur puisse taper le montant immédiatement, sans clic supplémentaire.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                var cellContent = FeesMontantColumn.GetCellContent(addedRow);
                var textBox = cellContent as TextBox ?? FindVisualChild<TextBox>(cellContent);
                if (textBox != null)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            });
        });
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null)
            return null;

        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed)
                return typed;

            var nested = FindVisualChild<T>(child);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
