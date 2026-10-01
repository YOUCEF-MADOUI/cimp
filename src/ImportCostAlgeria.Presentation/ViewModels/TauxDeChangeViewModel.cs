using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ImportCostAlgeria.Audit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;
using ImportCostAlgeria.Presentation.Services;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Écran "Taux de change" (Sections 13 & 14) : historique versionné des taux officiels enregistrés
/// et publication d'un nouveau taux (réservée à l'Administrateur) — jamais d'écrasement.
/// </summary>
public sealed class TauxDeChangeViewModel : ObservableObject
{
    private readonly ExchangeRateAdminRepository _repository;
    private readonly SessionContext _session;
    private readonly AuditTrailService _audit;

    private string _newCurrencyCode = "USD";
    private string _newQuoteCurrencyCode = "DZD";
    private decimal _newRateToDzd;
    private int _newQuotityUnit = 1;
    private DateTime _newValidFrom = DateTime.Today;
    private string _newSourceName = "Banque d'Algérie";
    private string _newRateType = "OFFICIEL_DOUANE_ALCES";

    public TauxDeChangeViewModel(ExchangeRateAdminRepository repository, SessionContext session, AuditTrailService audit)
    {
        _repository = repository;
        _session = session;
        _audit = audit;

        Rates = new ObservableCollection<ExchangeRateRecord>();
        RefreshCommand = new RelayCommand(Refresh);
        PublishCommand = new RelayCommand(Publish);

        Refresh();
    }

    public ObservableCollection<ExchangeRateRecord> Rates { get; }

    public string NewCurrencyCode { get => _newCurrencyCode; set => SetField(ref _newCurrencyCode, value); }
    /// <summary>
    /// Section 6 du plan multi-devises : devise de cotation du taux publié. "DZD" (par défaut) publie un
    /// taux réglementaire/douanier classique ; une autre devise (ex: "USD") publie un taux commercial
    /// cross-rate (ex: EUR coté en USD) utilisé uniquement pour la conversion commerciale, jamais pour le
    /// calcul douanier.
    /// </summary>
    public string NewQuoteCurrencyCode { get => _newQuoteCurrencyCode; set => SetField(ref _newQuoteCurrencyCode, value); }
    public decimal NewRateToDzd { get => _newRateToDzd; set => SetField(ref _newRateToDzd, value); }
    /// <summary>Quotité : nombre d'unités de NewCurrencyCode auxquelles correspond NewRateToDzd (1 par défaut).</summary>
    public int NewQuotityUnit { get => _newQuotityUnit; set => SetField(ref _newQuotityUnit, value); }
    public DateTime NewValidFrom { get => _newValidFrom; set => SetField(ref _newValidFrom, value); }
    public string NewSourceName { get => _newSourceName; set => SetField(ref _newSourceName, value); }
    public string NewRateType { get => _newRateType; set => SetField(ref _newRateType, value); }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand PublishCommand { get; }

    private void Refresh()
    {
        Rates.Clear();
        foreach (var r in _repository.GetAll())
            Rates.Add(r);
    }

    private void Publish()
    {
        try
        {
            _session.RequireAdministrator("publier un nouveau taux de change officiel");

            if (string.IsNullOrWhiteSpace(NewCurrencyCode) || NewRateToDzd <= 0 || NewQuotityUnit <= 0)
            {
                MessageBox.Show("La devise, la devise de cotation, un taux strictement positif et une quotité strictement positive sont obligatoires.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string quoteCurrency = string.IsNullOrWhiteSpace(NewQuoteCurrencyCode) ? "DZD" : NewQuoteCurrencyCode.Trim().ToUpperInvariant();

            var newRate = new ExchangeRateRecord
            {
                CurrencyCode = NewCurrencyCode.Trim().ToUpperInvariant(),
                QuoteCurrencyCode = quoteCurrency,
                RateToDzd = NewRateToDzd,
                QuotityUnit = NewQuotityUnit,
                ValidFrom = DateOnly.FromDateTime(NewValidFrom),
                RateType = NewRateType.Trim(),
                SourceName = NewSourceName.Trim()
            };

            // Section 6 du plan multi-devises : ne retrouver l'ancien taux ouvert QUE pour la même paire de
            // devises (CurrencyCode + QuoteCurrencyCode), jamais une autre paire.
            var previous = Rates.FirstOrDefault(r => r.CurrencyCode == newRate.CurrencyCode && r.QuoteCurrencyCode == newRate.QuoteCurrencyCode && r.ValidTo == null);
            _repository.PublishNewRate(newRate, _session.CurrentUser!.Id, _session.CurrentUser.Role);

            _audit.RecordAction(null, _session.CurrentUser.Id, _session.CurrentUser.DisplayName,
                "EXCHANGE_RATE", "PUBLISH_NEW_RATE",
                oldValue: previous == null ? null : $"{previous.QuotityUnit} {previous.CurrencyCode} = {previous.RateToDzd:F4} {previous.QuoteCurrencyCode}",
                newValue: $"{newRate.QuotityUnit} {newRate.CurrencyCode} = {newRate.RateToDzd:F4} {newRate.QuoteCurrencyCode} à compter du {newRate.ValidFrom:dd/MM/yyyy}",
                legalSourceReference: newRate.SourceName);

            Refresh();
            MessageBox.Show("Nouveau taux de change publié avec succès.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CIMP — Action refusée", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
