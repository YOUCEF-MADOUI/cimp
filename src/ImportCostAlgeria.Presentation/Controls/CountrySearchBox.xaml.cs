using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImportCostAlgeria.Core.Domain;

namespace ImportCostAlgeria.Presentation.Controls;

/// <summary>
/// Revue du 2026-10-01 (point 2 — Pays d'origine / Pays d'expédition) : ComboBox de recherche dynamique
/// de pays, réutilisable (une instance indépendante par champ — origine, expédition, etc.).
///
/// - Source de données unique : <see cref="CountryCatalog"/> (pas de duplication de la liste des pays).
/// - Aucune limite de longueur ni de préfixe sur la recherche (contrairement à l'ancien
///   TextBox MaxLength="2") : filtre par sous-chaîne sur la désignation française OU le code ISO2.
/// - Sélection possible au clavier (Haut/Bas pour parcourir la liste, Entrée pour valider) et à la
///   souris (clic sur un élément de la liste).
/// - Propriété publique bindable <see cref="SelectedIso2"/> : c'est la SEULE donnée persistée (le code
///   ISO2), exactement comme avant ; le texte affiché ("Algérie (DZ)") n'est qu'une commodité d'affichage.
/// </summary>
public partial class CountrySearchBox : UserControl
{
    public static readonly DependencyProperty SelectedIso2Property = DependencyProperty.Register(
        nameof(SelectedIso2),
        typeof(string),
        typeof(CountrySearchBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedIso2Changed));

    /// <summary>Code ISO2 actuellement retenu (ou null si aucun pays sélectionné). Bindable TwoWay.</summary>
    public string? SelectedIso2
    {
        get => (string?)GetValue(SelectedIso2Property);
        set => SetValue(SelectedIso2Property, value);
    }

    public static readonly DependencyProperty WatermarkProperty = DependencyProperty.Register(
        nameof(Watermark),
        typeof(string),
        typeof(CountrySearchBox),
        new PropertyMetadata("Rechercher un pays (nom ou code)..."));

    /// <summary>Texte d'indication affiché lorsque le champ est vide (facultatif).</summary>
    public string Watermark
    {
        get => (string)GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    /// <summary>
    /// Vrai pendant que le code modifie lui-même le Text de la zone de saisie (sélection programmatique,
    /// synchronisation depuis SelectedIso2...) : évite de redéclencher un filtrage/une boucle infinie.
    /// </summary>
    private bool _isUpdatingTextProgrammatically;

    public CountrySearchBox()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncTextFromSelectedIso2();
    }

    private static void OnSelectedIso2Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CountrySearchBox box)
            box.SyncTextFromSelectedIso2();
    }

    /// <summary>Réaffiche "Nom (CODE)" si SelectedIso2 est renseigné (ex. chargement d'une importation existante).</summary>
    private void SyncTextFromSelectedIso2()
    {
        if (PART_TextBox == null) return;

        var country = CountryCatalog.FindByIso2(SelectedIso2);
        string expectedText = country?.DisplayLabel ?? string.Empty;

        if (PART_TextBox.Text == expectedText) return;

        _isUpdatingTextProgrammatically = true;
        try
        {
            PART_TextBox.Text = expectedText;
            PART_TextBox.CaretIndex = PART_TextBox.Text.Length;
        }
        finally
        {
            _isUpdatingTextProgrammatically = false;
        }
    }

    private void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingTextProgrammatically) return;

        string filter = PART_TextBox.Text;
        var matches = CountryCatalog.Search(filter);

        PART_ListBox.ItemsSource = matches;

        if (matches.Count > 0 && PART_TextBox.IsFocused)
        {
            PART_ListBox.SelectedIndex = 0;
            PART_Popup.IsOpen = true;
        }
        else
        {
            PART_Popup.IsOpen = false;
        }

        // La saisie en cours ne représente plus nécessairement une sélection valide tant que
        // l'utilisateur n'a pas choisi un élément de la liste (au clavier ou à la souris) : on ne touche
        // PAS encore à SelectedIso2 ici, afin de ne jamais enregistrer un code ISO2 partiel/invalide.
    }

    private void OnTextBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingTextProgrammatically) return;

        // Reproduit le confort d'un ComboBox éditable : sélectionne tout le texte existant pour que la
        // première frappe remplace directement la valeur actuelle, et propose immédiatement la liste
        // complète (ou déjà filtrée si du texte est présent), afin de pouvoir choisir sans retaper.
        PART_TextBox.SelectAll();
        var matches = CountryCatalog.Search(PART_TextBox.Text);
        PART_ListBox.ItemsSource = matches;
        if (matches.Count > 0)
        {
            PART_ListBox.SelectedIndex = 0;
            PART_Popup.IsOpen = true;
        }
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e)
    {
        // Laisse le temps à un clic sur la liste d'être traité (PreviewMouseLeftButtonUp) avant de
        // refermer/réinitialiser : on referme seulement si le focus n'est pas allé dans la popup.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!PART_ListBox.IsKeyboardFocusWithin)
            {
                PART_Popup.IsOpen = false;
                // Si l'utilisateur quitte le champ sans avoir validé de sélection cohérente avec le
                // texte tapé, on revient à la dernière sélection connue (jamais de code ISO2 inventé/partiel).
                SyncTextFromSelectedIso2();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (PART_Popup.IsOpen && PART_ListBox.Items.Count > 0)
                {
                    PART_ListBox.SelectedIndex = Math.Min(PART_ListBox.SelectedIndex + 1, PART_ListBox.Items.Count - 1);
                    PART_ListBox.ScrollIntoView(PART_ListBox.SelectedItem);
                    e.Handled = true;
                }
                break;

            case Key.Up:
                if (PART_Popup.IsOpen && PART_ListBox.Items.Count > 0)
                {
                    PART_ListBox.SelectedIndex = Math.Max(PART_ListBox.SelectedIndex - 1, 0);
                    PART_ListBox.ScrollIntoView(PART_ListBox.SelectedItem);
                    e.Handled = true;
                }
                break;

            case Key.Enter:
                if (PART_Popup.IsOpen && PART_ListBox.SelectedItem is CountryInfo selected)
                {
                    CommitSelection(selected);
                    e.Handled = true;
                }
                break;

            case Key.Escape:
                PART_Popup.IsOpen = false;
                SyncTextFromSelectedIso2();
                e.Handled = true;
                break;
        }
    }

    private void OnListBoxPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (PART_ListBox.SelectedItem is CountryInfo selected)
        {
            CommitSelection(selected);
            e.Handled = true;
        }
    }

    private void OnListBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PART_ListBox.SelectedItem is CountryInfo selected)
        {
            CommitSelection(selected);
            e.Handled = true;
        }
    }

    /// <summary>Valide définitivement le pays choisi (clavier ou souris) : seule cette méthode écrit SelectedIso2.</summary>
    private void CommitSelection(CountryInfo country)
    {
        SelectedIso2 = country.Iso2;
        _isUpdatingTextProgrammatically = true;
        try
        {
            PART_TextBox.Text = country.DisplayLabel;
            PART_TextBox.CaretIndex = PART_TextBox.Text.Length;
        }
        finally
        {
            _isUpdatingTextProgrammatically = false;
        }

        PART_Popup.IsOpen = false;
        PART_TextBox.Focus();
    }
}
