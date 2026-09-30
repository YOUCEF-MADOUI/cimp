using System.Collections.ObjectModel;
using System.Linq;
using ImportCostAlgeria.Audit;
using ImportCostAlgeria.Presentation.Infrastructure;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Écran "Journal d'audit" (Section 34) : lecture seule, aucune entrée n'est jamais supprimable ou
/// modifiable depuis l'interface — trace de toutes les actions sensibles du système.
/// </summary>
public sealed class AuditLogViewModel : ObservableObject
{
    private readonly AuditTrailService _audit;
    private string _filterCategory = string.Empty;

    public AuditLogViewModel(AuditTrailService audit)
    {
        _audit = audit;
        Entries = new ObservableCollection<AuditLogEntry>();
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    public ObservableCollection<AuditLogEntry> Entries { get; }

    public string FilterCategory
    {
        get => _filterCategory;
        set { SetField(ref _filterCategory, value); Refresh(); }
    }

    public RelayCommand RefreshCommand { get; }

    private void Refresh()
    {
        Entries.Clear();
        var all = _audit.GetAllAuditLogs().OrderByDescending(e => e.TimestampUtc).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(FilterCategory))
            all = all.Where(e => e.ActionCategory.Contains(FilterCategory, System.StringComparison.OrdinalIgnoreCase)
                               || e.ActionName.Contains(FilterCategory, System.StringComparison.OrdinalIgnoreCase));

        foreach (var e in all)
            Entries.Add(e);
    }
}
