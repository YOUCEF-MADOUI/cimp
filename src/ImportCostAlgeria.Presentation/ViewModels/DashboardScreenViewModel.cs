using System.Collections.ObjectModel;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Écran "Tableau de bord" (Section 38) : indicateurs consolidés calculés à partir de l'historique RÉEL
/// des calculs exécutés et persistés (aucune valeur inventée / statique).
/// </summary>
public sealed class DashboardScreenViewModel : ObservableObject
{
    private readonly DashboardRepository _dashboardRepository;
    private Company? _company;

    private int _nombreImportations;
    private int _nombreDeProduits;
    private decimal _coutTotalDzd;
    private decimal _totalDroitsDeDouaneDzd;
    private int _nombreDAnomaliesBloquantes;

    public DashboardScreenViewModel(DashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
        DernieresImportations = new ObservableCollection<string>();
    }

    public int NombreImportations { get => _nombreImportations; private set => SetField(ref _nombreImportations, value); }
    public int NombreDeProduits { get => _nombreDeProduits; private set => SetField(ref _nombreDeProduits, value); }
    public decimal CoutTotalDzd { get => _coutTotalDzd; private set => SetField(ref _coutTotalDzd, value); }
    public decimal TotalDroitsDeDouaneDzd { get => _totalDroitsDeDouaneDzd; private set => SetField(ref _totalDroitsDeDouaneDzd, value); }
    public int NombreDAnomaliesBloquantes { get => _nombreDAnomaliesBloquantes; private set => SetField(ref _nombreDAnomaliesBloquantes, value); }

    public ObservableCollection<string> DernieresImportations { get; }

    public void LoadForCompany(Company company)
    {
        _company = company;
        var data = _dashboardRepository.BuildFor(company.Id);

        NombreImportations = data.NombreImportations;
        NombreDeProduits = data.NombreDeProduits;
        CoutTotalDzd = data.CoutTotalDzd;
        TotalDroitsDeDouaneDzd = data.TotalDroitsDeDouaneDzd;
        NombreDAnomaliesBloquantes = data.NombreDAnomaliesBloquantes;

        DernieresImportations.Clear();
        foreach (var imp in data.DernieresImportations)
        {
            string alerte = imp.Bloquee ? " ⚠️ CALCUL BLOQUÉ" : string.Empty;
            DernieresImportations.Add($"{imp.ImportNumber} — {imp.ReferenceDate:dd/MM/yyyy} — {imp.CoutTotalDzd:N2} DZD{alerte}");
        }
    }
}
