using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.CalculationEngine;
using ImportCostAlgeria.Core.Domain;

namespace ImportCostAlgeria.Reporting;

/// <summary>
/// Feuille 1 : DETAIL_ARTICLES (Sections 17, 23 & 35).
/// </summary>
public sealed record ExcelReportDetailRow(
    int LineNumber,
    string Reference,
    string Designation,
    decimal Quantite,
    decimal PrixFournisseurUnitaire,
    string Devise,
    decimal TauxChange,
    decimal ValeurConvertieDzd,
    string CodeSh,
    string Origine,
    string Incoterm,
    decimal PartFraisDouaneDzd,
    decimal ValeurDouaniereDzd,
    decimal DroitDouaneDzd,
    decimal AutresTaxesDzd,
    decimal TvaDzd,
    decimal AutresFraisLocauxDzd,
    decimal TotalFraisRepartisDzd,
    decimal CoutTotalRevientDzd,
    decimal CoutUnitaireRevientDzd);

/// <summary>
/// Feuille 2 : RECAPITULATIF (Sections 22, 23 & 35).
/// </summary>
public sealed record ExcelReportRecapSheet(
    decimal ValeurFournisseurDzd,
    decimal TransportInternationalDzd,
    decimal AssuranceDzd,
    decimal AutresFraisDzd,
    decimal TotalFraisDzd,
    decimal ValeurDouaniereDzd,
    decimal TotalDroitsDouaneDzd,
    decimal TotalAutresTaxesDzd,
    decimal TotalTvaDzd,
    decimal TotalDroitsEtTaxesDzd,
    decimal CoutAcquisitionHorsTvaDzd,
    decimal CoutTotalRevientDzd,
    decimal CoutMoyenUnitaireDzd,
    bool IsVatNonRecoverable,
    string RegulatoryVersionCode,
    string LegalDisclaimerFr);

/// <summary>
/// Feuille 3 : FRAIS — Détail et vérification de la répartition des frais (Sections 15, 16 & 23).
/// </summary>
public sealed record ExcelReportFeeSheetRow(
    string FeeName,
    string CategoryCode,
    decimal AmountCurrency,
    string CurrencyCode,
    decimal ExchangeRateToDzd,
    decimal AmountConvertedDzd,
    string AllocationMethod,
    bool IncludedInCustomsValue,
    bool IncludedInCostOfGoods,
    decimal SumOfLineAllocationsDzd,
    bool IsAllocationSumExact);

/// <summary>
/// Feuille 4 : TAXES — Détail explicatif de chaque droit et taxe (Sections 13, 22 & 23).
/// </summary>
public sealed record ExcelReportTaxSheetRow(
    int LineNumber,
    string ProductReference,
    string HsCode10,
    string TaxCode,
    string TaxNameFr,
    decimal TaxableBaseDzd,
    decimal RatePercent,
    decimal TaxAmountDzd,
    bool IsNonRecoverable,
    string LegalSourceAndArticle,
    string RegulatoryVersionCode);

/// <summary>
/// Feuille 5 : CONTROLES — Anomalies, avertissements et blocages (Sections 19, 22 & 23).
/// </summary>
public sealed record ExcelReportControlSheetRow(
    string Severity,
    string AnomalyCode,
    int? LineNumber,
    string MessageFr,
    string? ExpectedRegulatoryValue,
    string? ActualUserOrExcelValue);

/// <summary>
/// Modèle complet du classeur Excel dynamique à 5 feuilles (Section 23).
/// </summary>
public sealed record ExcelWorkbookReportModel(
    string WorkbookTitle,
    IReadOnlyList<ExcelReportDetailRow> DetailArticlesSheet,
    ExcelReportRecapSheet RecapitulatifSheet,
    IReadOnlyList<ExcelReportFeeSheetRow> FraisSheet,
    IReadOnlyList<ExcelReportTaxSheetRow> TaxesSheet,
    IReadOnlyList<ExcelReportControlSheetRow> ControlesSheet);

/// <summary>
/// Modèle complet du rapport PDF dynamique (Section 24 & 36).
/// </summary>
public sealed record PdfProfessionalReportModel(
    string ImportNumber,
    DateOnly ReferenceDate,
    DateTime GeneratedAtUtc,
    string CompanyLegalName,
    string? CompanyNif,
    string SupplierName,
    string ExportShippingCountryIso2,
    IReadOnlyList<string> DistinctOriginCountries,
    string IncotermCode,
    string CurrencyCode,
    decimal AppliedExchangeRateToDzd,
    string ArrivalPort,
    string TransportMode,
    ExcelReportRecapSheet FinancialSummary,
    IReadOnlyList<ExcelReportDetailRow> ArticleDetails,
    IReadOnlyList<ExcelReportTaxSheetRow> TaxesBreakdown,
    IReadOnlyList<ExcelReportFeeSheetRow> FeesBreakdown,
    IReadOnlyList<ExcelReportControlSheetRow> ControlsAndAlerts,
    string RegulatoryVersionUsed,
    string MandatoryLegalDisclaimerFr);

public sealed class ReportBuilderService
{
    public ExcelWorkbookReportModel BuildDynamicFiveSheetExcelReport(
        Company company,
        ImportOperation operation,
        ImportCalculationSummary calculation,
        string regulatoryVersionUsed)
    {
        var lineMap = operation.Lines.ToDictionary(l => l.LineNumber);
        var detailRows = new List<ExcelReportDetailRow>();
        var taxRows = new List<ExcelReportTaxSheetRow>();

        foreach (var lineRes in calculation.LineResults)
        {
            var srcLine = lineMap[lineRes.LineNumber];
            string hsCode = srcLine.HsCodeConfirmed10 ?? "NON RENSEIGNÉ";

            detailRows.Add(new ExcelReportDetailRow(
                LineNumber: lineRes.LineNumber,
                Reference: lineRes.ProductReference,
                Designation: lineRes.Designation,
                Quantite: lineRes.Quantity,
                PrixFournisseurUnitaire: srcLine.UnitPurchasePrice,
                Devise: lineRes.CurrencyCode,
                TauxChange: lineRes.AppliedExchangeRateToDzd,
                ValeurConvertieDzd: lineRes.EconomicOutcome.PurchaseValueDzd,
                CodeSh: hsCode,
                Origine: srcLine.OriginCountryIso2 ?? operation.DefaultOriginCountryIso2 ?? "N/A",
                Incoterm: operation.Incoterm.ToString(),
                PartFraisDouaneDzd: lineRes.EconomicOutcome.AllocatedCustomsIncludedFeesDzd,
                ValeurDouaniereDzd: lineRes.CustomsOutcome.CustomsValueDzd,
                DroitDouaneDzd: lineRes.CustomsOutcome.CustomsDutyAmountDzd,
                AutresTaxesDzd: lineRes.CustomsOutcome.TotalAdditionalTaxesDzd,
                TvaDzd: lineRes.CustomsOutcome.ImportVatAmountDzd,
                AutresFraisLocauxDzd: lineRes.EconomicOutcome.AllocatedLocalAndPostCustomsFeesDzd,
                TotalFraisRepartisDzd: lineRes.EconomicOutcome.TotalAllocatedFeesDzd,
                CoutTotalRevientDzd: lineRes.EconomicOutcome.RealCostOfGoodsTotalDzd,
                CoutUnitaireRevientDzd: lineRes.EconomicOutcome.UnitCostOfGoodsDzd));

            // Droit de douane dans la feuille TAXES
            taxRows.Add(new ExcelReportTaxSheetRow(
                LineNumber: lineRes.LineNumber,
                ProductReference: lineRes.ProductReference,
                HsCode10: hsCode,
                TaxCode: "DD",
                TaxNameFr: "Droit de douane",
                TaxableBaseDzd: lineRes.CustomsOutcome.CustomsValueDzd,
                RatePercent: lineRes.CustomsOutcome.CustomsDutyRatePercent,
                TaxAmountDzd: lineRes.CustomsOutcome.CustomsDutyAmountDzd,
                IsNonRecoverable: true,
                LegalSourceAndArticle: "Code des Douanes Algérien Art. 9, 10 & 103 / Tarif Douanier DGD",
                RegulatoryVersionCode: regulatoryVersionUsed));

            // Autres taxes applicables (DAPS, TIC...) dans la feuille TAXES
            foreach (var addTax in lineRes.CustomsOutcome.AdditionalTaxes)
            {
                taxRows.Add(new ExcelReportTaxSheetRow(
                    LineNumber: lineRes.LineNumber,
                    ProductReference: lineRes.ProductReference,
                    HsCode10: hsCode,
                    TaxCode: addTax.TaxCode,
                    TaxNameFr: addTax.TaxNameFr,
                    TaxableBaseDzd: addTax.TaxableBaseDzd,
                    RatePercent: addTax.RatePercent,
                    TaxAmountDzd: addTax.TaxAmountDzd,
                    IsNonRecoverable: addTax.IsNonRecoverable,
                    LegalSourceAndArticle: $"{addTax.LegalArticleReference} ({addTax.JoraReference})",
                    RegulatoryVersionCode: addTax.RegulatoryVersionCode));
            }

            // TVA dans la feuille TAXES
            taxRows.Add(new ExcelReportTaxSheetRow(
                LineNumber: lineRes.LineNumber,
                ProductReference: lineRes.ProductReference,
                HsCode10: hsCode,
                TaxCode: "TVA",
                TaxNameFr: "TVA à l'importation",
                TaxableBaseDzd: lineRes.CustomsOutcome.VatTaxableBaseDzd,
                RatePercent: lineRes.CustomsOutcome.VatRatePercent,
                TaxAmountDzd: lineRes.CustomsOutcome.ImportVatAmountDzd,
                IsNonRecoverable: company.IsImportVatNonRecoverable,
                LegalSourceAndArticle: "Code des Taxes sur le Chiffre d'Affaires (CTCA) Art. 19 & 21",
                RegulatoryVersionCode: regulatoryVersionUsed));
        }

        // Feuille FRAIS
        var feeRows = new List<ExcelReportFeeSheetRow>();
        foreach (var fee in operation.Fees)
        {
            decimal allocatedSum = calculation.LineResults
                .SelectMany(lr => lr.FeeAllocations)
                .Where(fa => fa.FeeId == fee.Id)
                .Sum(fa => fa.AllocatedAmountDzd);

            decimal rateUsed = calculation.LineResults.FirstOrDefault()?.AppliedExchangeRateToDzd ?? 1m;
            if (string.Equals(fee.CurrencyCode, "DZD", StringComparison.OrdinalIgnoreCase))
                rateUsed = 1m;

            decimal convertedDzd = Math.Round(fee.Amount * rateUsed, 2, MidpointRounding.AwayFromZero);

            feeRows.Add(new ExcelReportFeeSheetRow(
                FeeName: fee.FeeName,
                CategoryCode: fee.FeeCategoryCode,
                AmountCurrency: fee.Amount,
                CurrencyCode: fee.CurrencyCode,
                ExchangeRateToDzd: rateUsed,
                AmountConvertedDzd: convertedDzd,
                AllocationMethod: fee.AllocationMethod.ToString(),
                IncludedInCustomsValue: fee.IncludeInCustomsValue,
                IncludedInCostOfGoods: fee.IncludeInCostOfGoods,
                SumOfLineAllocationsDzd: allocatedSum,
                IsAllocationSumExact: Math.Abs(convertedDzd - allocatedSum) <= 0.01m));
        }

        // Feuille CONTROLES
        var controlRows = calculation.Anomalies
            .Select(a => new ExcelReportControlSheetRow(
                Severity: a.Severity.ToString().ToUpperInvariant(),
                AnomalyCode: a.AnomalyCode,
                LineNumber: a.LineNumber,
                MessageFr: a.MessageFr,
                ExpectedRegulatoryValue: a.ExpectedValue,
                ActualUserOrExcelValue: a.ActualValue))
            .ToList();

        decimal totalQty = calculation.LineResults.Sum(x => x.Quantity);
        decimal avgUnitCost = totalQty > 0m
            ? Math.Round(calculation.TotalRealCostOfGoodsDzd / totalQty, 2, MidpointRounding.AwayFromZero)
            : 0m;

        decimal transportDzd = feeRows
            .Where(f => f.CategoryCode.Contains("FRET", StringComparison.OrdinalIgnoreCase) ||
                        f.CategoryCode.Contains("TRANSPORT_INTERIEUR", StringComparison.OrdinalIgnoreCase))
            .Sum(f => f.AmountConvertedDzd);

        decimal assuranceDzd = feeRows
            .Where(f => f.CategoryCode.Contains("ASSURANCE", StringComparison.OrdinalIgnoreCase))
            .Sum(f => f.AmountConvertedDzd);

        decimal otherFeesDzd = calculation.TotalImportFeesDzd - transportDzd - assuranceDzd;

        var recap = new ExcelReportRecapSheet(
            ValeurFournisseurDzd: calculation.TotalPurchaseValueDzd,
            TransportInternationalDzd: transportDzd,
            AssuranceDzd: assuranceDzd,
            AutresFraisDzd: otherFeesDzd,
            TotalFraisDzd: calculation.TotalImportFeesDzd,
            ValeurDouaniereDzd: calculation.TotalCustomsValueDzd,
            TotalDroitsDouaneDzd: calculation.TotalCustomsDutyDzd,
            TotalAutresTaxesDzd: calculation.TotalAdditionalTaxesDzd,
            TotalTvaDzd: calculation.TotalImportVatDzd,
            TotalDroitsEtTaxesDzd: calculation.TotalDutiesAndTaxesDzd,
            CoutAcquisitionHorsTvaDzd: calculation.TotalAcquisitionCostExVatDzd,
            CoutTotalRevientDzd: calculation.TotalRealCostOfGoodsDzd,
            CoutMoyenUnitaireDzd: avgUnitCost,
            IsVatNonRecoverable: company.IsImportVatNonRecoverable,
            RegulatoryVersionCode: regulatoryVersionUsed,
            LegalDisclaimerFr: calculation.MandatoryLegalDisclaimerFr);

        return new ExcelWorkbookReportModel(
            WorkbookTitle: $"Rapport_Importation_{operation.ImportNumber}",
            DetailArticlesSheet: detailRows,
            RecapitulatifSheet: recap,
            FraisSheet: feeRows,
            TaxesSheet: taxRows,
            ControlesSheet: controlRows);
    }

    public PdfProfessionalReportModel BuildDynamicPdfReport(
        Company company,
        ImportOperation operation,
        ImportCalculationSummary calculation,
        string regulatoryVersionUsed)
    {
        var excelModel = BuildDynamicFiveSheetExcelReport(company, operation, calculation, regulatoryVersionUsed);
        var origins = operation.Lines
            .Select(l => l.OriginCountryIso2 ?? operation.DefaultOriginCountryIso2 ?? "N/A")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        decimal effectiveRate = calculation.LineResults.FirstOrDefault()?.AppliedExchangeRateToDzd ?? 1m;

        return new PdfProfessionalReportModel(
            ImportNumber: operation.ImportNumber,
            ReferenceDate: operation.ReferenceDate,
            GeneratedAtUtc: DateTime.UtcNow,
            CompanyLegalName: company.LegalName,
            CompanyNif: company.NIF,
            SupplierName: operation.SupplierName,
            ExportShippingCountryIso2: operation.ExportShippingCountryIso2,
            DistinctOriginCountries: origins,
            IncotermCode: operation.Incoterm.ToString(),
            CurrencyCode: operation.MainCurrencyCode,
            AppliedExchangeRateToDzd: effectiveRate,
            ArrivalPort: operation.ArrivalPortOrBorder,
            TransportMode: operation.TransportMode,
            FinancialSummary: excelModel.RecapitulatifSheet,
            ArticleDetails: excelModel.DetailArticlesSheet,
            TaxesBreakdown: excelModel.TaxesSheet,
            FeesBreakdown: excelModel.FraisSheet,
            ControlsAndAlerts: excelModel.ControlesSheet,
            RegulatoryVersionUsed: regulatoryVersionUsed,
            MandatoryLegalDisclaimerFr: calculation.MandatoryLegalDisclaimerFr);
    }
}
