using ImportCostAlgeria.AI;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Database;
using ImportCostAlgeria.Database.Repositories;
using ImportCostAlgeria.RegulatoryEngine;
using ImportCostAlgeria.Reporting;

namespace ImportCostAlgeria.Presentation.Services;

/// <summary>
/// Assemblage (composition) des moteurs métier indépendants de l'UI à partir des dépôts EF Core.
/// Ceci n'est QUE du câblage d'instances (Dependency Injection manuelle) : aucune règle métier n'est
/// codée ici — toute la logique reste dans ImportCostAlgeria.CalculationEngine / RegulatoryEngine.
/// </summary>
public sealed class EngineFactory
{
    private readonly ICimpDbContextFactory _dbFactory;

    public EngineFactory(ICimpDbContextFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public RegulatoryRuleEngine CreateRegulatoryEngine() =>
        new(new EfRegulatoryRuleRepository(_dbFactory));

    public IExchangeRateProvider CreateExchangeRateProvider() =>
        new EfExchangeRateProvider(_dbFactory);

    /// <summary>Conversion COMMERCIALE entre devises quelconques (ex: EUR -> USD) — jamais réglementaire.</summary>
    public CurrencyConversionService CreateCommercialConversionService() =>
        new(CreateExchangeRateProvider());

    /// <summary>
    /// Résolution RÉGLEMENTAIRE taux devise -&gt; DZD (officiel ou manuel PAR DEVISE). Exposé ici pour que la
    /// vue "Taux de change & Incoterm" puisse afficher/dériver le taux commercial EUR/USD EXACTEMENT comme
    /// ImportCalculationOrchestrator (même méthode, même formule : (A -&gt; DZD) / (B -&gt; DZD)), sans jamais
    /// dupliquer une logique de taux croisé direct dans le ViewModel.
    /// </summary>
    public CurrencyCalculator CreateCurrencyCalculator() =>
        new(CreateExchangeRateProvider());

    public ImportCalculationOrchestrator CreateOrchestrator()
    {
        var rateProvider = new EfExchangeRateProvider(_dbFactory);
        return new ImportCalculationOrchestrator(
            new CurrencyCalculator(rateProvider),
            new CostAllocationEngine(),
            new CustomsValueCalculator(),
            CreateRegulatoryEngine(),
            CreateCommercialConversionService());
    }

    public HSClassifierService CreateHsClassifier() => new();

    public ReportBuilderService CreateReportBuilder() => new();

    public ImportSimulatorService CreateSimulator() => new(CreateOrchestrator());
}
