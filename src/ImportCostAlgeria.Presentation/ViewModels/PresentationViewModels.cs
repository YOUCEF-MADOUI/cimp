using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Core.Services;

namespace ImportCostAlgeria.Presentation.ViewModels;

/// <summary>
/// Menu principal de l'application (Section 37).
/// </summary>
public enum MainNavigationMenuItem
{
    TableauDeBord,
    Importations,
    NouvelleImportation,
    ImporterExcel,
    Produits,
    Reglementation,
    Simulateur,
    Rapports,
    Anomalies,
    AssistantIa,
    Parametres
}

/// <summary>
/// Indicateurs consolidés du Tableau de Bord (Section 38).
/// </summary>
public sealed record DashboardKpiSummary(
    int NombreImportations,
    decimal ValeurTotaleImporteeDzd,
    decimal TotalDroitsDeDouaneDzd,
    decimal TotalTaxesDzd,
    decimal TotalFraisDzd,
    decimal CoutTotalDzd,
    int NombreDeProduits,
    int NombreDAnomalies,
    int ImportationsNecessitantValidation);

public sealed class DashboardViewModel
{
    public static IReadOnlyList<(MainNavigationMenuItem Key, string LabelFr)> MainMenu => new[]
    {
        (MainNavigationMenuItem.TableauDeBord,       "TABLEAU DE BORD"),
        (MainNavigationMenuItem.Importations,        "IMPORTATIONS"),
        (MainNavigationMenuItem.NouvelleImportation, "NOUVELLE IMPORTATION"),
        (MainNavigationMenuItem.ImporterExcel,       "IMPORTER EXCEL"),
        (MainNavigationMenuItem.Produits,            "PRODUITS"),
        (MainNavigationMenuItem.Reglementation,      "RÉGLEMENTATION"),
        (MainNavigationMenuItem.Simulateur,          "SIMULATEUR"),
        (MainNavigationMenuItem.Rapports,            "RAPPORTS"),
        (MainNavigationMenuItem.Anomalies,           "ANOMALIES"),
        (MainNavigationMenuItem.AssistantIa,         "ASSISTANT IA"),
        (MainNavigationMenuItem.Parametres,          "PARAMÈTRES")
    };

    public DashboardKpiSummary BuildDashboardSummary(
        IReadOnlyList<ImportCalculationSummary> calculations,
        int companyProductCount)
    {
        var realCalcs = calculations.Where(c => !c.IsSimulation).ToList();
        return new DashboardKpiSummary(
            NombreImportations: realCalcs.Count,
            ValeurTotaleImporteeDzd: realCalcs.Sum(c => c.TotalPurchaseValueDzd),
            TotalDroitsDeDouaneDzd: realCalcs.Sum(c => c.TotalCustomsDutyDzd),
            TotalTaxesDzd: realCalcs.Sum(c => c.TotalAdditionalTaxesDzd + c.TotalImportVatDzd),
            TotalFraisDzd: realCalcs.Sum(c => c.TotalImportFeesDzd),
            CoutTotalDzd: realCalcs.Sum(c => c.TotalRealCostOfGoodsDzd),
            NombreDeProduits: companyProductCount,
            NombreDAnomalies: realCalcs.Sum(c => c.Anomalies.Count),
            ImportationsNecessitantValidation: realCalcs.Count(c => c.Anomalies.Count > 0));
    }
}
