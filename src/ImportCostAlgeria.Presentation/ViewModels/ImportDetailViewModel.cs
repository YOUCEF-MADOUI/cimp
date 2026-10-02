using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ImportCostAlgeria.AI;
using ImportCostAlgeria.Audit;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Core.Services;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.Presentation.Infrastructure;
using ImportCostAlgeria.Presentation.Services;
using ImportCostAlgeria.Presentation.Views;
using ImportCostAlgeria.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Écran "Détail d'une importation" (Sections 7, 9, 13-17, 22-28) : cœur fonctionnel de l'application —
/// articles, frais, taux de change, calcul complet, anomalies, détail par article, exports.
/// </summary>
public sealed class ImportDetailViewModel : ObservableObject
{
    private readonly ImportOperationRepository _operationRepository;
    private readonly CalculationSnapshotRepository _snapshotRepository;
    private readonly AuditTrailService _audit;
    private readonly SessionContext _session;
    private readonly EngineFactory _engineFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly IncotermDynamicFieldService _incotermFieldService = new();

    private Company _company = null!;
    private ImportOperation _operation = null!;
    private ImportationsViewModel? _parentList;

    private ImportCalculationSummary? _lastSummary;
    private bool _hasBlockingAnomalies;
    private decimal _totalCoutRevientDzd;
    private decimal _totalValeurDouaneDzd;
    private decimal _totalDroitsDouaneDzd;
    private decimal _totalTvaDzd;
    private decimal _totalAutresTaxesDzd;
    private decimal _totalCsDzd;
    private decimal _totalPrctDzd;
    private decimal _totalTcsDzd;
    private decimal _totalDapsDzd;
    private decimal _totalRpsDzd;
    private decimal _totalFraisDzd;
    private bool _isProvisionalCalculation;
    private string _provisionalMessageFr = string.Empty;
    private decimal _totalDedouanementDzd;
    private string _customsValueDisplayCurrency = "DA";
    private decimal? _customsValueDisplayAmount;
    private bool _hasAnyAiProposedHs;
    private bool _hasAnyManualVatRate;
    private bool _hasAnyVatExemptionReason;
    private bool _hasAnyExcelDutyRate;
    private string _exchangeRateInfo = string.Empty;
    private string _authorizationExchangeRateInfo = string.Empty;
    private string _authorizationConversionSummary = string.Empty;
    private string _incotermGuidance = string.Empty;
    private string _headerLabel = string.Empty;
    private StandardFeeTemplate? _selectedFeeTemplate;

    public ImportDetailViewModel(
        ImportOperationRepository operationRepository,
        CalculationSnapshotRepository snapshotRepository,
        AuditTrailService audit,
        SessionContext session,
        EngineFactory engineFactory,
        IServiceProvider serviceProvider)
    {
        _operationRepository = operationRepository;
        _snapshotRepository = snapshotRepository;
        _audit = audit;
        _session = session;
        _engineFactory = engineFactory;
        _serviceProvider = serviceProvider;

        Lines = new ObservableCollection<ImportLineRowViewModel>();
        // Revue du 2026-10-02 (REFONTE INTERFACE, Section 10) : masquage dynamique des colonnes
        // facultatives (IA/fiscales) dès que l'ensemble des articles affichés change.
        Lines.CollectionChanged += (_, __) => RecomputeDynamicColumnVisibility();
        Fees = new ObservableCollection<ImportFee>();
        Anomalies = new ObservableCollection<CalculationAnomaly>();
        FeeTemplates = ImportFeeCatalog.StandardTemplates.ToArray();

        BackCommand = new RelayCommand(() => _parentList?.NavigateBackToList?.Invoke());
        AddLineCommand = new RelayCommand(AddManualLine);
        RemoveLineCommand = new RelayCommand<ImportLineRowViewModel>(RemoveLine);
        ImportExcelCommand = new RelayCommand(OpenExcelWizard);
        CalculateCommand = new RelayCommand(Calculate);
        ExportExcelCommand = new RelayCommand(ExportExcel);
        ExportPdfCommand = new RelayCommand(ExportPdf);
        ConfirmHsCommand = new RelayCommand<ImportLineRowViewModel>(OpenHsConfirmDialog);
        ViewDetailCommand = new RelayCommand<ImportLineRowViewModel>(OpenLineDetailDialog);
        AddFeeCommand = new RelayCommand(AddFeeFromTemplate);
        RemoveFeeCommand = new RelayCommand<ImportFee>(RemoveFee);
    }

    public void Initialize(Company company, ImportOperation operation, ImportationsViewModel parentList)
    {
        _company = company;
        _operation = operation;
        _parentList = parentList;

        HeaderLabel = $"Importation {operation.ImportNumber} — {operation.SupplierName} — Incoterm {operation.Incoterm}";

        Lines.Clear();
        foreach (var line in operation.Lines.OrderBy(l => l.LineNumber))
            Lines.Add(new ImportLineRowViewModel(line));

        Fees.Clear();
        foreach (var fee in operation.Fees)
            Fees.Add(fee);

        Anomalies.Clear();
        _lastSummary = null;
        HasBlockingAnomalies = false;
        IsProvisionalCalculation = false;
        ProvisionalMessageFr = string.Empty;

        RefreshExchangeRateInfo();
        RefreshAuthorizationExchangeRateInfo();
        RefreshIncotermGuidance();
        RecomputeDynamicColumnVisibility();
        RefreshCustomsValueDisplayAmount();
    }

    public string HeaderLabel { get => _headerLabel; private set => SetField(ref _headerLabel, value); }

    public ObservableCollection<ImportLineRowViewModel> Lines { get; }
    public ObservableCollection<ImportFee> Fees { get; }
    public ObservableCollection<CalculationAnomaly> Anomalies { get; }
    public StandardFeeTemplate[] FeeTemplates { get; }
    /// <summary>
    /// Revue du 2026-10-02 (Section 19 — "La liste déroulante Méthode de répartition est vide") : expose
    /// désormais les 5 méthodes réellement proposées en V1 (Section 35 : Par poids / Par volume ne sont
    /// PAS proposées pour le moment, même si FeeAllocationMethod les conserve pour une évolution future).
    /// Le bug racine (liste réellement vide à l'écran) venait du XAML : DataGridComboBoxColumn n'étant pas
    /// un FrameworkElement, un binding ElementName/RelativeSource sur sa colonne ne se résout jamais — voir
    /// ImportDetailView.xaml qui utilise désormais {x:Static} sur ImportFeeCatalog.AllocationMethodsForV1.
    /// Cette propriété reste exposée pour toute autre utilisation (ex: tests, futurs écrans) qui bénéficie
    /// bien d'un DataContext binding classique.
    /// </summary>
    public FeeAllocationMethod[] FeeAllocationMethods { get; } = ImportFeeCatalog.AllocationMethodsForV1.ToArray();

    public StandardFeeTemplate? SelectedFeeTemplate
    {
        get => _selectedFeeTemplate;
        set => SetField(ref _selectedFeeTemplate, value);
    }

    public bool HasBlockingAnomalies { get => _hasBlockingAnomalies; private set => SetField(ref _hasBlockingAnomalies, value); }
    public decimal TotalCoutRevientDzd { get => _totalCoutRevientDzd; private set => SetField(ref _totalCoutRevientDzd, value); }
    public decimal TotalValeurDouaneDzd { get => _totalValeurDouaneDzd; private set => SetField(ref _totalValeurDouaneDzd, value); }
    public decimal TotalDroitsDouaneDzd { get => _totalDroitsDouaneDzd; private set => SetField(ref _totalDroitsDouaneDzd, value); }
    public decimal TotalTvaDzd { get => _totalTvaDzd; private set => SetField(ref _totalTvaDzd, value); }
    /// <summary>Section 23 : total des taxes additionnelles (CS, PRCT, TCS, DAPS...) distinct de la TVA et du DD.</summary>
    public decimal TotalAutresTaxesDzd { get => _totalAutresTaxesDzd; private set => SetField(ref _totalAutresTaxesDzd, value); }
    /// <summary>Section 14 de la correction du 2026-10-02 : chaque taxe additionnelle affichée séparément (CS, PRCT, TCS, DAPS, RPS).</summary>
    public decimal TotalCsDzd { get => _totalCsDzd; private set => SetField(ref _totalCsDzd, value); }
    public decimal TotalPrctDzd { get => _totalPrctDzd; private set => SetField(ref _totalPrctDzd, value); }
    public decimal TotalTcsDzd { get => _totalTcsDzd; private set => SetField(ref _totalTcsDzd, value); }
    public decimal TotalDapsDzd { get => _totalDapsDzd; private set => SetField(ref _totalDapsDzd, value); }
    public decimal TotalRpsDzd { get => _totalRpsDzd; private set => SetField(ref _totalRpsDzd, value); }
    public decimal TotalFraisDzd { get => _totalFraisDzd; private set => SetField(ref _totalFraisDzd, value); }

    /// <summary>
    /// Section 15 de la correction du 2026-10-02 : vrai lorsque le calcul s'est exécuté SANS anomalie
    /// bloquante mais en utilisant au moins un taux PAR DÉFAUT de l'importation (ou en laissant une taxe
    /// NON DÉTERMINÉE) — le résultat est alors "provisoire" (bandeau orange), à distinguer du cas
    /// réellement bloquant (<see cref="HasBlockingAnomalies"/>, bandeau rouge, calcul non fiable du tout).
    /// </summary>
    public bool IsProvisionalCalculation { get => _isProvisionalCalculation; private set => SetField(ref _isProvisionalCalculation, value); }
    public string ProvisionalMessageFr { get => _provisionalMessageFr; private set => SetField(ref _provisionalMessageFr, value); }

    /// <summary>
    /// Revue du 2026-10-02 (REFONTE INTERFACE, Section 9) : TOTAL DÉDOUANEMENT = DD + CS + TVA + PRCT +
    /// TCS + RPS (Sections 9 & 22 de la demande — formule EXPLICITEMENT répétée avec CS inclus, même si le
    /// libellé initial ne mentionnait que "TVA+TCS+PRCT+DD+RPS"). N'inclut JAMAIS les frais commerciaux
    /// (transport, manutention, magasinage, transit...) ni la DAPS (absente de la formule donnée) — ce
    /// total représente strictement les taxes/droits de LIQUIDATION douanière.
    /// </summary>
    public decimal TotalDedouanementDzd { get => _totalDedouanementDzd; private set => SetField(ref _totalDedouanementDzd, value); }

    /// <summary>Devises proposées pour l'affichage (uniquement) de la valeur en douane — Section 8.</summary>
    public string[] CustomsValueDisplayCurrencies { get; } = { "DA", "EUR", "USD" };

    /// <summary>
    /// Revue du 2026-10-02 (REFONTE INTERFACE, Section 8) : devise choisie pour l'AFFICHAGE de la valeur en
    /// douane — une conversion PUREMENT visuelle, qui ne modifie JAMAIS la valeur réglementaire (toujours
    /// calculée et stockée en DZD), ni les droits/taxes/coût de revient (tous recalculés en DZD, inchangés).
    /// </summary>
    public string CustomsValueDisplayCurrency
    {
        get => _customsValueDisplayCurrency;
        set { if (SetField(ref _customsValueDisplayCurrency, value)) RefreshCustomsValueDisplayAmount(); }
    }

    /// <summary>Valeur en douane convertie (affichage uniquement) dans <see cref="CustomsValueDisplayCurrency"/> — null si aucun taux n'est disponible pour la conversion demandée.</summary>
    public decimal? CustomsValueDisplayAmount { get => _customsValueDisplayAmount; private set => SetField(ref _customsValueDisplayAmount, value); }

    /// <summary>Code ISO correspondant à <see cref="CustomsValueDisplayCurrency"/> ("DA" -&gt; "DZD"), pour résoudre le taux de change.</summary>
    private string CustomsValueDisplayCurrencyIso => _customsValueDisplayCurrency == "DA" ? "DZD" : _customsValueDisplayCurrency;

    // ------------------------------------------------------------------------------------------
    // Revue du 2026-10-02 (REFONTE INTERFACE, Section 10) : visibilité dynamique des colonnes
    // facultatives (IA/fiscales) du tableau des articles — masquées automatiquement lorsqu'AUCUN
    // article affiché ne porte réellement cette donnée, affichées dès qu'au moins un article la porte.
    // Ne supprime RIEN du modèle : une pure dérivation d'affichage, recalculée à chaque changement des
    // lignes (ajout/suppression/import Excel) et après chaque calcul.
    // ------------------------------------------------------------------------------------------
    public bool HasAnyAiProposedHs { get => _hasAnyAiProposedHs; private set => SetField(ref _hasAnyAiProposedHs, value); }
    public bool HasAnyManualVatRate { get => _hasAnyManualVatRate; private set => SetField(ref _hasAnyManualVatRate, value); }
    public bool HasAnyVatExemptionReason { get => _hasAnyVatExemptionReason; private set => SetField(ref _hasAnyVatExemptionReason, value); }
    public bool HasAnyExcelDutyRate { get => _hasAnyExcelDutyRate; private set => SetField(ref _hasAnyExcelDutyRate, value); }

    /// <summary>Section 11/13 : la colonne "PU Autorisation"/"Total Autorisation" n'a de sens que si la devise d'autorisation diffère de la devise principale de l'importation.</summary>
    public bool NeedsAuthorizationDisplay =>
        !string.Equals(_operation?.AuthorizationCurrencyCode, _operation?.MainCurrencyCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>Symbole de la devise d'autorisation (ex : "$"), pour les en-têtes dynamiques "PU $"/"Total $" — Section 11.</summary>
    public string AuthorizationCurrencySymbol => CurrencyDisplay.SymbolFor(_operation?.AuthorizationCurrencyCode);

    private void RecomputeDynamicColumnVisibility()
    {
        HasAnyAiProposedHs = Lines.Any(l => !string.IsNullOrWhiteSpace(l.Line.AiProposedHsCode10));
        HasAnyManualVatRate = Lines.Any(l => l.ManualVatRatePercent.HasValue);
        HasAnyVatExemptionReason = Lines.Any(l => !string.IsNullOrWhiteSpace(l.VatExemptionReasonFr));
        HasAnyExcelDutyRate = Lines.Any(l => l.ExcelDutyRatePercent.HasValue);
        OnPropertyChanged(nameof(NeedsAuthorizationDisplay));
        OnPropertyChanged(nameof(AuthorizationCurrencySymbol));
    }

    /// <summary>
    /// Convertit <see cref="TotalValeurDouaneDzd"/> (toujours calculée/stockée en DZD) vers la devise
    /// choisie pour l'affichage — jamais l'inverse (Section 8 : "ne doit absolument pas modifier la valeur
    /// douanière réglementaire"). Utilise le même taux réglementaire officiel (ou la surcharge manuelle de
    /// l'importation) que le calcul douanier lui-même — jamais le taux commercial d'autorisation.
    /// </summary>
    private void RefreshCustomsValueDisplayAmount()
    {
        if (_operation == null) { CustomsValueDisplayAmount = null; return; }

        if (CustomsValueDisplayCurrencyIso == "DZD")
        {
            CustomsValueDisplayAmount = TotalValeurDouaneDzd;
            return;
        }

        var provider = _engineFactory.CreateExchangeRateProvider();
        decimal? rateToDzd = string.Equals(CustomsValueDisplayCurrencyIso, _operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase)
                              && _operation.ManualExchangeRateOverride.HasValue
            ? _operation.ManualExchangeRateOverride
            : provider.GetRegulatoryRate(CustomsValueDisplayCurrencyIso, _operation.ReferenceDate)?.RateToDzd;

        CustomsValueDisplayAmount = ProfitCalculator.ConvertDzdToDisplayCurrency(TotalValeurDouaneDzd, rateToDzd);
    }
    public string ExchangeRateInfo { get => _exchangeRateInfo; private set => SetField(ref _exchangeRateInfo, value); }
    public string IncotermGuidance { get => _incotermGuidance; private set => SetField(ref _incotermGuidance, value); }

    /// <summary>Section 11 du plan multi-devises : résumé affiché "Devise facture / Taux / Montant facture / Montant autorisation".</summary>
    public string AuthorizationExchangeRateInfo { get => _authorizationExchangeRateInfo; private set => SetField(ref _authorizationExchangeRateInfo, value); }
    public string AuthorizationConversionSummary { get => _authorizationConversionSummary; private set => SetField(ref _authorizationConversionSummary, value); }

    public bool IsManualRateMode
    {
        get => _operation?.ManualExchangeRateOverride.HasValue ?? false;
        set
        {
            if (!value) _operation.ManualExchangeRateOverride = null;
            else _operation.ManualExchangeRateOverride ??= 0m;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ManualRateValue));
        }
    }

    public decimal? ManualRateValue
    {
        get => _operation?.ManualExchangeRateOverride;
        set { _operation.ManualExchangeRateOverride = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Section 13 du plan multi-devises : devise de l'autorisation d'importation (USD par défaut).
    /// Strictement distincte de la devise réglementaire (toujours DZD pour le calcul douanier).
    /// </summary>
    public string AuthorizationCurrencyCode
    {
        get => _operation?.AuthorizationCurrencyCode ?? "USD";
        set
        {
            if (_operation == null) return;
            _operation.AuthorizationCurrencyCode = string.IsNullOrWhiteSpace(value) ? "USD" : value.Trim().ToUpperInvariant();
            OnPropertyChanged();
            RefreshAuthorizationExchangeRateInfo();
            OnPropertyChanged(nameof(NeedsAuthorizationDisplay));
            OnPropertyChanged(nameof(AuthorizationCurrencySymbol));
        }
    }

    /// <summary>Taux commercial MANUEL (devise facture -> devise d'autorisation), distinct du taux réglementaire manuel.</summary>
    public bool IsManualAuthorizationRateMode
    {
        get => _operation?.ManualAuthorizationExchangeRateOverride.HasValue ?? false;
        set
        {
            if (!value) _operation.ManualAuthorizationExchangeRateOverride = null;
            else _operation.ManualAuthorizationExchangeRateOverride ??= 0m;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ManualAuthorizationRateValue));
            RefreshAuthorizationExchangeRateInfo();
        }
    }

    public decimal? ManualAuthorizationRateValue
    {
        get => _operation?.ManualAuthorizationExchangeRateOverride;
        set { _operation.ManualAuthorizationExchangeRateOverride = value; OnPropertyChanged(); RefreshAuthorizationExchangeRateInfo(); }
    }

    /// <summary>
    /// Revue du 2026-10-02 (Section 15 — "PRCT/TCS introuvable : demander UNE FOIS pour tout l'import").
    /// Taux PRCT confirmé manuellement pour CETTE importation, appliqué uniquement aux lignes dont le code
    /// SH ne dispose d'aucune règle réglementaire PRCT officielle (voir ImportCalculationOrchestrator).
    /// </summary>
    public bool IsManualPrctMode
    {
        get => _operation?.UserConfirmedManualPrct ?? false;
        set { _operation.UserConfirmedManualPrct = value; OnPropertyChanged(); OnPropertyChanged(nameof(ManualPrctRateValue)); }
    }

    public decimal? ManualPrctRateValue
    {
        get => _operation?.ManualPrctRatePercent;
        set { _operation.ManualPrctRatePercent = value; OnPropertyChanged(); }
    }

    /// <summary>Même principe que <see cref="IsManualPrctMode"/>/<see cref="ManualPrctRateValue"/>, pour la TCS.</summary>
    public bool IsManualTcsMode
    {
        get => _operation?.UserConfirmedManualTcs ?? false;
        set { _operation.UserConfirmedManualTcs = value; OnPropertyChanged(); OnPropertyChanged(nameof(ManualTcsRateValue)); }
    }

    public decimal? ManualTcsRateValue
    {
        get => _operation?.ManualTcsRatePercent;
        set { _operation.ManualTcsRatePercent = value; OnPropertyChanged(); }
    }

    // ------------------------------------------------------------------------------------------
    // Revue du 2026-10-02 (CORRECTION URGENTE — "ne plus bloquer le calcul faute de RegulatoryRule") :
    // TAUX DE TAXES PAR DÉFAUT de l'importation (Section 20 de la demande). Actifs par défaut
    // (UseDefaultRatesWhenRuleMissing = true) : utilisés UNIQUEMENT lorsqu'aucune RegulatoryRule
    // officielle n'a pu être résolue pour un article — jamais pour remplacer une règle officielle.
    // ------------------------------------------------------------------------------------------
    public bool UseDefaultRatesWhenRuleMissing
    {
        get => _operation?.UseDefaultRatesWhenRuleMissing ?? true;
        set { _operation.UseDefaultRatesWhenRuleMissing = value; OnPropertyChanged(); }
    }

    public decimal DefaultDdRateValue
    {
        get => _operation?.DefaultDdRatePercent ?? 0m;
        set { _operation.DefaultDdRatePercent = value; OnPropertyChanged(); }
    }

    public decimal DefaultCsRateValue
    {
        get => _operation?.DefaultCsRatePercent ?? 3.0m;
        set { _operation.DefaultCsRatePercent = value; OnPropertyChanged(); }
    }

    public decimal DefaultPrctRateValue
    {
        get => _operation?.DefaultPrctRatePercent ?? 2.0m;
        set { _operation.DefaultPrctRatePercent = value; OnPropertyChanged(); }
    }

    public decimal DefaultTvaRateValue
    {
        get => _operation?.DefaultTvaRatePercent ?? 19.0m;
        set { _operation.DefaultTvaRatePercent = value; OnPropertyChanged(); }
    }

    public decimal DefaultTcsRateValue
    {
        get => _operation?.DefaultTcsRatePercent ?? 0m;
        set { _operation.DefaultTcsRatePercent = value; OnPropertyChanged(); }
    }

    /// <summary>Montant RPS suggéré par défaut (DZD, informatif — jamais recalculé automatiquement, voir <see cref="AddFeeFromTemplate"/>).</summary>
    public decimal DefaultRpsAmountValue
    {
        get => _operation?.DefaultRpsAmountDzd ?? 0m;
        set { _operation.DefaultRpsAmountDzd = value; OnPropertyChanged(); }
    }

    public RelayCommand BackCommand { get; }
    public RelayCommand AddLineCommand { get; }
    public RelayCommand<ImportLineRowViewModel> RemoveLineCommand { get; }
    public RelayCommand ImportExcelCommand { get; }
    public RelayCommand CalculateCommand { get; }
    public RelayCommand ExportExcelCommand { get; }
    public RelayCommand ExportPdfCommand { get; }
    public RelayCommand<ImportLineRowViewModel> ConfirmHsCommand { get; }
    public RelayCommand<ImportLineRowViewModel> ViewDetailCommand { get; }
    public RelayCommand AddFeeCommand { get; }
    public RelayCommand<ImportFee> RemoveFeeCommand { get; }

    private void RefreshExchangeRateInfo()
    {
        if (string.Equals(_operation.MainCurrencyCode, "DZD", StringComparison.OrdinalIgnoreCase))
        {
            ExchangeRateInfo = "Devise principale = DA : aucune conversion nécessaire.";
            return;
        }

        var provider = _engineFactory.CreateExchangeRateProvider();
        var official = provider.GetRegulatoryRate(_operation.MainCurrencyCode, _operation.ReferenceDate);
        ExchangeRateInfo = official == null
            ? $"INFORMATION NON DÉTERMINÉE : aucun taux officiel enregistré pour {_operation.MainCurrencyCode} au {_operation.ReferenceDate:dd/MM/yyyy}."
            : $"Taux officiel enregistré : 1 {_operation.MainCurrencyCode} = {official.RateToDzd:F4} DA ({official.SourceName}, valide depuis le {official.ValidFrom:dd/MM/yyyy}).";
    }

    /// <summary>
    /// Section 11 du plan multi-devises : affiche clairement la devise de la facture, le taux commercial
    /// (officiel ou manuel) vers la devise de l'autorisation d'importation, et le montant équivalent.
    /// Jamais mélangé avec <see cref="RefreshExchangeRateInfo"/> (conversion réglementaire vers DZD).
    /// </summary>
    private void RefreshAuthorizationExchangeRateInfo()
    {
        if (_operation == null) return;

        if (string.Equals(_operation.AuthorizationCurrencyCode, _operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            AuthorizationExchangeRateInfo = $"Devise de l'autorisation = devise de la facture ({_operation.MainCurrencyCode}) : aucune conversion commerciale nécessaire.";
            return;
        }

        if (_operation.ManualAuthorizationExchangeRateOverride.HasValue && _operation.ManualAuthorizationExchangeRateOverride.Value > 0m)
        {
            AuthorizationExchangeRateInfo = $"⚠️ TAUX MANUEL : 1 {_operation.MainCurrencyCode} = {_operation.ManualAuthorizationExchangeRateOverride.Value:F4} {_operation.AuthorizationCurrencyCode} (saisi par l'utilisateur).";
            return;
        }

        var conversionService = _engineFactory.CreateCommercialConversionService();
        var (rate, official, _) = conversionService.ResolveCrossRate(
            _operation.MainCurrencyCode, _operation.AuthorizationCurrencyCode, _operation.ReferenceDate, null);

        AuthorizationExchangeRateInfo = official == null
            ? $"INFORMATION NON DÉTERMINÉE : aucun taux commercial officiel enregistré pour {_operation.MainCurrencyCode} → {_operation.AuthorizationCurrencyCode} au {_operation.ReferenceDate:dd/MM/yyyy}. Publiez-le dans l'écran \"Taux de change\" ou saisissez un taux manuel."
            : $"Taux commercial officiel : 1 {_operation.MainCurrencyCode} = {rate:F4} {_operation.AuthorizationCurrencyCode} ({official.SourceName}, valide depuis le {official.ValidFrom:dd/MM/yyyy}).";
    }

    private void RefreshIncotermGuidance()
    {
        var fields = _incotermFieldService.GetRequiredDynamicFields(_operation.Incoterm);
        IncotermGuidance = fields.Count == 0
            ? "Aucune exigence spécifique supplémentaire pour cet Incoterm."
            : "Frais à vérifier pour l'Incoterm " + _operation.Incoterm + " : " +
              string.Join(" ; ", fields.Select(f => $"{f.LabelFr} ({f.LegalBasisArticleFr})"));
    }

    private void AddManualLine()
    {
        int nextNumber = Lines.Count == 0 ? 1 : Lines.Max(l => l.Line.LineNumber) + 1;
        var line = new ImportLine
        {
            LineNumber = nextNumber,
            ProductReference = $"ART-{nextNumber:D3}",
            Designation = "Nouvel article",
            Quantity = 1m,
            UnitPurchasePrice = 0m,
            CurrencyCode = _operation.MainCurrencyCode,
            OriginCountryIso2 = _operation.DefaultOriginCountryIso2
        };
        Lines.Add(new ImportLineRowViewModel(line));
    }

    private void RemoveLine(ImportLineRowViewModel? row)
    {
        if (row == null) return;
        _session.RequireNotConsultation("supprimer une ligne d'article");
        Lines.Remove(row);
    }

    private void AddFeeFromTemplate()
    {
        var template = SelectedFeeTemplate ?? ImportFeeCatalog.StandardTemplates[0];

        // Revue du 2026-10-02 (cas de référence D10 réel, Section 6 — CRITIQUE : éviter le double comptage
        // du fret). En Incoterm CFR, le prix facturé inclut déjà le fret jusqu'au point convenu (voir
        // IncotermDynamicFieldService, qui ne liste d'ailleurs PAS "FRET_INTERNATIONAL" comme champ requis
        // pour CFR, contrairement à EXW/FOB). Si l'utilisateur ajoute quand même ce frais sous CFR, il est
        // par défaut marqué "Déjà inclus dans le prix facturé" (jamais ajouté une seconde fois à la valeur
        // en douane) plutôt que d'hériter silencieusement du traitement "Addition" prévu pour EXW/FOB.
        // L'utilisateur reste libre de corriger ce traitement si ce fret est réellement un complément non
        // compris dans le prix CFR — ImportCalculationOrchestrator.ValidateIncotermRequiredFees avertit de
        // toute façon si un tel frais reste configuré en "Addition" sous CFR.
        bool isCfrFreightAlreadyIncluded = _operation.Incoterm == IncotermCode.CFR
            && template.CategoryCode.Contains("FRET", StringComparison.OrdinalIgnoreCase);

        Fees.Add(new ImportFee
        {
            FeeCategoryCode = template.CategoryCode,
            FeeName = template.DefaultLabelFr,
            Amount = 0m,
            CurrencyCode = _operation.MainCurrencyCode,
            AllocationMethod = template.SuggestedAllocationMethod,
            IncludeInCustomsValue = isCfrFreightAlreadyIncluded ? false : template.DefaultIncludeInCustomsValue,
            CustomsTreatment = isCfrFreightAlreadyIncluded
                ? CustomsAdjustmentTreatment.IncludedInInvoicePrice
                : template.DefaultCustomsTreatment,
            IncludeInCostOfGoods = template.DefaultIncludeInCostOfGoods
        });
    }

    private void RemoveFee(ImportFee? fee)
    {
        if (fee == null) return;
        _session.RequireNotConsultation("supprimer un frais");
        Fees.Remove(fee);
    }

    private void OpenExcelWizard()
    {
        var wizardVm = _serviceProvider.GetRequiredService<ExcelImportWizardViewModel>();
        wizardVm.Initialize(_company, _operation);
        var window = new ExcelImportWizardWindow { DataContext = wizardVm, Owner = Application.Current.MainWindow };

        if (window.ShowDialog() == true && wizardVm.ImportedLines.Count > 0)
        {
            int nextNumber = Lines.Count == 0 ? 1 : Lines.Max(l => l.Line.LineNumber) + 1;
            foreach (var line in wizardVm.ImportedLines)
            {
                line.LineNumber = nextNumber++;
                Lines.Add(new ImportLineRowViewModel(line));
            }

            _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
                "EXCEL_IMPORT", "IMPORT_LINES_FROM_EXCEL",
                newValue: $"{wizardVm.ImportedLines.Count} ligne(s) importée(s)", importOperationId: _operation.Id);
        }
    }

    private void OpenHsConfirmDialog(ImportLineRowViewModel? row)
    {
        if (row == null) return;
        _session.RequireNotConsultation("confirmer un code SH proposé par l'IA");

        var classifier = _engineFactory.CreateHsClassifier();
        var baseInput = new HsClassificationInput(
            row.ProductReference, row.Designation, null, row.OriginCountryIso2, null);

        // Revue du 2026-10-01 (point 4) : jusqu'à 3 candidats sont désormais proposés (au lieu d'un seul),
        // chacun avec justification, niveau de confiance, codes alternatifs et informations manquantes.
        var initialCandidates = classifier.ClassifyCandidates(baseInput);

        row.Line.AiProposedHsCode10 = initialCandidates.FirstOrDefault()?.HsCode10;
        row.Line.AiHsDecision = AiProposalDecision.PendingUserValidation;

        var dialogVm = new HsConfirmDialogViewModel(
            classifier, baseInput, initialCandidates, row.ProductReference, row.Designation, row.OriginCountryIso2);
        var window = new HsConfirmDialog { DataContext = dialogVm, Owner = Application.Current.MainWindow };

        if (window.ShowDialog() == true)
        {
            var proposal = dialogVm.BuildProposalForSelectedCandidate();
            classifier.ApplyUserDecisionOnImportLine(row.Line, proposal, dialogVm.Decision, dialogVm.ModifiedHsCode);
            OnPropertyChanged(nameof(Lines));
            row.OnHsChanged();

            // Trace complète de la validation (point 4 de la revue) : proposition IA + justification,
            // date (TimestampUtc, automatique), source/version du service, utilisateur ayant validé
            // (UserId/UserDisplayName), et code finalement retenu (NewValue).
            _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
                "HS_CLASSIFICATION", $"AI_HS_{dialogVm.Decision}".ToUpperInvariant(),
                oldValue: $"Proposé par IA : {proposal.ProposedHsCode10} (confiance {proposal.ConfidencePercent:F0} %) — Justification : {proposal.JustificationFr}",
                newValue: row.Line.HsCodeConfirmed10,
                importOperationId: _operation.Id,
                regulatoryRuleCode: proposal.ProposedHsCode10,
                legalSourceReference: classifier.ServiceNameAndVersion,
                regulatoryVersionCode: classifier.ServiceNameAndVersion);
        }
    }

    private void OpenLineDetailDialog(ImportLineRowViewModel? row)
    {
        if (row?.Result == null)
        {
            MessageBox.Show("Veuillez exécuter le calcul avant de consulter le détail de cet article.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new LineDetailDialog { DataContext = new LineDetailDialogViewModel(row), Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    private void Calculate()
    {
        try
        {
            _session.RequireNotConsultation("exécuter le calcul du coût d'importation");

            _operation.Lines.Clear();
            _operation.Lines.AddRange(Lines.Select(r => r.Line));
            _operation.Fees.Clear();
            _operation.Fees.AddRange(Fees);

            var orchestrator = _engineFactory.CreateOrchestrator();
            var summary = orchestrator.ExecuteCalculation(_company, _operation);
            _lastSummary = summary;

            // Revue du 2026-10-02 (REFONTE INTERFACE, Section 10/18) : identifiants des frais RPS (déplacé
            // avant la boucle ci-dessous pour alimenter la nouvelle colonne RPS par article, SANS jamais
            // dupliquer le montant total — chaque ligne ne reçoit que sa PART déjà répartie par le moteur).
            var rpsFeeIds = _operation.Fees
                .Where(f => string.Equals(f.FeeCategoryCode, "RPS", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Id)
                .ToHashSet();

            foreach (var row in Lines)
            {
                row.Result = summary.LineResults.FirstOrDefault(l => l.LineNumber == row.Line.LineNumber);
                row.AnomaliesForLine = summary.Anomalies.Where(a => a.LineNumber == row.Line.LineNumber).ToList();
                row.RpsAllocatedDzd = row.Result?.FeeAllocations
                    .Where(a => rpsFeeIds.Contains(a.FeeId))
                    .Sum(a => a.AllocatedAmountDzd);
                row.RaiseEtatChanged();
            }

            Anomalies.Clear();
            foreach (var a in summary.Anomalies.OrderByDescending(a => a.Severity))
                Anomalies.Add(a);

            HasBlockingAnomalies = summary.HasBlockingAnomalies;
            TotalCoutRevientDzd = summary.TotalRealCostOfGoodsDzd;
            TotalValeurDouaneDzd = summary.TotalCustomsValueDzd;
            TotalDroitsDouaneDzd = summary.TotalCustomsDutyDzd;
            TotalTvaDzd = summary.TotalImportVatDzd;
            TotalAutresTaxesDzd = summary.TotalAdditionalTaxesDzd;
            TotalFraisDzd = summary.TotalImportFeesDzd;

            // Section 14 : détail de chaque taxe additionnelle séparément (plus un seul total agrégé).
            var allAppliedTaxes = summary.LineResults.SelectMany(l => l.CustomsOutcome.AdditionalTaxes).ToList();
            decimal SumTax(string code) => allAppliedTaxes
                .Where(t => string.Equals(t.TaxCode, code, StringComparison.OrdinalIgnoreCase))
                .Sum(t => t.TaxAmountDzd);
            TotalCsDzd = SumTax("CS");
            TotalPrctDzd = SumTax("PRCT");
            TotalTcsDzd = SumTax("TCS");
            TotalDapsDzd = SumTax("DAPS");

            TotalRpsDzd = summary.LineResults
                .SelectMany(l => l.FeeAllocations)
                .Where(a => rpsFeeIds.Contains(a.FeeId))
                .Sum(a => a.AllocatedAmountDzd);

            // Section 9 : TOTAL DÉDOUANEMENT = DD + CS + TVA + PRCT + TCS + RPS (jamais les frais
            // commerciaux/importation, jamais la DAPS — formule explicite de la demande).
            TotalDedouanementDzd = TotalDroitsDouaneDzd + TotalCsDzd + TotalTvaDzd + TotalPrctDzd + TotalTcsDzd + TotalRpsDzd;

            RecomputeDynamicColumnVisibility();
            RefreshCustomsValueDisplayAmount();

            // Section 15 : distinguer un calcul réellement bloqué (bandeau rouge, inchangé) d'un calcul
            // PROVISOIRE (bandeau orange) qui s'est exécuté avec succès mais en utilisant au moins un taux
            // PAR DÉFAUT de l'importation ou une taxe laissée NON DÉTERMINÉE (jamais un blocage — Section
            // 1 de la correction du 2026-10-02 : l'absence de RegulatoryRule ne bloque plus le calcul).
            bool usedDefaultOrUndeterminedRates = summary.Anomalies.Any(a =>
                a.AnomalyCode.EndsWith("_DEFAULT_RATE_USED", StringComparison.OrdinalIgnoreCase) ||
                a.AnomalyCode.EndsWith("_RATE_NOT_DETERMINED", StringComparison.OrdinalIgnoreCase) ||
                a.AnomalyCode is "REGULATORY_RULE_NOT_FOUND" or "MISSING_HS_CODE");
            IsProvisionalCalculation = !summary.HasBlockingAnomalies && usedDefaultOrUndeterminedRates;
            ProvisionalMessageFr = IsProvisionalCalculation
                ? "⚠ Calcul provisoire : certaines règles réglementaires n'ont pas été trouvées. Des taux par défaut de l'importation ont été utilisés (ou certaines taxes restent non déterminées). Vérifiez les données avant de considérer le résultat comme définitif."
                : string.Empty;

            _operationRepository.SaveOperation(_operation);

            string versionLabel = summary.LineResults
                .SelectMany(l => l.CustomsOutcome.AdditionalTaxes.Select(t => t.RegulatoryVersionCode))
                .Concat(new[] { "N/A" })
                .Distinct()
                .First();

            _snapshotRepository.SaveSnapshot(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, versionLabel, summary);

            // Section 22 de la correction du 2026-10-02 : trace explicitement dans l'audit chaque taux PAR
            // DÉFAUT de l'importation réellement utilisé pour ce calcul (jamais une simple absence silencieuse).
            string defaultRatesAuditNote = string.Empty;
            if (IsProvisionalCalculation)
            {
                var defaultUsageByCode = summary.Anomalies
                    .Where(a => a.AnomalyCode.EndsWith("_DEFAULT_RATE_USED", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(a => a.AnomalyCode)
                    .Select(g => $"{g.Key} x{g.Count()}")
                    .ToList();
                if (defaultUsageByCode.Count > 0)
                {
                    defaultRatesAuditNote = $" — Taux par défaut (DEFAULT_IMPORT) utilisés : {string.Join(", ", defaultUsageByCode)}.";
                }
            }

            _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
                "CALCULATION", "EXECUTE_CALCULATION",
                newValue: $"{summary.TotalRealCostOfGoodsDzd:N2} DA — {summary.Anomalies.Count} anomalie(s){defaultRatesAuditNote}",
                importOperationId: _operation.Id,
                regulatoryVersionCode: IsProvisionalCalculation ? "DEFAULT_IMPORT" : null);

            RefreshExchangeRateInfo();
            RefreshAuthorizationExchangeRateInfo();

            var commercialConversion = summary.CommercialAuthorizationConversion;
            AuthorizationConversionSummary = commercialConversion == null
                ? string.Empty
                : $"Montant facture : {commercialConversion.OriginalTotalAmount:N2} {commercialConversion.OriginalCurrencyCode}  →  Montant autorisation : {commercialConversion.AuthorizationTotalAmount:N2} {commercialConversion.AuthorizationCurrencyCode} (taux {commercialConversion.EffectiveRate:F4}, {commercialConversion.RateTypeLabelFr})";

            MessageBox.Show(
                summary.HasBlockingAnomalies
                    ? "Calcul exécuté avec des anomalies BLOQUANTES : le coût de revient ne peut pas être considéré comme définitif tant qu'elles ne sont pas résolues."
                    : IsProvisionalCalculation
                        ? $"{ProvisionalMessageFr}\nCoût total de revient (provisoire) : {summary.TotalRealCostOfGoodsDzd:N2} DA."
                        : $"Calcul exécuté avec succès.\nCoût total de revient : {summary.TotalRealCostOfGoodsDzd:N2} DA.",
                "CIMP — Résultat du calcul", MessageBoxButton.OK,
                summary.HasBlockingAnomalies ? MessageBoxImage.Warning
                    : IsProvisionalCalculation ? MessageBoxImage.Warning
                    : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CIMP — Action refusée", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportExcel()
    {
        if (_lastSummary == null)
        {
            MessageBox.Show("Veuillez d'abord exécuter le calcul.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"Rapport_{_operation.ImportNumber}.xlsx",
            Filter = "Classeur Excel (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog() != true) return;

        var reportBuilder = _engineFactory.CreateReportBuilder();
        var model = reportBuilder.BuildDynamicFiveSheetExcelReport(_company, _operation, _lastSummary, "Voir feuille TAXES pour le détail par ligne");
        ExcelWorkbookWriter.WriteToFile(model, dialog.FileName);

        _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
            "EXPORT", "EXPORT_EXCEL", newValue: dialog.FileName, importOperationId: _operation.Id);

        MessageBox.Show($"Export Excel généré :\n{dialog.FileName}", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ExportPdf()
    {
        if (_lastSummary == null)
        {
            MessageBox.Show("Veuillez d'abord exécuter le calcul.", "CIMP", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = $"Rapport_{_operation.ImportNumber}.pdf",
            Filter = "Document PDF (*.pdf)|*.pdf"
        };
        if (dialog.ShowDialog() != true) return;

        var reportBuilder = _engineFactory.CreateReportBuilder();
        var model = reportBuilder.BuildDynamicPdfReport(_company, _operation, _lastSummary, "Voir feuille TAXES pour le détail par ligne");
        PdfReportWriter.WriteToFile(model, dialog.FileName);

        _audit.RecordAction(_company.Id, _session.CurrentUser?.Id ?? Guid.Empty, _session.CurrentUser?.DisplayName ?? "Inconnu",
            "EXPORT", "EXPORT_PDF", newValue: dialog.FileName, importOperationId: _operation.Id);

        MessageBox.Show($"Export PDF généré :\n{dialog.FileName}", "CIMP", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
