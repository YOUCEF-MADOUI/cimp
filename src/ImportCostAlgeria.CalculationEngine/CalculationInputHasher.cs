using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ImportCostAlgeria.Core.Domain;

namespace ImportCostAlgeria.CalculationEngine;

/// <summary>
/// Correction 2026-10-02 (demande utilisateur — "Persistance des calculs après fermeture de CIMP",
/// Étape 2) : empreinte (hash) STABLE et DÉTERMINISTE de toutes les données d'ENTRÉE d'une importation
/// qui influencent réellement son résultat de calcul (quantités, prix, devises, taux manuels, Incoterm,
/// frais, méthodes de répartition, taxes manuelles confirmées, taux DD par article...). Sert UNIQUEMENT à
/// détecter qu'une importation a été modifiée depuis son dernier calcul sauvegardé (<see
/// cref="ImportCostAlgeria.Database.Repositories.CalculationSnapshotRepository"/>) — ne participe JAMAIS
/// au calcul métier lui-même (valeur en douane, droits, taxes, coût de revient).
/// Volontairement IGNORE les données réglementaires externes (règles officielles DD/TVA/CS/TCS/PRCT/DAPS,
/// taux de change officiels publiés) : un changement de réglementation entre deux ouvertures de la même
/// importation n'est PAS considéré ici comme une modification de l'importation elle-même (l'utilisateur
/// reste libre de relancer un calcul explicitement s'il sait qu'une règle a changé) — seules les données
/// SAISIES par l'utilisateur sont prises en compte, pour rester un indicateur simple et prévisible.
/// </summary>
public static class CalculationInputHasher
{
    public static string ComputeHash(Company company, ImportOperation operation)
    {
        var sb = new StringBuilder();
        void Add(string label, object? value) => sb.Append(label).Append('=').Append(Fmt(value)).Append(';');

        Add("Company", company.Id);
        Add("ImportNumber", operation.ImportNumber);
        Add("ReferenceDate", operation.ReferenceDate);
        Add("SupplierName", operation.SupplierName);
        Add("PurchaseCountry", operation.PurchaseCountryIso2);
        Add("DefaultOriginCountry", operation.DefaultOriginCountryIso2);
        Add("ExportShippingCountry", operation.ExportShippingCountryIso2);
        Add("MainCurrency", operation.MainCurrencyCode);
        Add("AuthorizationCurrency", operation.AuthorizationCurrencyCode);
        Add("ManualExchangeRateOverride", operation.ManualExchangeRateOverride);
        Add("ManualAuthorizationCurrencyRateToDzd", operation.ManualAuthorizationCurrencyRateToDzd);
        Add("Incoterm", operation.Incoterm);
        Add("ArrivalPort", operation.ArrivalPortOrBorder);
        Add("TransportMode", operation.TransportMode);
        Add("UseDefaultRatesWhenRuleMissing", operation.UseDefaultRatesWhenRuleMissing);
        Add("DefaultDdRatePercent", operation.DefaultDdRatePercent);
        Add("DefaultCsRatePercent", operation.DefaultCsRatePercent);
        Add("DefaultPrctRatePercent", operation.DefaultPrctRatePercent);
        Add("DefaultTvaRatePercent", operation.DefaultTvaRatePercent);
        Add("DefaultTcsRatePercent", operation.DefaultTcsRatePercent);
        Add("DefaultRpsAmountDzd", operation.DefaultRpsAmountDzd);
        Add("ManualPrctRatePercent", operation.ManualPrctRatePercent);
        Add("UserConfirmedManualPrct", operation.UserConfirmedManualPrct);
        Add("ManualTcsRatePercent", operation.ManualTcsRatePercent);
        Add("UserConfirmedManualTcs", operation.UserConfirmedManualTcs);

        foreach (var line in operation.Lines.OrderBy(l => l.LineNumber))
        {
            Add("L.Number", line.LineNumber);
            Add("L.Reference", line.ProductReference);
            Add("L.Designation", line.Designation);
            Add("L.Quantity", line.Quantity);
            Add("L.Unit", line.MeasurementUnit);
            Add("L.UnitPrice", line.UnitPurchasePrice);
            Add("L.Currency", line.CurrencyCode);
            Add("L.HsCode", line.HsCodeConfirmed10);
            Add("L.Origin", line.OriginCountryIso2);
            Add("L.ExcelDutyRatePercent", line.ExcelDutyRatePercent);
            Add("L.UserConfirmedExcelDutyFallback", line.UserConfirmedExcelDutyFallback);
            Add("L.ManualVatRatePercent", line.ManualVatRatePercent);
            Add("L.UserConfirmedManualVatRate", line.UserConfirmedManualVatRate);
            Add("L.VatExemptionReasonFr", line.VatExemptionReasonFr);
            Add("L.GrossWeightKg", line.LineGrossWeightKg);
            Add("L.VolumeM3", line.LineVolumeM3);
            Add("L.SalePriceDzd", line.SalePriceDzd);
            foreach (var kvp in line.ManualFeeAllocationsDzd.OrderBy(k => k.Key))
                Add($"L.ManualAlloc[{kvp.Key}]", kvp.Value);
        }

        foreach (var fee in operation.Fees.OrderBy(f => f.Id))
        {
            Add($"F[{fee.Id}].Category", fee.FeeCategoryCode);
            Add($"F[{fee.Id}].Name", fee.FeeName);
            Add($"F[{fee.Id}].Amount", fee.Amount);
            Add($"F[{fee.Id}].Currency", fee.CurrencyCode);
            Add($"F[{fee.Id}].AllocationMethod", fee.AllocationMethod);
            Add($"F[{fee.Id}].IncludeInCustomsValue", fee.IncludeInCustomsValue);
            Add($"F[{fee.Id}].CustomsTreatment", fee.CustomsTreatment);
            Add($"F[{fee.Id}].IncludeInCostOfGoods", fee.IncludeInCostOfGoods);
        }

        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hashBytes);
    }

    private static string Fmt(object? value) => value switch
    {
        null => "null",
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("O", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null"
    };
}
