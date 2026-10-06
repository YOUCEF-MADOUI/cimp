using System;
using System.Collections.Generic;
using System.Linq;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.RegulatoryEngine;

namespace ImportCostAlgeria.CalculationEngine;

// ============================================================================
// 1. MODÈLES DE RÉSULTATS DU MOTEUR DE CALCUL (SECTIONS 25, 26, 27, 28, 40)
// ============================================================================

public sealed record CalculationAnomaly(
    AnomalySeverity Severity,
    string AnomalyCode,
    string MessageFr,
    int? LineNumber = null,
    string? ExpectedValue = null,
    string? ActualValue = null);

public sealed record AppliedTaxBreakdown(
    string TaxCode,
    string TaxNameFr,
    decimal TaxableBaseDzd,
    decimal RatePercent,
    decimal TaxAmountDzd,
    bool IsNonRecoverable,
    string RegulatoryRuleCode,
    string LegalArticleReference,
    string JoraReference,
    string RegulatoryVersionCode,
    DataOriginTag OriginTag);

public sealed record FeeAllocationTrace(
    Guid FeeId,
    string FeeName,
    FeeAllocationMethod MethodUsed,
    decimal ShareRatio,
    decimal AllocatedAmountDzd,
    bool IncludedInCustomsValue,
    bool IncludedInCostOfGoods);

/// <summary>
/// Résultat 1 (Section 25) : Résultat Douanier et Fiscal séparé du coût économique.
/// </summary>
public sealed record LineCustomsResult(
    decimal CustomsValueDzd,
    decimal CustomsDutyRatePercent,
    decimal CustomsDutyAmountDzd,
    DutyComparisonStatus ExcelVsRegulatoryComparison,
    string ComparisonLabelFr,
    IReadOnlyList<AppliedTaxBreakdown> AdditionalTaxes,
    decimal TotalAdditionalTaxesDzd,
    decimal VatTaxableBaseDzd,
    decimal VatRatePercent,
    decimal ImportVatAmountDzd,
    decimal TotalDutiesAndTaxesDzd,
    decimal CustomsValuePlusDutiesAndTaxesDzd,
    // Section 17 de l'audit : traçabilité de la source légale du droit de douane et de la TVA appliqués,
    // issue de la règle réglementaire effectivement résolue (jamais une référence générique codée en dur).
    // Null lorsque le taux appliqué n'a pas de règle réglementaire officielle correspondante (ex: taux
    // Excel confirmé par l'utilisateur en l'absence de règle officielle) — dans ce cas, l'appelant doit
    // afficher "INFORMATION NON DÉTERMINÉE" plutôt que d'inventer une citation.
    string? CustomsDutyLegalArticleReference = null,
    string? CustomsDutyJoraReference = null,
    string? CustomsDutyRegulatoryVersionCode = null,
    string? VatLegalArticleReference = null,
    string? VatJoraReference = null,
    string? VatRegulatoryVersionCode = null,
    // Revue du 2026-10-01 (point 5 & 6 — Droits et taxes par code SH / Affichage écran Importation) :
    // statut explicite (Applicable / Non applicable / Donnée manquante) des taxes additionnelles
    // "standard" (PRCT, TCS, DAPS) pour ce code SH/cette date, construit UNIQUEMENT à partir des règles
    // réglementaires réellement résolues (RegulatoryRuleEngine.BuildStandardTaxApplicabilityReport) —
    // jamais un taux inventé ni une absence silencieuse. Liste vide si le code SH n'a pas pu être résolu.
    IReadOnlyList<TaxApplicabilityStatus>? StandardTaxApplicability = null,
    // Revue du 2026-10-02 (Section 12 — TVA ne doit jamais rester silencieusement à 0) : origine du taux
    // de TVA réellement appliqué (VatRatePercent) — DonneeOfficielle (règle réglementaire), DonneeUtilisateur
    // (taux confirmé manuellement en l'absence de règle) ou CalculDuLogiciel (aucune règle ET aucune
    // confirmation : VatRatePercent reste à 0 mais ne doit JAMAIS être présenté comme définitif — la ligne
    // est de toute façon déjà bloquante, voir ImportCalculationSummary.Anomalies).
    DataOriginTag VatRateOriginTag = DataOriginTag.CalculDuLogiciel,
    // Revue du 2026-10-02 (correction urgente — ne plus bloquer le calcul faute de RegulatoryRule) :
    // origine du taux de Droit de Douane réellement appliqué (CustomsDutyRatePercent), même principe que
    // VatRateOriginTag ci-dessus — DonneeOfficielle (règle réglementaire), DonneeUtilisateur (taux Excel
    // confirmé manuellement), ValeurParDefautImportation (taux par défaut de l'importation, en l'absence
    // de règle ET de confirmation Excel) ou CalculDuLogiciel (aucune des trois : reste 0 %, jamais
    // présenté comme définitif — voir aussi ExcelVsRegulatoryComparison/ComparisonLabelFr).
    DataOriginTag CustomsDutyRateOriginTag = DataOriginTag.CalculDuLogiciel,
    // Revue du 2026-10-05 ("DD Excel prioritaire par défaut") : taux DD "IA" PROPOSÉ par le moteur
    // réglementaire à partir du Code SH confirmé et des règles publiées, qu'il soit ou non le taux
    // EFFECTIVEMENT appliqué (CustomsDutyRatePercent peut désormais provenir du DD Excel à la place).
    // Toujours renseigné dès qu'une règle réglementaire DD existe pour ce Code SH, pour permettre
    // l'affichage de la colonne "DD IA" et la comparaison avec le DD Excel — null si aucune règle
    // réglementaire DD n'a pu être résolue pour cet article (aucune proposition IA possible).
    decimal? AiProposedDutyRatePercent = null);

/// <summary>
/// Résultat 2 (Section 26) : Coût d'acquisition et Coût de revient économique réel.
/// </summary>
public sealed record LineEconomicCostResult(
    decimal PurchaseValueCurrency,
    decimal PurchaseValueDzd,
    decimal AllocatedCustomsIncludedFeesDzd,
    decimal AllocatedLocalAndPostCustomsFeesDzd,
    decimal TotalAllocatedFeesDzd,
    decimal CustomsDutyDzd,
    decimal NonRecoverableAdditionalTaxesDzd,
    decimal NonRecoverableImportVatDzd,
    decimal AcquisitionCostExVatDzd,
    decimal RealCostOfGoodsTotalDzd,
    decimal UnitCostOfGoodsDzd,
    decimal LandedCostCoefficient);

public sealed record LineFullCalculationResult(
    int LineNumber,
    string ProductReference,
    string Designation,
    decimal Quantity,
    string CurrencyCode,
    decimal AppliedExchangeRateToDzd,
    LineCustomsResult CustomsOutcome,
    LineEconomicCostResult EconomicOutcome,
    IReadOnlyList<FeeAllocationTrace> FeeAllocations,
    // Section 12 du plan multi-devises : conversion COMMERCIALE (jamais réglementaire) de cette ligne vers
    // la devise d'autorisation d'importation (ex: EUR -> USD). Null lorsque la devise de la ligne est déjà
    // la devise d'autorisation (aucune conversion à afficher, Section 15 : "ne pas afficher inutilement
    // une conversion"). Ne participe JAMAIS à CustomsOutcome / EconomicOutcome (Section 14 & 22).
    LineCommercialConversion? AuthorizationConversion = null);

/// <summary>
/// Conversion commerciale d'une ligne vers la devise d'autorisation d'importation (Section 12). Valeurs
/// purement informatives/traçabilité, jamais réinjectées dans le calcul douanier.
/// </summary>
public sealed record LineCommercialConversion(
    string AuthorizationCurrencyCode,
    decimal AuthorizationUnitPrice,
    decimal AuthorizationTotalAmount,
    decimal EffectiveRate,
    bool IsManualRate);

public sealed record ImportCalculationSummary(
    Guid ImportOperationId,
    string ImportNumber,
    DateOnly ReferenceDate,
    bool IsSimulation,
    // 11 résultats exigés par la Section 1 :
    decimal TotalPurchaseValueMainCurrency,
    decimal TotalPurchaseValueDzd,
    decimal TotalCustomsValueDzd,
    decimal TotalCustomsDutyDzd,
    decimal TotalAdditionalTaxesDzd,
    decimal TotalImportVatDzd,
    decimal TotalDutiesAndTaxesDzd,      // Section 25 : Total Droits et Taxes
    decimal TotalImportFeesDzd,          // Section 1.7 : Frais liés à l'importation
    decimal TotalAcquisitionCostExVatDzd,// Section 1.8 : Coût d'acquisition HT
    decimal TotalRealCostOfGoodsDzd,     // Section 1.9 & 1.11 : Coût de revient réel total de l'importation
    IReadOnlyList<LineFullCalculationResult> LineResults, // Section 1.10 : Coût unitaire par article
    IReadOnlyList<CalculationAnomaly> Anomalies,
    bool HasBlockingAnomalies,
    string MandatoryLegalDisclaimerFr,
    // Section 6, 11 & 13 du plan multi-devises : conversion COMMERCIALE globale de la facture (devise
    // originale -> devise de l'autorisation d'importation, ex: EUR -> USD). Null lorsque la devise
    // d'autorisation est identique à la devise de la facture (aucune conversion nécessaire). Strictement
    // distincte de la conversion réglementaire vers DZD déjà portée par les champs ci-dessus — jamais
    // mélangée avec eux (Section 14 & 22).
    CommercialAuthorizationConversion? CommercialAuthorizationConversion = null);

/// <summary>
/// Conversion commerciale globale de la facture vers la devise de l'autorisation d'importation
/// (Section 5, 6, 9 & 13 du plan multi-devises). Toujours accompagnée de la traçabilité du taux utilisé
/// (officiel ou manuel, source, date) — jamais une simple valeur numérique opaque (Section 7).
/// </summary>
public sealed record CommercialAuthorizationConversion(
    string OriginalCurrencyCode,
    decimal OriginalTotalAmount,
    string AuthorizationCurrencyCode,
    decimal AuthorizationTotalAmount,
    decimal EffectiveRate,
    bool IsManualRate,
    string? OfficialRateSourceName,
    DateOnly? OfficialRateValidFrom,
    string RateTypeLabelFr);

// ============================================================================
// 2. SOUS-MOTEURS SPÉCIALISÉS (SÉPARÉS DE TOUTE INTERFACE GRAPHIQUE)
// ============================================================================

public interface IExchangeRateProvider
{
    /// <summary>Taux réglementaire/douanier historique : <paramref name="currencyCode"/> -&gt; DZD.</summary>
    ExchangeRateRecord? GetRegulatoryRate(string currencyCode, DateOnly referenceDate);

    /// <summary>
    /// Taux historique entre deux devises quelconques (Section 6 du plan multi-devises), ex: EUR -&gt; USD.
    /// Implémentation par défaut : déléguée à <see cref="GetRegulatoryRate"/> lorsque la devise de cotation
    /// demandée est "DZD" (comportement strictement identique à avant l'ajout du multi-devises, donc sans
    /// aucune régression pour le code existant) ; retourne null si aucun taux n'est trouvé pour une autre
    /// devise de cotation et que l'implémentation ne la gère pas explicitement (voir
    /// ImportCostAlgeria.Database.Repositories.EfExchangeRateProvider pour l'implémentation réelle sur base).
    /// </summary>
    ExchangeRateRecord? GetRate(string fromCurrencyCode, string toCurrencyCode, DateOnly referenceDate) =>
        string.Equals(toCurrencyCode, "DZD", StringComparison.OrdinalIgnoreCase)
            ? GetRegulatoryRate(fromCurrencyCode, referenceDate)
            : null;
}

/// <summary>
/// Normalisation et comparaison d'un taux de change officiel/manuel, centralisées (Section 8 du plan
/// multi-devises : "ne duplique pas les formules de conversion dans plusieurs ViewModels/classes") afin
/// que <see cref="CurrencyCalculator"/> (conversion réglementaire vers DZD) et
/// <see cref="CurrencyConversionService"/> (conversion commerciale entre devises quelconques, ex: EUR -&gt;
/// USD) appliquent exactement la même règle de normalisation par quotité et le même seuil d'alerte
/// "⚠️ TAUX MANUEL", quelle que soit la devise de cotation cible.
/// </summary>
public static class ExchangeRateNormalization
{
    /// <summary>
    /// Seuil d'écart relatif (Section 9) au-delà duquel un taux manuel saisi par l'utilisateur, différent
    /// du taux officiel enregistré pour la même devise/période, déclenche un avertissement "⚠️ TAUX
    /// MANUEL". Volontairement non nul (et non une égalité stricte) afin de ne jamais déclencher une
    /// fausse alerte sur un simple écart d'arrondi négligeable entre deux représentations du même taux.
    /// Valeur actuelle : 0,5 % d'écart relatif — seuil conservateur documenté, ajustable ici en un seul
    /// endroit si l'application doit un jour exposer ce réglage à l'utilisateur.
    /// </summary>
    public const decimal ManualRateDifferenceWarningThresholdPercent = 0.5m;

    /// <summary>
    /// Ramène un taux officiel publié "pour <see cref="ExchangeRateRecord.QuotityUnit"/> unités" à un taux
    /// "pour 1 unité", c'est-à-dire la même convention que celle utilisée par tous les taux saisis
    /// manuellement dans l'application (Section 3 de l'audit : les deux chemins doivent représenter la
    /// même unité économique).
    /// </summary>
    public static decimal ToUnitRate(ExchangeRateRecord official) =>
        official.QuotityUnit == 0 ? 0m : official.RateToDzd / official.QuotityUnit;

    /// <summary>
    /// Résout le taux effectif à utiliser pour une conversion (devise source -&gt; devise cible quelconque),
    /// en appliquant systématiquement la normalisation par quotité ci-dessus et en générant les anomalies
    /// appropriées (taux manquant = BLOCAGE, taux manuel différent du taux officiel au-delà du seuil =
    /// AVERTISSEMENT "⚠️ TAUX MANUEL").
    /// </summary>
    public static (decimal EffectiveRate, ExchangeRateRecord? OfficialRecord, CalculationAnomaly? Anomaly) Resolve(
        ExchangeRateRecord? official,
        decimal? manualOverrideRate,
        string fromCurrencyCode,
        string toCurrencyCode,
        DateOnly referenceDate,
        string missingRateAnomalyCode,
        string manualDiffAnomalyCode,
        int? lineNumber = null)
    {
        if (manualOverrideRate.HasValue && manualOverrideRate.Value > 0m)
        {
            if (official != null)
            {
                decimal officialUnitRate = ToUnitRate(official);
                decimal relativeDiffPercent = officialUnitRate == 0m
                    ? 100m
                    : Math.Abs((manualOverrideRate.Value - officialUnitRate) / officialUnitRate) * 100m;

                if (relativeDiffPercent > ManualRateDifferenceWarningThresholdPercent)
                {
                    var warning = new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        manualDiffAnomalyCode,
                        $"⚠️ TAUX MANUEL : Le taux {fromCurrencyCode}→{toCurrencyCode} utilisé ({manualOverrideRate.Value:F4}) diffère de {relativeDiffPercent:F2} % du taux officiel enregistré ({officialUnitRate:F4} - {official.SourceName}).",
                        LineNumber: lineNumber,
                        ExpectedValue: officialUnitRate.ToString("F4"),
                        ActualValue: manualOverrideRate.Value.ToString("F4"));
                    return (manualOverrideRate.Value, official, warning);
                }
            }

            return (manualOverrideRate.Value, official, null);
        }

        if (official == null)
        {
            var blocking = new CalculationAnomaly(
                AnomalySeverity.Blocage,
                missingRateAnomalyCode,
                $"⚠️ Taux de change absent pour la conversion {fromCurrencyCode}→{toCurrencyCode} à la date de référence {referenceDate:dd/MM/yyyy}.",
                LineNumber: lineNumber);
            return (0m, null, blocking);
        }

        return (ToUnitRate(official), official, null);
    }
}

/// <summary>
/// Arrondi monétaire générique PAR DEVISE (correction du 2026-10-01, point 2 de l'audit) : distinct de
/// <see cref="CurrencyCalculator.RoundDzd"/>, qui reste nommément et sémantiquement réservé aux montants
/// RÉELLEMENT EXPRIMÉS EN DZD (calcul réglementaire/douanier). Un montant EUR ou USD (conversion
/// commerciale, montant original de facture, valeur de l'autorisation d'importation) ne doit jamais être
/// arrondi par une fonction dont le nom indique explicitement du DZD — même si, pour les devises
/// actuellement gérées (EUR/USD/DZD), le nombre de décimales appliqué est identique aujourd'hui, les deux
/// usages doivent rester conceptuellement et nommément séparés pour ne jamais être confondus, et pour que
/// l'ajout futur d'une devise à nombre de décimales différent (ex: une devise sans sous-unité) n'oblige
/// pas à toucher au calcul réglementaire. Centralisé ICI (moteur de calcul) : ne jamais dupliquer une
/// règle d'arrondi dans un ViewModel ou un générateur de rapport.
/// </summary>
public static class CurrencyRounding
{
    // Nombre de décimales standard par devise (convention ISO 4217). Par défaut 2 décimales, qui couvre
    // toutes les devises actuellement gérées par l'application (EUR, USD, DZD). Reste extensible à une
    // devise future sans sous-unité (ex: 0 décimale) sans impacter les autres devises.
    private static readonly IReadOnlyDictionary<string, int> DecimalPlacesByCurrency =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["DZD"] = 2,
            ["EUR"] = 2,
            ["USD"] = 2
        };

    /// <summary>Arrondit <paramref name="amount"/> selon le nombre de décimales standard de <paramref name="currencyCode"/>.</summary>
    public static decimal Round(decimal amount, string? currencyCode)
    {
        int decimals = !string.IsNullOrWhiteSpace(currencyCode) && DecimalPlacesByCurrency.TryGetValue(currencyCode, out int configuredDecimals)
            ? configuredDecimals
            : 2; // Valeur par défaut prudente pour toute devise non encore répertoriée explicitement.

        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// Convertisseur de devises versionné avec contrôle ALCES et alerte Taux Manuel (Sections 13 & 14).
/// Base juridique : Art. 16 decies du Code des Douanes (Loi n° 17-04 du 16 février 2017, JORA n° 11).
/// Usage EXCLUSIF : conversion réglementaire/douanière d'une devise vers DZD (alimente la valeur en
/// douane, les droits et les taxes). Pour toute conversion commerciale entre deux devises quelconques
/// (ex: EUR -&gt; USD, Section 6 du plan multi-devises), voir <see cref="CurrencyConversionService"/> —
/// les deux classes partagent la même logique de normalisation via <see cref="ExchangeRateNormalization"/>
/// pour ne jamais dupliquer une formule de conversion (Section 8).
/// </summary>
public sealed class CurrencyCalculator
{
    private readonly IExchangeRateProvider _rateProvider;

    public CurrencyCalculator(IExchangeRateProvider rateProvider)
    {
        _rateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
    }

    public (decimal EffectiveRateToDzd, ExchangeRateRecord? OfficialRecord, CalculationAnomaly? Anomaly) ResolveRate(
        string currencyCode,
        DateOnly referenceDate,
        decimal? manualOverrideRate)
    {
        if (string.Equals(currencyCode, "DZD", StringComparison.OrdinalIgnoreCase))
        {
            return (1.0m, null, null);
        }

        var official = _rateProvider.GetRegulatoryRate(currencyCode, referenceDate);

        return ExchangeRateNormalization.Resolve(
            official,
            manualOverrideRate,
            currencyCode,
            "DZD",
            referenceDate,
            missingRateAnomalyCode: "MISSING_EXCHANGE_RATE",
            manualDiffAnomalyCode: "MANUAL_EXCHANGE_RATE_DIFF");
    }

    /// <summary>
    /// Arrondi RÉSERVÉ aux montants réellement exprimés en DZD (valeur en douane, droits, taxes, coût de
    /// revient réel...). Pour un montant dans une autre devise (EUR, USD...), utiliser
    /// <see cref="CurrencyRounding.Round"/> à la place (point 2 de l'audit du 2026-10-01).
    /// </summary>
    public static decimal RoundDzd(decimal amount) => CurrencyRounding.Round(amount, "DZD");
}

/// <summary>
/// Service CENTRALISÉ de conversion entre devises quelconques (Section 8 du plan multi-devises) :
/// "Convert(amount, fromCurrency, toCurrency, rate)". Couvre notamment la conversion COMMERCIALE
/// (ex: EUR -&gt; USD pour la valeur de l'autorisation d'importation, Section 5 & 13) et reste
/// architecturalement extensible à toute autre paire de devises (USD -&gt; EUR, DZD -&gt; USD...).
/// Ne doit JAMAIS être utilisé pour la conversion réglementaire/douanière vers DZD qui reste la
/// responsabilité exclusive de <see cref="CurrencyCalculator"/> (Section 14 & 22 : "ne pas mélanger
/// conversion commerciale et conversion réglementaire").
/// </summary>
public sealed class CurrencyConversionService
{
    private readonly IExchangeRateProvider _rateProvider;

    public CurrencyConversionService(IExchangeRateProvider rateProvider)
    {
        _rateProvider = rateProvider ?? throw new ArgumentNullException(nameof(rateProvider));
    }

    /// <summary>
    /// Résout le taux effectif pour convertir <paramref name="fromCurrencyCode"/> vers <paramref name="toCurrencyCode"/>.
    /// </summary>
    /// <remarks>
    /// NOUVEAU SYSTÈME (correction 2026-10-02, Section 4 — "Corriger définitivement la conversion
    /// EUR/USD/DZD") : lorsque les DEUX devises disposent chacune d'un taux RÉGLEMENTAIRE publié par
    /// rapport au DZD (ex: 1 EUR = 150,7166 DA et 1 USD = 133,15 DA, toujours disponibles puisqu'exigés
    /// pour le calcul douanier), le taux croisé est désormais TOUJOURS calculé automatiquement par la
    /// formule : Taux_EURversUSD = (EUR -&gt; DZD) / (USD -&gt; DZD) — et plus jamais lu depuis un éventuel
    /// enregistrement "croisé" publié directement (EUR coté en USD) sur l'écran Taux de change. C'est
    /// cette confusion qui a permis la publication accidentelle d'une valeur d'ordre de grandeur DZD
    /// (ex: ~133) comme si elle était un taux EUR→USD, provoquant l'énoncé erroné "16,69 € = 2 228,45 $"
    /// alors que le résultat correct est environ 18,88 $. Un enregistrement "croisé" direct reste
    /// néanmoins RECONNU en repli (jamais supprimé de l'architecture), uniquement pour une devise qui
    /// n'aurait PAS de taux réglementaire DZD publié (extensibilité future), ou lorsqu'un taux MANUEL est
    /// explicitement saisi par l'utilisateur pour CETTE importation (qui garde toujours la priorité
    /// absolue).
    /// </remarks>
    public (decimal EffectiveRate, ExchangeRateRecord? OfficialRecord, CalculationAnomaly? Anomaly) ResolveCrossRate(
        string fromCurrencyCode,
        string toCurrencyCode,
        DateOnly referenceDate,
        decimal? manualOverrideRate)
    {
        if (string.Equals(fromCurrencyCode, toCurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            return (1.0m, null, null);
        }

        // Un taux manuel saisi par l'utilisateur garde toujours la priorité absolue et doit pouvoir être
        // comparé à un éventuel taux croisé déjà publié directement (comportement historique inchangé) —
        // on ne tente donc la dérivation automatique via le DZD que lorsqu'aucun taux manuel n'est fourni.
        if (!manualOverrideRate.HasValue || manualOverrideRate.Value <= 0m)
        {
            bool fromIsDzd = string.Equals(fromCurrencyCode, "DZD", StringComparison.OrdinalIgnoreCase);
            bool toIsDzd = string.Equals(toCurrencyCode, "DZD", StringComparison.OrdinalIgnoreCase);

            ExchangeRateRecord? fromToDzd = fromIsDzd ? null : _rateProvider.GetRegulatoryRate(fromCurrencyCode, referenceDate);
            ExchangeRateRecord? toToDzd = toIsDzd ? null : _rateProvider.GetRegulatoryRate(toCurrencyCode, referenceDate);

            if (fromIsDzd && toToDzd != null)
            {
                // DZD -> devise : inverse du taux réglementaire (Section 7 — Valeur en douane DZD affichée
                // en USD = Valeur_DZD / (1 USD = X DA), jamais Valeur_DZD x X).
                decimal toUnitRate = ExchangeRateNormalization.ToUnitRate(toToDzd);
                if (toUnitRate != 0m)
                    return (1m / toUnitRate, toToDzd, null);
            }
            else if (toIsDzd && fromToDzd != null)
            {
                return (ExchangeRateNormalization.ToUnitRate(fromToDzd), fromToDzd, null);
            }
            else if (!fromIsDzd && !toIsDzd && fromToDzd != null && toToDzd != null)
            {
                decimal fromUnitRate = ExchangeRateNormalization.ToUnitRate(fromToDzd);
                decimal toUnitRate = ExchangeRateNormalization.ToUnitRate(toToDzd);
                if (toUnitRate != 0m)
                    return (fromUnitRate / toUnitRate, fromToDzd, null);
            }
        }

        // Repli (historique, Section 6 du plan multi-devises) : taux "croisé" publié directement (devise
        // exotique sans taux DZD, ou comparaison d'un taux manuel à un taux croisé déjà publié).
        var official = _rateProvider.GetRate(fromCurrencyCode, toCurrencyCode, referenceDate);

        return ExchangeRateNormalization.Resolve(
            official,
            manualOverrideRate,
            fromCurrencyCode,
            toCurrencyCode,
            referenceDate,
            missingRateAnomalyCode: "MISSING_COMMERCIAL_EXCHANGE_RATE",
            manualDiffAnomalyCode: "MANUAL_COMMERCIAL_EXCHANGE_RATE_DIFF");
    }

    /// <summary>
    /// Convertit un montant d'une devise vers une autre en appliquant le taux résolu (officiel ou manuel).
    /// Ne modifie jamais la valeur/devise d'origine fournie par l'appelant (Section 4 : "ne jamais
    /// remplacer la devise originale de la facture par une conversion") — se contente de retourner le
    /// résultat converti à côté de l'original.
    /// </summary>
    public CurrencyConversionOutcome Convert(
        decimal amount,
        string fromCurrencyCode,
        string toCurrencyCode,
        DateOnly referenceDate,
        decimal? manualOverrideRate)
    {
        var (rate, official, anomaly) = ResolveCrossRate(fromCurrencyCode, toCurrencyCode, referenceDate, manualOverrideRate);
        // Correction (point 2 de l'audit) : le montant converti est exprimé en toCurrencyCode (ex: USD),
        // jamais en DZD — il doit donc être arrondi selon la devise CIBLE, pas via RoundDzd.
        decimal convertedAmount = CurrencyRounding.Round(amount * rate, toCurrencyCode);

        return new CurrencyConversionOutcome(
            OriginalAmount: amount,
            FromCurrencyCode: fromCurrencyCode,
            ToCurrencyCode: toCurrencyCode,
            EffectiveRate: rate,
            ConvertedAmount: convertedAmount,
            IsManualRate: manualOverrideRate.HasValue && manualOverrideRate.Value > 0m,
            OfficialRecord: official,
            Anomaly: anomaly);
    }
}

/// <summary>Résultat traçable d'une conversion commerciale entre deux devises (Section 7 & 8).</summary>
public sealed record CurrencyConversionOutcome(
    decimal OriginalAmount,
    string FromCurrencyCode,
    string ToCurrencyCode,
    decimal EffectiveRate,
    decimal ConvertedAmount,
    bool IsManualRate,
    ExchangeRateRecord? OfficialRecord,
    CalculationAnomaly? Anomaly);

/// <summary>
/// Moteur de répartition configurable des frais (Sections 11 & 12).
/// Supporte : Par valeur, Par quantité, Par poids, Par volume, Montant fixe, Pourcentage, Manuelle.
/// </summary>
public sealed class CostAllocationEngine
{
    public IReadOnlyDictionary<Guid, FeeAllocationTrace> AllocateFeeAcrossLines(
        ImportFee fee,
        decimal feeAmountDzd,
        IReadOnlyList<(ImportLine Line, decimal LinePurchaseDzd)> linesWithPurchaseDzd,
        List<CalculationAnomaly> anomalies)
    {
        var result = new Dictionary<Guid, FeeAllocationTrace>();
        if (linesWithPurchaseDzd.Count == 0 || feeAmountDzd == 0m)
        {
            return result;
        }

        decimal[] weights = new decimal[linesWithPurchaseDzd.Count];

        switch (fee.AllocationMethod)
        {
            case FeeAllocationMethod.ByValue:
            // Revue du 2026-10-02 (Section 3.4.D) : "Pourcentage" applique le même taux à CHAQUE ligne
            // proportionnellement à sa propre valeur d'achat — l'assiette de répartition est donc
            // identique à "Par valeur" (feeDzd ayant déjà été calculé comme Base_totale × taux/100 dans
            // ImportCalculationOrchestrator, cette pondération reproduit exactement, pour chaque ligne,
            // LinePurchaseDzd × taux/100).
            case FeeAllocationMethod.Percentage:
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                    weights[i] = linesWithPurchaseDzd[i].LinePurchaseDzd;
                break;

            case FeeAllocationMethod.ByQuantity:
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                    weights[i] = linesWithPurchaseDzd[i].Line.Quantity;
                break;

            case FeeAllocationMethod.ByWeight:
                if (linesWithPurchaseDzd.Any(x => !x.Line.LineGrossWeightKg.HasValue || x.Line.LineGrossWeightKg.Value <= 0m))
                {
                    // Revue du 2026-10-05 (Tâche #20, point 1 — "retirer le blocage") : l'absence du poids
                    // nécessaire à une répartition PAR POIDS ne doit plus jamais empêcher le calcul complet,
                    // la consultation des résultats ni la sauvegarde de l'importation. Sévérité abaissée de
                    // Blocage à Avertissement — AUCUN autre changement de comportement : aucun poids n'est
                    // inventé, l'anomalie n'est ni supprimée ni masquée, et le repli déjà existant est
                    // intégralement conservé (ce frais précis n'est simplement PAS réparti entre les
                    // articles tant que le poids manque — voir "return result" ci-dessous, inchangé — le
                    // reste du calcul se poursuit normalement pour toutes les autres lignes/frais).
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "MISSING_WEIGHT_FOR_WEIGHT_ALLOCATION",
                        $"⚠️ Le frais '{fee.FeeName}' est configuré pour une répartition PAR POIDS, mais le poids est absent sur une ou plusieurs lignes."));
                    return result;
                }
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                    weights[i] = linesWithPurchaseDzd[i].Line.LineGrossWeightKg!.Value;
                break;

            case FeeAllocationMethod.ByVolume:
                if (linesWithPurchaseDzd.Any(x => !x.Line.LineVolumeM3.HasValue || x.Line.LineVolumeM3.Value <= 0m))
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Blocage,
                        "MISSING_VOLUME_FOR_VOLUME_ALLOCATION",
                        $"⚠️ Le frais '{fee.FeeName}' est configuré pour une répartition PAR VOLUME, mais le volume est absent sur une ou plusieurs lignes."));
                    return result;
                }
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                    weights[i] = linesWithPurchaseDzd[i].Line.LineVolumeM3!.Value;
                break;

            case FeeAllocationMethod.FixedAmount:
                // Section 3.4.C : "Montant fixe" répartit le montant SAISI À L'IDENTIQUE (poids égaux)
                // entre toutes les lignes concernées — comportement déjà existant, conservé tel quel
                // (ex: RPS, frais bancaires forfaitaires non proportionnels à la valeur/quantité).
                for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
                    weights[i] = 1m;
                break;

            case FeeAllocationMethod.Manual:
                // Revue du 2026-10-02 (Section 3.4.E) : la méthode "Manuelle" n'est PAS une pondération
                // comme les autres — chaque ligne doit recevoir EXACTEMENT le montant que l'utilisateur lui
                // a affecté (ImportLine.ManualFeeAllocationsDzd), jamais une valeur recalculée/normalisée
                // pour "forcer" la somme à correspondre au frais total. Traitée dans une branche dédiée
                // ci-dessous (pas de répartition proportionnelle par poids).
                return AllocateManualFee(fee, feeAmountDzd, linesWithPurchaseDzd, anomalies);
        }

        decimal totalWeight = weights.Sum();
        if (totalWeight <= 0m)
        {
            anomalies.Add(new CalculationAnomaly(
                AnomalySeverity.Erreur,
                "INCONSISTENT_DATA",
                $"⚠️ Base de répartition nulle pour le frais '{fee.FeeName}' (méthode {fee.AllocationMethod})."));
            return result;
        }

        decimal runningAllocated = 0m;
        for (int i = 0; i < linesWithPurchaseDzd.Count; i++)
        {
            var line = linesWithPurchaseDzd[i].Line;
            decimal ratio = weights[i] / totalWeight;
            decimal allocated = (i == linesWithPurchaseDzd.Count - 1)
                ? CurrencyCalculator.RoundDzd(feeAmountDzd - runningAllocated)
                : CurrencyCalculator.RoundDzd(feeAmountDzd * ratio);

            runningAllocated += allocated;

            result[line.Id] = new FeeAllocationTrace(
                FeeId: fee.Id,
                FeeName: fee.FeeName,
                MethodUsed: fee.AllocationMethod,
                ShareRatio: Math.Round(ratio, 8, MidpointRounding.AwayFromZero),
                AllocatedAmountDzd: allocated,
                IncludedInCustomsValue: fee.IncludeInCustomsValue,
                IncludedInCostOfGoods: fee.IncludeInCostOfGoods);
        }

        return result;
    }

    /// <summary>
    /// Section 3.4.E (demande utilisateur) : répartition "Manuelle" — chaque ligne reçoit EXACTEMENT le
    /// montant affecté par l'utilisateur (<see cref="ImportLine.ManualFeeAllocationsDzd"/>), jamais une
    /// valeur recalculée/normalisée. Si la somme des montants affectés ne correspond pas EXACTEMENT au
    /// montant total du frais, une anomalie explicite est levée — SANS jamais corriger silencieusement la
    /// répartition de l'utilisateur.
    /// </summary>
    private static IReadOnlyDictionary<Guid, FeeAllocationTrace> AllocateManualFee(
        ImportFee fee,
        decimal feeAmountDzd,
        IReadOnlyList<(ImportLine Line, decimal LinePurchaseDzd)> linesWithPurchaseDzd,
        List<CalculationAnomaly> anomalies)
    {
        var result = new Dictionary<Guid, FeeAllocationTrace>();
        decimal totalManualAllocated = 0m;

        foreach (var (line, _) in linesWithPurchaseDzd)
        {
            decimal manualVal = line.ManualFeeAllocationsDzd.TryGetValue(fee.Id, out decimal v) ? v : 0m;
            totalManualAllocated += manualVal;

            result[line.Id] = new FeeAllocationTrace(
                FeeId: fee.Id,
                FeeName: fee.FeeName,
                MethodUsed: fee.AllocationMethod,
                ShareRatio: feeAmountDzd != 0m ? Math.Round(manualVal / feeAmountDzd, 8, MidpointRounding.AwayFromZero) : 0m,
                AllocatedAmountDzd: CurrencyCalculator.RoundDzd(manualVal),
                IncludedInCustomsValue: fee.IncludeInCustomsValue,
                IncludedInCostOfGoods: fee.IncludeInCostOfGoods);
        }

        decimal roundedTotalManualAllocated = CurrencyCalculator.RoundDzd(totalManualAllocated);
        decimal roundedFeeAmount = CurrencyCalculator.RoundDzd(feeAmountDzd);
        if (roundedTotalManualAllocated != roundedFeeAmount)
        {
            anomalies.Add(new CalculationAnomaly(
                AnomalySeverity.Erreur,
                "MANUAL_FEE_ALLOCATION_INCOMPLETE",
                $"⚠️ Le montant des frais n'est pas entièrement réparti. Frais '{fee.FeeName}' : total affecté manuellement {roundedTotalManualAllocated:N2} DZD pour un frais total de {roundedFeeAmount:N2} DZD."));
        }

        return result;
    }
}

/// <summary>
/// Calculateur de la Valeur en Douane selon les articles 16 bis, 16 ter et 16 octies du Code des Douanes Algérien.
/// Vérifie également la cohérence des frais requis selon l'Incoterm sélectionné (EXW, FOB, CFR en V1).
/// </summary>
public sealed class CustomsValueCalculator
{
    public void ValidateIncotermRequiredFees(ImportOperation operation, List<CalculationAnomaly> anomalies)
    {
        bool hasFreight = operation.Fees.Any(f =>
            string.Equals(f.FeeCategoryCode, "FRET_INTERNATIONAL", StringComparison.OrdinalIgnoreCase) && f.Amount > 0m);
        bool hasInsurance = operation.Fees.Any(f =>
            string.Equals(f.FeeCategoryCode, "ASSURANCE", StringComparison.OrdinalIgnoreCase) && f.Amount > 0m);
        bool hasExwExportFees = operation.Fees.Any(f =>
            (string.Equals(f.FeeCategoryCode, "TRANSPORT_INTERIEUR_EXPORT", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(f.FeeCategoryCode, "FRAIS_EXPORT", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(f.FeeCategoryCode, "MANUTENTION_EXPORT", StringComparison.OrdinalIgnoreCase)) && f.Amount > 0m);

        switch (operation.Incoterm)
        {
            case IncotermCode.EXW:
                if (!hasExwExportFees || !hasFreight)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Erreur,
                        "EXW_MISSING_REQUIRED_FEES",
                        "⚠️ Incoterm EXW mais frais nécessaires manquants (transport intérieur export / frais export / manutention chargement / fret international requis selon Art. 16 octies §1 e) du Code des Douanes)."));
                }
                if (!hasInsurance)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "EXW_MISSING_INSURANCE",
                        "⚠️ Incoterm EXW : Assurance transport international absente."));
                }
                break;

            case IncotermCode.FOB:
                if (!hasFreight)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Erreur,
                        "FOB_MISSING_FREIGHT",
                        "⚠️ Incoterm FOB mais fret international absent (requis pour déterminer la valeur en douane au lieu d'introduction en Algérie selon Art. 16 octies §1 e) CDA)."));
                }
                if (!hasInsurance)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "FOB_MISSING_INSURANCE",
                        "⚠️ Incoterm FOB mais assurance transport international absente."));
                }
                break;

            case IncotermCode.CFR:
                // Revue du 2026-10-02 (demande utilisateur, Section 6 — "CFR ne doit plus demander
                // d'assurance manquante") : selon les Incoterms 2020, le VENDEUR n'a PAS l'obligation de
                // souscrire une assurance pour l'acheteur en CFR (cette obligation n'existe qu'en CIF).
                // L'ancienne anomalie CFR_MISSING_INSURANCE a donc été SUPPRIMÉE — l'absence d'assurance en
                // CFR est un fonctionnement NORMAL, jamais une anomalie à signaler. Important : cela ne
                // signifie PAS que "l'assurance est incluse dans le fret" (ce serait une autre erreur) —
                // simplement qu'elle n'est pas exigée côté vendeur. Si l'utilisateur saisit malgré tout une
                // assurance comme frais, elle continue d'être traitée normalement selon ses propres cases
                // "Inclus valeur en douane"/"Inclus coût de revient" (aucun traitement spécial requis ici).
                // Le traitement CIF (où l'assurance reste une exigence documentée) n'existe pas encore en V1
                // (IncotermCode ne liste que EXW/FOB/CFR) — le jour où CIF sera ajouté, restaurer un contrôle
                // équivalent à EXW/FOB UNIQUEMENT pour ce nouveau cas, jamais pour CFR.

                // Revue du 2026-10-02 (cas de référence D10 réel, Section 6 — CRITIQUE : éviter le double
                // comptage du fret). En Incoterm CFR, le prix facturé (PTFN) inclut DÉJÀ le fret jusqu'au
                // point convenu : un frais "FRET_INTERNATIONAL" ajouté EN PLUS avec un traitement
                // "Addition_Art16Octies" additionnerait une seconde fois un fret déjà intégré au prix. Ne
                // jamais corriger silencieusement : on avertit explicitement l'utilisateur pour qu'il
                // confirme si ce fret est réellement un complément non compris dans le prix CFR, ou s'il
                // doit être requalifié en "Déjà inclus dans le prix facturé" (CustomsAdjustmentTreatment
                // .IncludedInInvoicePrice, IncludeInCustomsValue = false).
                foreach (var possibleDoubleCountedFreight in operation.Fees.Where(f =>
                    f.FeeCategoryCode.Contains("FRET", StringComparison.OrdinalIgnoreCase) &&
                    f.Amount > 0m &&
                    f.IncludeInCustomsValue &&
                    f.CustomsTreatment != CustomsAdjustmentTreatment.Deduction_Art16Ter_Octies3 &&
                    f.CustomsTreatment != CustomsAdjustmentTreatment.IncludedInInvoicePrice))
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "CFR_POSSIBLE_FREIGHT_DOUBLE_COUNT",
                        $"⚠️ Incoterm CFR : le frais '{possibleDoubleCountedFreight.FeeName}' est ajouté EN PLUS à la valeur en douane (traitement \"Addition\"). En CFR, le prix facturé inclut normalement déjà le fret jusqu'au point convenu — vérifiez qu'il ne s'agit pas d'un double comptage. Si ce fret est déjà compris dans le prix facturé, requalifiez ce frais en \"Déjà inclus dans le prix facturé\" (ne pas l'ajouter à la valeur en douane)."));
                }
                break;
        }
    }

    public decimal CalculateLineCustomsValueDzd(
        decimal linePurchaseValueDzd,
        IReadOnlyList<FeeAllocationTrace> lineFeeAllocations,
        IReadOnlyDictionary<Guid, ImportFee> feeDefinitionsById)
    {
        decimal additions = 0m;
        decimal deductions = 0m;

        foreach (var alloc in lineFeeAllocations)
        {
            if (!feeDefinitionsById.TryGetValue(alloc.FeeId, out var feeDef))
                continue;

            // Revue du 2026-10-02 (correction urgente — case "Inclure dans la valeur en douane" sans
            // effet réel) : IncludeInCustomsValue est désormais le SEUL interrupteur maître déterminant
            // si un frais a un quelconque effet sur la valeur en douane (demande utilisateur, Section 3.1 :
            // "Inclure dans la valeur en douane" et "Inclure dans le coût de revient" doivent fonctionner
            // indépendamment l'une de l'autre, chacune pilotée par sa propre case à cocher). Avant cette
            // correction, une addition exigeait EN PLUS que CustomsTreatment soit EXACTEMENT
            // Addition_Art16Octies : si un frais avait été créé avec un autre traitement par défaut (ex:
            // PostIntroductionExcluded, cas de la plupart des StandardTemplates) puis que l'utilisateur
            // cochait manuellement la case à l'écran "Frais" sans que l'écran n'expose de contrôle pour
            // changer CustomsTreatment, le montant restait silencieusement EXCLU de la valeur en douane —
            // alors qu'il apparaissait bien dans "Frais alloués" (coût de revient, piloté séparément par
            // IncludedInCostOfGoods). CustomsTreatment ne sert plus qu'à choisir le SENS de l'effet
            // (addition vs déduction) une fois IncludeInCustomsValue=true explicitement coché — il ne doit
            // plus jamais, à lui seul, empêcher une addition pourtant demandée par l'utilisateur, ni
            // provoquer une déduction alors que la case est décochée.
            if (!feeDef.IncludeInCustomsValue)
                continue;

            switch (feeDef.CustomsTreatment)
            {
                case CustomsAdjustmentTreatment.Deduction_Art16Ter_Octies3:
                    deductions += alloc.AllocatedAmountDzd;
                    break;

                case CustomsAdjustmentTreatment.IncludedInInvoicePrice:
                    // Garde-fou anti double comptage (Section 3.6/cas D10 réel, fret déjà inclus au prix
                    // CFR) : par définition, un frais marqué "déjà inclus dans le prix facturé" n'a AUCUN
                    // montant supplémentaire à ajouter à la valeur en douane, même si la case "Inclure
                    // dans valeur en douane" a été cochée par erreur — le montant est déjà dans
                    // linePurchaseValueDzd via le prix commercial de la ligne.
                    break;

                default: // Addition_Art16Octies et PostIntroductionExcluded : dès lors que l'utilisateur a
                         // explicitement coché "Inclure dans valeur en douane", le frais est ajouté.
                    additions += alloc.AllocatedAmountDzd;
                    break;
            }
        }

        return CurrencyCalculator.RoundDzd(linePurchaseValueDzd + additions - deductions);
    }
}

/// <summary>
/// Orchestrateur principal du moteur de calcul (Section 3, 22-28).
/// Strictement indépendant de toute interface graphique (UI).
/// </summary>
public sealed class ImportCalculationOrchestrator
{
    public const string OfficialLegalDisclaimer =
        "Les résultats sont calculés à partir des données saisies, des règles réglementaires enregistrées dans le système et des paramètres sélectionnés. Ils doivent être vérifiés au regard de la déclaration et des documents douaniers officiels applicables à l'opération.";

    private readonly CurrencyCalculator _currencyCalculator;
    private readonly CostAllocationEngine _allocationEngine;
    private readonly CustomsValueCalculator _customsValueCalculator;
    private readonly RegulatoryRuleEngine _regulatoryEngine;
    private readonly CurrencyConversionService _commercialConversionService;

    public ImportCalculationOrchestrator(
        CurrencyCalculator currencyCalculator,
        CostAllocationEngine allocationEngine,
        CustomsValueCalculator customsValueCalculator,
        RegulatoryRuleEngine regulatoryEngine,
        CurrencyConversionService commercialConversionService)
    {
        _currencyCalculator = currencyCalculator;
        _allocationEngine = allocationEngine;
        _customsValueCalculator = customsValueCalculator;
        _regulatoryEngine = regulatoryEngine;
        _commercialConversionService = commercialConversionService;
    }

    public ImportCalculationSummary ExecuteCalculation(Company company, ImportOperation operation)
    {
        var anomalies = new List<CalculationAnomaly>();

        // 1. Contrôle Incoterm & champs dynamiques (Sections 7, 8, 28)
        _customsValueCalculator.ValidateIncotermRequiredFees(operation, anomalies);

        // 2. Résolution du taux de change principal RÉGLEMENTAIRE (Sections 13 & 14) : devise facture -> DZD.
        // C'est la SEULE conversion qui alimente la valeur en douane / droits / taxes ci-dessous.
        var (mainRateToDzd, _, mainRateAnomaly) = _currencyCalculator.ResolveRate(
            operation.MainCurrencyCode,
            operation.ReferenceDate,
            operation.ManualExchangeRateOverride);

        if (mainRateAnomaly != null)
            anomalies.Add(mainRateAnomaly);

        // 2bis. Résolution du taux de change COMMERCIAL (correction 2026-10-02, demande utilisateur — "ne
        // JAMAIS saisir ni interpréter directement un taux EUR -> USD") : devise facture -> devise de
        // l'autorisation d'importation (ex: EUR -> USD). TOUJOURS DÉRIVÉ MATHÉMATIQUEMENT de deux taux
        // RÉGLEMENTAIRES par rapport au DZD (jamais un taux croisé saisi ou publié directement) :
        //   Taux_Facture→Autorisation = (Facture -> DZD) / (Autorisation -> DZD)
        // Chaque taux DZD est résolu EXACTEMENT comme le taux réglementaire principal ci-dessus (même
        // méthode CurrencyCalculator.ResolveRate, même possibilité de taux manuel PAR DEVISE via
        // ManualAuthorizationCurrencyRateToDzd si aucun taux officiel n'est encore publié) — jamais de
        // mélange avec la conversion douanière réelle (Section 22 : "ne pas mélanger"). N'alimente JAMAIS
        // CustomsOutcome / EconomicOutcome : reste une information séparée, affichée et exportée à part.
        bool needsAuthorizationConversion = !string.IsNullOrWhiteSpace(operation.AuthorizationCurrencyCode)
            && !string.Equals(operation.AuthorizationCurrencyCode, operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase);

        decimal authorizationRate = 1.0m;
        bool authorizationRateIsManual = false;
        ExchangeRateRecord? authorizationOfficialRate = null;

        if (needsAuthorizationConversion)
        {
            var (authCurrencyToDzd, authCurrencyOfficial, authCurrencyAnomaly) = _currencyCalculator.ResolveRate(
                operation.AuthorizationCurrencyCode,
                operation.ReferenceDate,
                operation.ManualAuthorizationCurrencyRateToDzd);

            bool hasManualAuthorizationRate =
                (operation.ManualExchangeRateOverride.HasValue && operation.ManualExchangeRateOverride.Value > 0m) ||
                (operation.ManualAuthorizationCurrencyRateToDzd.HasValue && operation.ManualAuthorizationCurrencyRateToDzd.Value > 0m);

            if (authCurrencyToDzd > 0m)
            {
                // Cas normal (et le SEUL utilisé dès que les deux devises disposent d'un taux DZD, officiel
                // ou manuel) : dérivation stricte via le DZD. NE JAMAIS inverser cette formule (division,
                // jamais une multiplication par le taux DZD de la devise d'autorisation) : voir
                // ImportCostAlgeria.Core.Domain.ImportOperation.ManualAuthorizationCurrencyRateToDzd, et les
                // tests de non-régression AuthorizationConversion_UserReportedBug_.../ExactUserExample_...
                authorizationRate = mainRateToDzd / authCurrencyToDzd;
                authorizationOfficialRate = authCurrencyOfficial;
                authorizationRateIsManual = hasManualAuthorizationRate;
            }
            else if (!hasManualAuthorizationRate)
            {
                // Repli (Section 6 du plan multi-devises — ne régresse PAS la correction ci-dessus) :
                // aucun taux réglementaire NI manuel vers le DZD n'existe pour la devise d'autorisation
                // (ex: USD n'a tout simplement aucun taux DZD publié pour cette opération). Dans ce cas, et
                // UNIQUEMENT dans ce cas, un enregistrement de taux croisé publié directement entre la
                // devise facture et la devise d'autorisation (ex: EUR -> USD) reste reconnu, via le MÊME
                // service et la MÊME règle que la conversion par ligne ci-dessous
                // (CurrencyConversionService.ResolveCrossRate, voir aussi son repli documenté). Dès qu'un
                // taux DZD existe pour les deux devises, cette branche n'est jamais utilisée (voir
                // ci-dessus) : aucune régression sur le cas normal.
                var (crossRate, crossOfficial, crossAnomaly) = _commercialConversionService.ResolveCrossRate(
                    operation.MainCurrencyCode, operation.AuthorizationCurrencyCode, operation.ReferenceDate, null);
                authorizationRate = crossRate;
                authorizationOfficialRate = crossOfficial;
                authorizationRateIsManual = false;
                // Le repli a sa propre anomalie (plus précise : "aucun taux DZD ET aucun taux croisé publié")
                // qui remplace celle, moins précise, du simple "taux DZD manquant" ci-dessus — et qui est
                // nulle si, comme ici, le repli a effectivement trouvé un taux exploitable.
                authCurrencyAnomaly = crossAnomaly;
            }
            else
            {
                // Taux manuel renseigné mais invalide (<= 0) : cas limite déjà couvert par le comportement
                // historique (taux non déterminé), conservé à l'identique.
                authorizationRate = 0m;
                authorizationOfficialRate = authCurrencyOfficial;
                authorizationRateIsManual = hasManualAuthorizationRate;
            }

            if (authCurrencyAnomaly != null)
            {
                // La conversion commerciale est informative : une absence de taux ne doit jamais bloquer le
                // calcul douanier réel (seul un taux réglementaire manquant pour la devise FACTURE le peut,
                // voir le bloc 2 ci-dessus).
                anomalies.Add(authCurrencyAnomaly.Severity == AnomalySeverity.Blocage
                    ? authCurrencyAnomaly with { Severity = AnomalySeverity.Avertissement }
                    : authCurrencyAnomaly);
            }
        }

        // 3. Calcul de la valeur d'achat par ligne (en devise et convertie en DZD)
        var linesWithPurchaseDzd = new List<(ImportLine Line, decimal LinePurchaseCurrency, decimal LinePurchaseDzd, decimal LineRate)>();
        foreach (var line in operation.Lines.OrderBy(l => l.LineNumber))
        {
            if (string.IsNullOrWhiteSpace(line.OriginCountryIso2) && string.IsNullOrWhiteSpace(operation.DefaultOriginCountryIso2))
            {
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Erreur,
                    "MISSING_ORIGIN",
                    $"⚠️ Origine absente pour l'article '{line.ProductReference}' (Ligne {line.LineNumber}).",
                    LineNumber: line.LineNumber));
            }

            if (line.AiHsDecision == AiProposalDecision.PendingUserValidation && !string.IsNullOrWhiteSpace(line.AiProposedHsCode10))
            {
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Blocage,
                    "UNCONFIRMED_AI_HS_CODE",
                    $"⚠️ Code SH proposé par IA ({line.AiProposedHsCode10}) non confirmé par l'utilisateur pour l'article '{line.ProductReference}'.",
                    LineNumber: line.LineNumber));
            }

            var (lineRate, _, lineRateAnomaly) = string.Equals(line.CurrencyCode, operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase)
                ? (mainRateToDzd, null, null)
                : _currencyCalculator.ResolveRate(line.CurrencyCode, operation.ReferenceDate, null);

            if (lineRateAnomaly != null)
                anomalies.Add(lineRateAnomaly with { LineNumber = line.LineNumber });

            // Correction (point 2 de l'audit) : purchaseCurrency est exprimé dans la devise D'ORIGINE de la
            // ligne (line.CurrencyCode, ex: EUR/USD), jamais en DZD à ce stade — seul purchaseDzd (après
            // application du taux réglementaire) est réellement un montant DZD.
            decimal purchaseCurrency = CurrencyRounding.Round(line.Quantity * line.UnitPurchasePrice, line.CurrencyCode);
            decimal purchaseDzd = CurrencyCalculator.RoundDzd(purchaseCurrency * lineRate);
            linesWithPurchaseDzd.Add((line, purchaseCurrency, purchaseDzd, lineRate));
        }

        // 4. Conversion et répartition de chaque frais selon sa propre méthode (Sections 9 & 11)
        var feeDefinitionsById = operation.Fees.ToDictionary(f => f.Id);
        var allocationsByLineId = operation.Lines.ToDictionary(l => l.Id, _ => new List<FeeAllocationTrace>());

        var purchasePairs = linesWithPurchaseDzd.Select(x => (x.Line, x.LinePurchaseDzd)).ToList();
        foreach (var fee in operation.Fees)
        {
            decimal feeDzd;

            if (fee.AllocationMethod == FeeAllocationMethod.Percentage)
            {
                // Revue du 2026-10-02 (correction fonctionnelle — méthode de répartition "Pourcentage",
                // Section 3.4.D de la demande utilisateur) : pour cette méthode, fee.Amount représente un
                // TAUX (%), jamais un montant en devise — AUCUNE conversion de change ne doit lui être
                // appliquée (CurrencyCode reste renseigné sur l'entité pour des raisons de modèle commun,
                // mais n'a ici aucune incidence). Base = valeur d'achat totale des lignes concernées par ce
                // frais (même assiette que "Par valeur") ; Frais = Base × Pourcentage / 100. Exemple exact
                // de la demande : Base = 10 000 DA, Pourcentage = 5 % -> Frais = 500 DA.
                decimal totalLinePurchaseDzd = purchasePairs.Sum(p => p.LinePurchaseDzd);
                feeDzd = CurrencyCalculator.RoundDzd(totalLinePurchaseDzd * (fee.Amount / 100m));
            }
            else
            {
                var (feeRate, _, feeRateAnomaly) = string.Equals(fee.CurrencyCode, operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase)
                    ? (mainRateToDzd, null, null)
                    : _currencyCalculator.ResolveRate(fee.CurrencyCode, operation.ReferenceDate, null);

                if (feeRateAnomaly != null)
                    anomalies.Add(feeRateAnomaly);

                feeDzd = CurrencyCalculator.RoundDzd(fee.Amount * feeRate);
            }

            var perLineAlloc = _allocationEngine.AllocateFeeAcrossLines(fee, feeDzd, purchasePairs, anomalies);
            foreach (var kvp in perLineAlloc)
            {
                allocationsByLineId[kvp.Key].Add(kvp.Value);
            }
        }

        // 5. Calcul ligne par ligne : Valeur en douane, Droits, Taxes, TVA, Coût douanier et Coût de revient (Sections 22-27)
        var lineResults = new List<LineFullCalculationResult>();

        foreach (var (line, purchaseCurrency, purchaseDzd, lineRate) in linesWithPurchaseDzd)
        {
            var lineAllocations = allocationsByLineId[line.Id];
            decimal customsValueDzd = _customsValueCalculator.CalculateLineCustomsValueDzd(
                purchaseDzd,
                lineAllocations,
                feeDefinitionsById);

            string? effectiveOrigin = line.OriginCountryIso2 ?? operation.DefaultOriginCountryIso2;
            var regOutcome = _regulatoryEngine.ResolveApplicableRules(new RegulatoryLookupQuery(
                HsCode10: line.HsCodeConfirmed10 ?? string.Empty,
                OriginCountryIso2: effectiveOrigin,
                OperationReferenceDate: operation.ReferenceDate,
                CustomsRegimeCode: operation.CustomsRegimeCode));

            // Revue du 2026-10-02 (CORRECTION URGENTE — ne plus bloquer le calcul faute de RegulatoryRule) :
            // l'absence de règle réglementaire officielle (code SH manquant ou règle introuvable) ne doit
            // plus JAMAIS être un BLOCAGE. Le moteur poursuit avec les taux par défaut de l'importation
            // (operation.UseDefaultRatesWhenRuleMissing, actif par défaut) et se contente d'un AVERTISSEMENT
            // traçable. Seules d'autres conditions (ex: code SH IA non confirmé) restent bloquantes.
            bool useDefaultRates = operation.UseDefaultRatesWhenRuleMissing;
            foreach (var warn in regOutcome.WarningsOrMissingInfo)
            {
                bool isMissingHsCode = string.IsNullOrWhiteSpace(line.HsCodeConfirmed10);
                string suffix = isMissingHsCode
                    ? (useDefaultRates
                        ? " Code SH manquant : les taxes dépendant du SH ne peuvent pas être confirmées réglementairement. Le calcul utilise malgré tout les taux par défaut de l'importation — statut CALCUL SIMPLIFIÉ / NON DÉFINITIF."
                        : " Code SH manquant — les taxes réglementaires dépendant du SH ne peuvent pas être confirmées.")
                    : (useDefaultRates
                        ? " Le calcul utilise les taux par défaut de l'importation. Le résultat doit être vérifié avant utilisation définitive."
                        : " Les taux par défaut de l'importation sont désactivés : les taxes concernées restent à 0 % (NON DÉTERMINÉ).");

                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    isMissingHsCode ? "MISSING_HS_CODE" : "REGULATORY_RULE_NOT_FOUND",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : {warn}{suffix}",
                    LineNumber: line.LineNumber));
            }

            // Comparaison Droit Excel vs Droit Réglementaire (Section 17) + repli sur le taux DD PAR
            // DÉFAUT de l'importation (Section 4 de la correction du 2026-10-02) : le DD reste résolu
            // ARTICLE PAR ARTICLE (jamais un taux global imposé à toute l'importation) — seule la valeur
            // de repli (operation.DefaultDdRatePercent) est commune à défaut de règle officielle/Excel.
            // Revue du 2026-10-05 ("DD Excel prioritaire par défaut") : le Droit de Douane fourni par
            // l'utilisateur dans son fichier Excel (ExcelDutyRatePercent) est désormais sa DONNÉE MÉTIER
            // DÉCLARÉE et devient la source PRIORITAIRE PAR DÉFAUT, SANS confirmation ligne par ligne (voir
            // ImportLine.UserConfirmedExcelDutyFallback, désormais obsolète et plus jamais lu ici). Le DD
            // "IA" (résolu automatiquement par le moteur réglementaire à partir du Code SH confirmé et des
            // règles publiées) reste systématiquement calculé et affiché à titre de PROPOSITION/COMPARAISON
            // — il ne redevient le taux effectivement appliqué que si l'utilisateur coche explicitement
            // "Forcer DD IA" (ImportLine.ForceAiDutyRate) sur cette ligne précise.
            //
            // Nouvel ordre de priorité (remplace l'ancienne règle "réglementaire toujours prioritaire") :
            //   1. DD IA forcé explicitement par l'utilisateur (ForceAiDutyRate=true ET une proposition IA existe) ;
            //   2. DD Excel fourni dans le fichier (prioritaire par défaut dès qu'il existe) ;
            //   3. DD IA proposé automatiquement (aucun DD Excel disponible) ;
            //   4. taux DD par défaut de l'importation (si operation.UseDefaultRatesWhenRuleMissing) ;
            //   5. 0 / NON DÉTERMINÉ.
            decimal? aiProposedDutyRatePercent = regOutcome.CustomsDutyRule?.RatePercent;
            bool aiRuleAvailable = regOutcome.CustomsDutyRule != null;
            bool forcedAiButUnavailable = line.ForceAiDutyRate && !aiRuleAvailable;

            decimal effectiveDutyRate;
            DutyComparisonStatus comparisonStatus;
            string comparisonLabel;
            DataOriginTag customsDutyRateOriginTag;
            // Conditionne la citation légale (CustomsDutyLegalArticleReference/JoraReference/VersionCode) :
            // ne doit JAMAIS citer la règle réglementaire comme fondement du taux appliqué lorsque ce n'est
            // pas réellement elle qui a été utilisée (ex : DD Excel prioritaire malgré une règle différente).
            bool effectiveRateComesFromAiRule;

            if (forcedAiButUnavailable)
            {
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    "AI_DUTY_RATE_FORCED_BUT_UNAVAILABLE",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : « Forcer DD IA » est coché mais aucune proposition DD IA (règle réglementaire) n'a pu être déterminée pour cet article — le Droit de Douane Excel (si disponible) ou le taux par défaut de l'importation est utilisé à la place.",
                    LineNumber: line.LineNumber));
            }

            if (line.ForceAiDutyRate && aiRuleAvailable)
            {
                effectiveDutyRate = aiProposedDutyRatePercent!.Value;
                customsDutyRateOriginTag = DataOriginTag.DonneeOfficielle;
                effectiveRateComesFromAiRule = true;
                comparisonStatus = DutyComparisonStatus.AiForcedByUserOverridingExcel;
                comparisonLabel = line.ExcelDutyRatePercent.HasValue
                    ? $"⚠️ DD IA ({effectiveDutyRate:F2} %) utilisé sur choix explicite de l'utilisateur (« Forcer DD IA »), au lieu du DD Excel ({line.ExcelDutyRatePercent.Value:F2} %)."
                    : $"DD IA ({effectiveDutyRate:F2} %) utilisé sur choix explicite de l'utilisateur (« Forcer DD IA »).";
            }
            else if (line.ExcelDutyRatePercent.HasValue)
            {
                effectiveDutyRate = line.ExcelDutyRatePercent.Value;
                customsDutyRateOriginTag = DataOriginTag.DonneeUtilisateur;
                effectiveRateComesFromAiRule = false;

                if (forcedAiButUnavailable)
                {
                    comparisonStatus = DutyComparisonStatus.AiForcedByUserButNoAiProposalAvailable;
                    comparisonLabel = $"⚠️ « Forcer DD IA » coché mais aucune proposition DD IA n'est disponible pour cet article : le DD Excel ({effectiveDutyRate:F2} %) reste utilisé.";
                }
                else if (!aiRuleAvailable)
                {
                    comparisonStatus = DutyComparisonStatus.ExcelPriorityNoAiProposalAvailable;
                    comparisonLabel = $"DD Excel ({effectiveDutyRate:F2} %) utilisé — aucune proposition DD IA disponible pour comparaison sur cet article.";
                }
                else if (line.ExcelDutyRatePercent.Value == aiProposedDutyRatePercent!.Value)
                {
                    comparisonStatus = DutyComparisonStatus.ExcelPriorityMatchesAi;
                    comparisonLabel = $"✓ DD Excel ({effectiveDutyRate:F2} %) correspond au DD IA proposé.";
                }
                else
                {
                    comparisonStatus = DutyComparisonStatus.ExcelPriorityDiffersFromAi;
                    comparisonLabel = $"⚠️ Différence : DD IA {aiProposedDutyRatePercent.Value:F2} % / DD Excel {effectiveDutyRate:F2} % — DD Excel utilisé (priorité par défaut). Cochez « Forcer DD IA » pour utiliser {aiProposedDutyRatePercent.Value:F2} % à la place.";
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "EXCEL_VS_AI_DUTY_DIFF",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Droit de Douane Excel ({effectiveDutyRate:F2} %) différent de la proposition DD IA ({aiProposedDutyRatePercent.Value:F2} %). Le DD Excel est utilisé par défaut (donnée métier déclarée par l'utilisateur) — cochez « Forcer DD IA » pour utiliser la proposition IA à la place.",
                        LineNumber: line.LineNumber,
                        ExpectedValue: $"{aiProposedDutyRatePercent.Value:F2}%",
                        ActualValue: $"{effectiveDutyRate:F2}%"));
                }

                // Revue du 2026-10-05 (Bug 2 — vérification de la convention de pourcentage, "NE PAS
                // DEVINER") : CIMP n'effectue AUCUNE normalisation automatique d'une éventuelle fraction
                // Excel (ex : une cellule au format Pourcentage dont la valeur interne est 0.05 pour un
                // affichage "5 %") — voir ExcelImporterService. Un taux Excel strictement compris entre 0 et
                // 1 point de pourcentage est donc soit un taux réellement infime (rare pour un Droit de
                // Douane), soit le signe d'une fraction Excel non convertie par l'utilisateur avant l'import
                // (0.05 saisi/exporté au lieu de 5). Dans le doute, un AVERTISSEMENT explicite et traçable
                // est levé ici plutôt qu'une correction silencieuse potentiellement erronée — la valeur
                // saisie reste néanmoins utilisée TELLE QUELLE pour le calcul (jamais multipliée par 100).
                if (effectiveDutyRate > 0m && effectiveDutyRate < 1m)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "EXCEL_DUTY_RATE_SUSPICIOUSLY_LOW",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : le Droit de Douane Excel utilisé ({effectiveDutyRate:F2} %) est anormalement faible. Vérifiez que la colonne Excel contient bien un pourcentage en points (ex : « 5 » ou « 5% » pour 5 %) et non une fraction (« 0.05 »), qui serait interprétée ici comme 0,05 % et non 5 %.",
                        LineNumber: line.LineNumber,
                        ActualValue: $"{effectiveDutyRate:F2}%"));
                }
            }
            else if (aiRuleAvailable)
            {
                effectiveDutyRate = aiProposedDutyRatePercent!.Value;
                customsDutyRateOriginTag = DataOriginTag.DonneeOfficielle;
                effectiveRateComesFromAiRule = true;
                comparisonStatus = DutyComparisonStatus.AiProposedRateUsedNoExcelAvailable;
                comparisonLabel = $"DD IA ({effectiveDutyRate:F2} %) utilisé — aucun DD Excel fourni pour cet article.";
            }
            else if (useDefaultRates)
            {
                effectiveDutyRate = operation.DefaultDdRatePercent;
                comparisonStatus = DutyComparisonStatus.DefaultImportRateUsed;
                comparisonLabel = $"⚠️ VALEUR PAR DÉFAUT DE L'IMPORTATION : DD = {effectiveDutyRate:F2} % (ni DD Excel, ni proposition IA disponibles pour cet article — à vérifier)";
                customsDutyRateOriginTag = DataOriginTag.ValeurParDefautImportation;
                effectiveRateComesFromAiRule = false;
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    "DD_DEFAULT_RATE_USED",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Droit de Douane non déterminé (ni DD Excel, ni proposition IA) — taux par défaut de l'importation appliqué ({effectiveDutyRate:F2} %). Statut : VALEUR PAR DÉFAUT / NON VÉRIFIÉE.",
                    LineNumber: line.LineNumber));
            }
            else
            {
                effectiveDutyRate = 0m;
                comparisonStatus = DutyComparisonStatus.RegulatoryNotFoundPendingConfirmation;
                comparisonLabel = "INFORMATION NON DÉTERMINÉE (Confirmation utilisateur requise)";
                customsDutyRateOriginTag = DataOriginTag.CalculDuLogiciel;
                effectiveRateComesFromAiRule = false;
            }

            // 4. Droit de douane (DD) = Valeur en douane × Taux DD (résolu article par article, jamais globalement)
            decimal customsDutyDzd = CurrencyCalculator.RoundDzd(customsValueDzd * (effectiveDutyRate / 100m));

            // 5. Taxes additionnelles AVANT TVA. Revue du 2026-10-02 (CORRECTION URGENTE) : CS, PRCT et
            // TCS sont désormais résolues par un mécanisme UNIFIÉ et systématique pour CHAQUE article :
            //   1) règle réglementaire officielle (si trouvée)  -> statut RÉGLEMENTAIRE
            //   2) règle officielle déclarant explicitement "non applicable" -> statut NON APPLICABLE
            //   3) confirmation manuelle ponctuelle pour cette importation (PRCT/TCS existants) -> MANUEL
            //   4) taux PAR DÉFAUT de l'importation (si operation.UseDefaultRatesWhenRuleMissing) -> DEFAULT_IMPORT
            //   5) sinon -> NON DÉTERMINÉ (0, avertissement, jamais un blocage).
            // Les AUTRES taxes additionnelles éventuelles (DAPS, TIC, RDAE...) conservent le comportement
            // générique existant : appliquées UNIQUEMENT si une règle officielle existe — AUCUN taux par
            // défaut n'est jamais inventé pour DAPS (Section 9 de la correction : "pas de DAPS arbitraire").
            RegulatoryRule? FindOfficialTaxRule(string taxCode) => regOutcome.AdditionalTaxRules
                .FirstOrDefault(r => string.Equals(r.TaxCode, taxCode, StringComparison.OrdinalIgnoreCase));
            bool IsExplicitlyNonApplicable(string taxCode) => regOutcome.NonApplicableTaxRules
                .Any(r => string.Equals(r.TaxCode, taxCode, StringComparison.OrdinalIgnoreCase));
            decimal GenericTaxBase(RegulatoryRule rule) => rule.CalculationBase == TaxableBaseType.CustomsValueDzd
                ? customsValueDzd
                : customsValueDzd + customsDutyDzd;

            var genericOtherTaxRules = regOutcome.AdditionalTaxRules
                .Where(r => !string.Equals(r.TaxCode, "CS", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(r.TaxCode, "PRCT", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(r.TaxCode, "TCS", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var beforeVatTaxRules = genericOtherTaxRules
                .Where(r => r.CalculationBase != TaxableBaseType.CustomsValuePlusPriorTaxesPlusVatDzd)
                .ToList();
            var afterVatTaxRules = genericOtherTaxRules
                .Where(r => r.CalculationBase == TaxableBaseType.CustomsValuePlusPriorTaxesPlusVatDzd)
                .ToList();

            var additionalTaxBreakdowns = new List<AppliedTaxBreakdown>();
            foreach (var taxRule in beforeVatTaxRules)
            {
                decimal taxBase = GenericTaxBase(taxRule);
                decimal taxAmount = CurrencyCalculator.RoundDzd(taxBase * (taxRule.RatePercent / 100m));

                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: taxRule.TaxCode,
                    TaxNameFr: taxRule.TaxNameFr,
                    TaxableBaseDzd: taxBase,
                    RatePercent: taxRule.RatePercent,
                    TaxAmountDzd: taxAmount,
                    IsNonRecoverable: true,
                    RegulatoryRuleCode: taxRule.Code,
                    LegalArticleReference: taxRule.LegalSource.ArticleReference,
                    JoraReference: taxRule.LegalSource.JoraReference,
                    RegulatoryVersionCode: taxRule.RegulatoryVersionCode,
                    OriginTag: DataOriginTag.DonneeOfficielle));
            }

            // Revue du 2026-10-01 (point 5 & 6) : statut explicite des taxes additionnelles "standard"
            // (CS, PRCT, TCS, DAPS) pour affichage sur l'écran Importation — construit uniquement à partir
            // du résultat déjà résolu ci-dessus (regOutcome), sans recalcul ni invention de taux.
            var standardTaxApplicability = RegulatoryRuleEngine.BuildStandardTaxApplicabilityReport(
                regOutcome, RegulatoryRuleEngine.StandardAdditionalTaxCodes);

            // ---- CS (Contribution de Solidarité) : assiette Valeur en douane par défaut (Section 5).
            // Tâche #21, point 5 : cascade de priorité désormais — 1) règle réglementaire officielle,
            // 2) CS explicitement non applicable, 3) taux CS confirmé manuellement pour cette importation
            // (UserConfirmedManualCs / ManualCsRatePercent, écran V1), 4) taux CS par défaut de
            // l'importation (uniquement si le SH est totalement inconnu du référentiel), 5) NON DÉTERMINÉ. ----
            var csRule = FindOfficialTaxRule("CS");
            if (csRule != null)
            {
                decimal csBase = GenericTaxBase(csRule);
                decimal csAmount = CurrencyCalculator.RoundDzd(csBase * (csRule.RatePercent / 100m));
                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: "CS", TaxNameFr: csRule.TaxNameFr, TaxableBaseDzd: csBase, RatePercent: csRule.RatePercent,
                    TaxAmountDzd: csAmount, IsNonRecoverable: true, RegulatoryRuleCode: csRule.Code,
                    LegalArticleReference: csRule.LegalSource.ArticleReference, JoraReference: csRule.LegalSource.JoraReference,
                    RegulatoryVersionCode: csRule.RegulatoryVersionCode, OriginTag: DataOriginTag.DonneeOfficielle));
            }
            else if (IsExplicitlyNonApplicable("CS"))
            {
                anomalies.Add(new CalculationAnomaly(AnomalySeverity.Info, "CS_NOT_APPLICABLE",
                    $"ℹ️ Ligne {line.LineNumber} ({line.ProductReference}) : Contribution de Solidarité (CS) explicitement NON APPLICABLE selon une règle réglementaire officielle.",
                    LineNumber: line.LineNumber));
            }
            // Tâche #21, point 5 ("Retirer la TCS de l'écran V1, exposer la CS comme taxe de solidarité
            // utilisateur") : confirmation manuelle d'un taux CS pour TOUTE l'importation (jamais par
            // article), UNE SEULE fois par import — priorité juste après la règle officielle et le statut
            // "explicitement non applicable", et AVANT le taux par défaut de l'importation. Alimente la
            // MÊME taxe CS (TaxCode "CS") que les branches ci-dessus/ci-dessous : jamais une seconde taxe
            // concurrente, jamais un second champ de taux CS.
            else if (operation.UserConfirmedManualCs && operation.ManualCsRatePercent.HasValue)
            {
                decimal manualCsBase = customsValueDzd;
                decimal manualCsAmount = CurrencyCalculator.RoundDzd(manualCsBase * (operation.ManualCsRatePercent.Value / 100m));
                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: "CS",
                    TaxNameFr: "Contribution de Solidarité (CS)",
                    TaxableBaseDzd: manualCsBase,
                    RatePercent: operation.ManualCsRatePercent.Value,
                    TaxAmountDzd: manualCsAmount,
                    IsNonRecoverable: true,
                    RegulatoryRuleCode: "MANUEL",
                    LegalArticleReference: "Taux saisi manuellement par l'utilisateur (aucune règle officielle publiée)",
                    JoraReference: "N/A",
                    RegulatoryVersionCode: "MANUEL",
                    OriginTag: DataOriginTag.DonneeUtilisateur));
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    "CS_MANUAL_RATE_USED",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Taux CS ({operation.ManualCsRatePercent.Value:F2} %) saisi manuellement pour cette importation, en l'absence de règle officielle.",
                    LineNumber: line.LineNumber));
            }
            // Correction du 2026-10-02 (régression Section H) : le taux PAR DÉFAUT de la CS ne doit être
            // invoqué QUE lorsque le code SH est totalement inconnu du référentiel (DD ET TVA tous deux
            // introuvables, regOutcome.IsDetermined == false, cf. test "NoRegulatoryRuleAnywhere..."). Si le
            // code SH est officiellement connu (DD et TVA publiés pour ce SH) mais qu'aucune règle CS
            // spécifique n'a été publiée, cela signifie que la CS NE S'APPLIQUE PAS à ce SH — il ne faut
            // JAMAIS inventer 3 % dans ce cas (sinon double emploi avec les totaux déjà réglementairement
            // déterminés, cf. Section H : Total Taxes Additionnelles attendu = 256 008,75, DAPS seule).
            else if (useDefaultRates && !regOutcome.IsDetermined)
            {
                decimal csBase = customsValueDzd;
                decimal csAmount = CurrencyCalculator.RoundDzd(csBase * (operation.DefaultCsRatePercent / 100m));
                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: "CS", TaxNameFr: "Contribution de Solidarité (CS)", TaxableBaseDzd: csBase,
                    RatePercent: operation.DefaultCsRatePercent, TaxAmountDzd: csAmount, IsNonRecoverable: true,
                    RegulatoryRuleCode: "DEFAULT_IMPORT",
                    LegalArticleReference: "Taux par défaut de l'importation (aucune règle officielle publiée)",
                    JoraReference: "N/A", RegulatoryVersionCode: "DEFAULT_IMPORT",
                    OriginTag: DataOriginTag.ValeurParDefautImportation));
                anomalies.Add(new CalculationAnomaly(AnomalySeverity.Avertissement, "CS_DEFAULT_RATE_USED",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Contribution de Solidarité (CS) non déterminée réglementairement — taux par défaut de l'importation appliqué ({operation.DefaultCsRatePercent:F2} %). Statut : VALEUR PAR DÉFAUT / NON VÉRIFIÉE.",
                    LineNumber: line.LineNumber));
            }
            else
            {
                anomalies.Add(new CalculationAnomaly(AnomalySeverity.Avertissement, "CS_RATE_NOT_DETERMINED",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Donnée réglementaire manquante pour la Contribution de Solidarité (CS) — statut NON DÉTERMINÉ (taux par défaut désactivé pour cette importation).",
                    LineNumber: line.LineNumber));
            }

            // ---- TCS (Taxe de Contribution de Solidarité) : règle officielle > confirmation manuelle
            // ponctuelle (existant) > taux par défaut de l'importation. Assiette Valeur en douane,
            // explicitement représentée, JAMAIS mélangée avec la DAPS (Section 8/9). ----
            var tcsRule = FindOfficialTaxRule("TCS");
            if (tcsRule != null)
            {
                decimal tcsBase = GenericTaxBase(tcsRule);
                decimal tcsAmount = CurrencyCalculator.RoundDzd(tcsBase * (tcsRule.RatePercent / 100m));
                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: "TCS", TaxNameFr: tcsRule.TaxNameFr, TaxableBaseDzd: tcsBase, RatePercent: tcsRule.RatePercent,
                    TaxAmountDzd: tcsAmount, IsNonRecoverable: true, RegulatoryRuleCode: tcsRule.Code,
                    LegalArticleReference: tcsRule.LegalSource.ArticleReference, JoraReference: tcsRule.LegalSource.JoraReference,
                    RegulatoryVersionCode: tcsRule.RegulatoryVersionCode, OriginTag: DataOriginTag.DonneeOfficielle));
            }
            else if (IsExplicitlyNonApplicable("TCS"))
            {
                anomalies.Add(new CalculationAnomaly(AnomalySeverity.Info, "TCS_NOT_APPLICABLE",
                    $"ℹ️ Ligne {line.LineNumber} ({line.ProductReference}) : Taxe de Contribution de Solidarité (TCS) explicitement NON APPLICABLE selon une règle réglementaire officielle.",
                    LineNumber: line.LineNumber));
            }
            else if (operation.UserConfirmedManualTcs && operation.ManualTcsRatePercent.HasValue)
            {
                decimal manualTcsBase = customsValueDzd;
                decimal manualTcsAmount = CurrencyCalculator.RoundDzd(manualTcsBase * (operation.ManualTcsRatePercent.Value / 100m));
                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: "TCS",
                    TaxNameFr: "Taxe de Contribution de Solidarité (TCS)",
                    TaxableBaseDzd: manualTcsBase,
                    RatePercent: operation.ManualTcsRatePercent.Value,
                    TaxAmountDzd: manualTcsAmount,
                    IsNonRecoverable: true,
                    RegulatoryRuleCode: "MANUEL",
                    LegalArticleReference: "Taux saisi manuellement par l'utilisateur (aucune règle officielle publiée)",
                    JoraReference: "N/A",
                    RegulatoryVersionCode: "MANUEL",
                    OriginTag: DataOriginTag.DonneeUtilisateur));
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    "TCS_MANUAL_RATE_USED",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Taux TCS ({operation.ManualTcsRatePercent.Value:F2} %) saisi manuellement pour cette importation, en l'absence de règle officielle.",
                    LineNumber: line.LineNumber));
            }
            // Correction du 2026-10-02 (régression Section H) : même principe que pour la CS ci-dessus —
            // le taux PAR DÉFAUT de la TCS n'est invoqué que si le SH est totalement inconnu du référentiel
            // (regOutcome.IsDetermined == false). Un SH officiellement connu (DD+TVA publiés) sans règle TCS
            // spécifique signifie que la TCS ne s'applique pas à ce SH, jamais une valeur inventée.
            else if (useDefaultRates && !regOutcome.IsDetermined)
            {
                decimal tcsBase = customsValueDzd;
                decimal tcsAmount = CurrencyCalculator.RoundDzd(tcsBase * (operation.DefaultTcsRatePercent / 100m));
                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: "TCS", TaxNameFr: "Taxe de Contribution de Solidarité (TCS)", TaxableBaseDzd: tcsBase,
                    RatePercent: operation.DefaultTcsRatePercent, TaxAmountDzd: tcsAmount, IsNonRecoverable: true,
                    RegulatoryRuleCode: "DEFAULT_IMPORT",
                    LegalArticleReference: "Taux par défaut de l'importation (aucune règle officielle publiée)",
                    JoraReference: "N/A", RegulatoryVersionCode: "DEFAULT_IMPORT",
                    OriginTag: DataOriginTag.ValeurParDefautImportation));
                if (operation.DefaultTcsRatePercent != 0m)
                {
                    anomalies.Add(new CalculationAnomaly(AnomalySeverity.Avertissement, "TCS_DEFAULT_RATE_USED",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Taxe de Contribution de Solidarité (TCS) non déterminée réglementairement — taux par défaut de l'importation appliqué ({operation.DefaultTcsRatePercent:F2} %). Statut : VALEUR PAR DÉFAUT / NON VÉRIFIÉE.",
                        LineNumber: line.LineNumber));
                }
            }
            else
            {
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    "TCS_RATE_NOT_DETERMINED",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Donnée réglementaire manquante pour la Taxe de Contribution de Solidarité (TCS) — confirmez un taux pour cette importation (écran Importation) ou laissez non déterminé.",
                    LineNumber: line.LineNumber));
            }

            decimal totalBeforeVatAdditionalTaxesDzd = additionalTaxBreakdowns.Sum(t => t.TaxAmountDzd);

            // 6. TVA à l'importation (Art. 19 CTCA : Assiette = Valeur en douane + Droits de douane + Taxes hors TVA)
            decimal vatTaxableBaseDzd = CurrencyCalculator.RoundDzd(customsValueDzd + customsDutyDzd + totalBeforeVatAdditionalTaxesDzd);
            decimal appliedVatRate;
            DataOriginTag vatRateOriginTag;
            if (regOutcome.VatRule != null)
            {
                appliedVatRate = regOutcome.VatRule.RatePercent;
                vatRateOriginTag = DataOriginTag.DonneeOfficielle;
            }
            else if (line.UserConfirmedManualVatRate && line.ManualVatRatePercent.HasValue)
            {
                // Revue du 2026-10-02 (Section 12) : taux de TVA confirmé manuellement en l'absence de règle
                // officielle — jamais un 0 % silencieux : tracé comme donnée utilisateur et toujours accompagné
                // d'une anomalie explicite (motif d'exonération exigé si le taux confirmé est 0 %).
                appliedVatRate = line.ManualVatRatePercent.Value;
                vatRateOriginTag = DataOriginTag.DonneeUtilisateur;

                if (appliedVatRate == 0m && string.IsNullOrWhiteSpace(line.VatExemptionReasonFr))
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "VAT_EXEMPTION_REASON_MISSING",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Exonération de TVA (0 %) confirmée sans motif précisé — renseignez le motif d'exonération.",
                        LineNumber: line.LineNumber));
                }
                else if (appliedVatRate == 0m)
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Info,
                        "VAT_MANUAL_EXEMPTION_CONFIRMED",
                        $"ℹ️ Ligne {line.LineNumber} ({line.ProductReference}) : Exonération de TVA confirmée manuellement — motif : {line.VatExemptionReasonFr}.",
                        LineNumber: line.LineNumber));
                }
                else
                {
                    anomalies.Add(new CalculationAnomaly(
                        AnomalySeverity.Avertissement,
                        "MANUAL_VAT_RATE_USED",
                        $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Taux de TVA ({appliedVatRate:F2} %) saisi manuellement, en l'absence de règle officielle.",
                        LineNumber: line.LineNumber));
                }
            }
            else if (useDefaultRates)
            {
                // Revue du 2026-10-02 (CORRECTION URGENTE, Section 6) : aucune règle officielle ni
                // confirmation manuelle — la TVA ne reste JAMAIS silencieusement à 0 % : le taux PAR
                // DÉFAUT de l'importation est appliqué (statut VALEUR PAR DÉFAUT, jamais présenté comme
                // définitif), avec un avertissement systématique.
                appliedVatRate = operation.DefaultTvaRatePercent;
                vatRateOriginTag = DataOriginTag.ValeurParDefautImportation;
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    "TVA_DEFAULT_RATE_USED",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : TVA non déterminée réglementairement — taux par défaut de l'importation appliqué ({appliedVatRate:F2} %). Statut : VALEUR PAR DÉFAUT / NON VÉRIFIÉE.",
                    LineNumber: line.LineNumber));
            }
            else
            {
                // Aucune règle officielle, aucune confirmation manuelle, taux par défaut désactivés pour
                // cette importation : ce 0 % n'est JAMAIS un résultat définitif à afficher comme tel (voir
                // LineDetailDialogViewModel.TvaStatutFr côté IHM) — un avertissement explicite a déjà été
                // généré ci-dessus (boucle regOutcome.WarningsOrMissingInfo).
                appliedVatRate = 0m;
                vatRateOriginTag = DataOriginTag.CalculDuLogiciel;
            }

            decimal importVatDzd = CurrencyCalculator.RoundDzd(vatTaxableBaseDzd * (appliedVatRate / 100m));

            // 6bis. Autres taxes additionnelles (hors CS/PRCT/TCS, ex: DAPS/TIC/RDAE) dont la règle
            // officielle déclare explicitement l'assiette "Valeur douane + taxes avant TVA + TVA" —
            // calculées ICI car leur assiette a besoin de importVatDzd, désormais connu. AUCUN taux par
            // défaut n'est jamais inventé pour ces taxes (seule une règle officielle les déclenche).
            foreach (var taxRule in afterVatTaxRules)
            {
                decimal taxBase = CurrencyCalculator.RoundDzd(customsValueDzd + totalBeforeVatAdditionalTaxesDzd + importVatDzd);
                decimal taxAmount = CurrencyCalculator.RoundDzd(taxBase * (taxRule.RatePercent / 100m));

                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: taxRule.TaxCode,
                    TaxNameFr: taxRule.TaxNameFr,
                    TaxableBaseDzd: taxBase,
                    RatePercent: taxRule.RatePercent,
                    TaxAmountDzd: taxAmount,
                    IsNonRecoverable: true,
                    RegulatoryRuleCode: taxRule.Code,
                    LegalArticleReference: taxRule.LegalSource.ArticleReference,
                    JoraReference: taxRule.LegalSource.JoraReference,
                    RegulatoryVersionCode: taxRule.RegulatoryVersionCode,
                    OriginTag: DataOriginTag.DonneeOfficielle));
            }

            // ---- PRCT (Précompte à l'importation) : TOUJOURS calculé APRÈS la TVA (Section 7). ----
            // Règle officielle (sa propre assiette déclarée) > non applicable > confirmation manuelle
            // ponctuelle pour l'importation (existant) > taux PAR DÉFAUT (assiette Valeur douane + DD + CS
            // + TVA, Section 7) > non déterminé.
            var prctRule = FindOfficialTaxRule("PRCT");
            if (prctRule != null)
            {
                decimal prctBase = prctRule.CalculationBase switch
                {
                    TaxableBaseType.CustomsValueDzd => customsValueDzd,
                    TaxableBaseType.CustomsValuePlusPriorTaxesPlusVatDzd =>
                        CurrencyCalculator.RoundDzd(customsValueDzd + totalBeforeVatAdditionalTaxesDzd + importVatDzd),
                    _ => CurrencyCalculator.RoundDzd(customsValueDzd + customsDutyDzd + totalBeforeVatAdditionalTaxesDzd)
                };
                decimal prctAmount = CurrencyCalculator.RoundDzd(prctBase * (prctRule.RatePercent / 100m));
                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: "PRCT", TaxNameFr: prctRule.TaxNameFr, TaxableBaseDzd: prctBase, RatePercent: prctRule.RatePercent,
                    TaxAmountDzd: prctAmount, IsNonRecoverable: true, RegulatoryRuleCode: prctRule.Code,
                    LegalArticleReference: prctRule.LegalSource.ArticleReference, JoraReference: prctRule.LegalSource.JoraReference,
                    RegulatoryVersionCode: prctRule.RegulatoryVersionCode, OriginTag: DataOriginTag.DonneeOfficielle));
            }
            else if (IsExplicitlyNonApplicable("PRCT"))
            {
                anomalies.Add(new CalculationAnomaly(AnomalySeverity.Info, "PRCT_NOT_APPLICABLE",
                    $"ℹ️ Ligne {line.LineNumber} ({line.ProductReference}) : Précompte à l'importation (PRCT) explicitement NON APPLICABLE selon une règle réglementaire officielle.",
                    LineNumber: line.LineNumber));
            }
            else if (operation.UserConfirmedManualPrct && operation.ManualPrctRatePercent.HasValue)
            {
                decimal manualPrctBase = CurrencyCalculator.RoundDzd(customsValueDzd + totalBeforeVatAdditionalTaxesDzd + importVatDzd);
                decimal manualPrctAmount = CurrencyCalculator.RoundDzd(manualPrctBase * (operation.ManualPrctRatePercent.Value / 100m));
                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: "PRCT",
                    TaxNameFr: "Précompte à l'importation (PRCT)",
                    TaxableBaseDzd: manualPrctBase,
                    RatePercent: operation.ManualPrctRatePercent.Value,
                    TaxAmountDzd: manualPrctAmount,
                    IsNonRecoverable: true,
                    RegulatoryRuleCode: "MANUEL",
                    LegalArticleReference: "Taux saisi manuellement par l'utilisateur (aucune règle officielle publiée)",
                    JoraReference: "N/A",
                    RegulatoryVersionCode: "MANUEL",
                    OriginTag: DataOriginTag.DonneeUtilisateur));
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    "PRCT_MANUAL_RATE_USED",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Taux PRCT ({operation.ManualPrctRatePercent.Value:F2} %) saisi manuellement pour cette importation, en l'absence de règle officielle.",
                    LineNumber: line.LineNumber));
            }
            // Correction du 2026-10-02 (régression Section H) : même principe que pour la CS/TCS — le taux
            // PAR DÉFAUT du PRCT n'est invoqué que si le SH est totalement inconnu du référentiel
            // (regOutcome.IsDetermined == false). Un SH officiellement connu (DD+TVA publiés) sans règle
            // PRCT spécifique signifie que le PRCT ne s'applique pas à ce SH, jamais une valeur inventée.
            else if (useDefaultRates && !regOutcome.IsDetermined)
            {
                // Section 7 : Base PRCT par défaut = Valeur douane + CS + TVA + DD = (VD+DD+CS) + TVA,
                // c'est-à-dire exactement l'assiette TVA (vatTaxableBaseDzd) + la TVA elle-même.
                decimal prctBase = CurrencyCalculator.RoundDzd(vatTaxableBaseDzd + importVatDzd);
                decimal prctAmount = CurrencyCalculator.RoundDzd(prctBase * (operation.DefaultPrctRatePercent / 100m));
                additionalTaxBreakdowns.Add(new AppliedTaxBreakdown(
                    TaxCode: "PRCT", TaxNameFr: "Précompte à l'importation (PRCT)", TaxableBaseDzd: prctBase,
                    RatePercent: operation.DefaultPrctRatePercent, TaxAmountDzd: prctAmount, IsNonRecoverable: true,
                    RegulatoryRuleCode: "DEFAULT_IMPORT",
                    LegalArticleReference: "Taux par défaut de l'importation (aucune règle officielle publiée)",
                    JoraReference: "N/A", RegulatoryVersionCode: "DEFAULT_IMPORT",
                    OriginTag: DataOriginTag.ValeurParDefautImportation));
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    "PRCT_DEFAULT_RATE_USED",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Précompte à l'importation (PRCT) non déterminé réglementairement — taux par défaut de l'importation appliqué ({operation.DefaultPrctRatePercent:F2} %). Statut : VALEUR PAR DÉFAUT / NON VÉRIFIÉE.",
                    LineNumber: line.LineNumber));
            }
            else
            {
                anomalies.Add(new CalculationAnomaly(
                    AnomalySeverity.Avertissement,
                    "PRCT_RATE_NOT_DETERMINED",
                    $"⚠️ Ligne {line.LineNumber} ({line.ProductReference}) : Donnée réglementaire manquante pour le Précompte à l'importation (PRCT) — confirmez un taux pour cette importation (écran Importation) ou laissez non déterminé.",
                    LineNumber: line.LineNumber));
            }

            decimal totalAdditionalTaxesDzd = additionalTaxBreakdowns.Sum(t => t.TaxAmountDzd);

            // Résultat 1 (Section 25) : Coût / Total Droits et Taxes douaniers
            decimal totalLineDutiesAndTaxesDzd = CurrencyCalculator.RoundDzd(customsDutyDzd + totalAdditionalTaxesDzd + importVatDzd);
            decimal lineCustomsClearedTotalDzd = CurrencyCalculator.RoundDzd(customsValueDzd + totalLineDutiesAndTaxesDzd);

            var customsOutcome = new LineCustomsResult(
                CustomsValueDzd: customsValueDzd,
                CustomsDutyRatePercent: effectiveDutyRate,
                CustomsDutyAmountDzd: customsDutyDzd,
                ExcelVsRegulatoryComparison: comparisonStatus,
                ComparisonLabelFr: comparisonLabel,
                AdditionalTaxes: additionalTaxBreakdowns,
                TotalAdditionalTaxesDzd: totalAdditionalTaxesDzd,
                VatTaxableBaseDzd: vatTaxableBaseDzd,
                VatRatePercent: appliedVatRate,
                ImportVatAmountDzd: importVatDzd,
                TotalDutiesAndTaxesDzd: totalLineDutiesAndTaxesDzd,
                CustomsValuePlusDutiesAndTaxesDzd: lineCustomsClearedTotalDzd,
                // Section 17 de l'audit : citation légale issue de la règle réglementaire RÉELLEMENT
                // résolue ET EFFECTIVEMENT APPLIQUÉE pour cette ligne (jamais une référence générique codée
                // en dur, et jamais citée comme fondement d'un taux qui n'est pas réellement celui utilisé).
                // Revue du 2026-10-05 : depuis l'introduction de la priorité "DD Excel par défaut", une règle
                // réglementaire DD peut exister (regOutcome.CustomsDutyRule) sans être le taux appliqué (le
                // DD Excel, différent, l'emporte) — dans ce cas la citation légale doit rester null plutôt que
                // de présenter à tort l'article réglementaire comme fondement du taux affiché.
                CustomsDutyLegalArticleReference: effectiveRateComesFromAiRule ? regOutcome.CustomsDutyRule?.LegalSource.ArticleReference : null,
                CustomsDutyJoraReference: effectiveRateComesFromAiRule ? regOutcome.CustomsDutyRule?.LegalSource.JoraReference : null,
                CustomsDutyRegulatoryVersionCode: effectiveRateComesFromAiRule ? regOutcome.CustomsDutyRule?.RegulatoryVersionCode : null,
                VatLegalArticleReference: regOutcome.VatRule?.LegalSource.ArticleReference,
                VatJoraReference: regOutcome.VatRule?.LegalSource.JoraReference,
                VatRegulatoryVersionCode: regOutcome.VatRule?.RegulatoryVersionCode,
                StandardTaxApplicability: standardTaxApplicability,
                VatRateOriginTag: vatRateOriginTag,
                CustomsDutyRateOriginTag: customsDutyRateOriginTag,
                AiProposedDutyRatePercent: aiProposedDutyRatePercent);

            // Résultat 2 (Section 26) : Coût d'acquisition et Coût de revient économique réel
            //
            // Revue du 2026-10-05 (Tâche #20, point 3 — analyse du "double comptage du fret") : analyse
            // tracée précisément AVANT toute modification, demandée explicitement par l'utilisateur, du
            // chemin complet du montant d'un frais (ex: FRET_INTERNATIONAL) : saisie du frais -> conversion
            // devise (feeDzd plus haut) -> répartition entre lignes (AllocateFeeAcrossLines, un seul passage
            // par frais, un seul FeeAllocationTrace par (frais, ligne), cf. boucle "foreach (var fee in
            // operation.Fees)" ci-dessus) -> CustomsValueCalculator.CalculateLineCustomsValueDzd (qui
            // n'ajoute le montant alloué qu'UNE SEULE fois à la valeur en douane, uniquement si
            // IncludeInCustomsValue=true) -> droits/taxes (assiette = valeur en douane déjà calculée,
            // jamais le frais brut une seconde fois) -> ci-dessous, coût de revient.
            //
            // CONCLUSION DE L'ANALYSE (vérifiée algébriquement + par calcul manuel sur le jeu de données
            // réel V1CompleteTestSuite "Section H" et sur les 4 scénarios dédiés de
            // Task20WeightWarningAndFreightDoubleCountingTests) : la formule ci-dessous NE compte JAMAIS deux fois le
            // montant brut d'un même frais, dans AUCUNE des 4 combinaisons possibles des cases "Inclure dans
            // la valeur en douane" (IncludeInCustomsValue) / "Inclure dans le coût de revient"
            // (IncludeInCostOfGoods) :
            //   - Cas 1 (valeur douane seule) : le frais n'entre dans AUCUN des deux compartiments
            //     ci-dessous (ni feesInCustomsValueDzd, ni localFeesInCostOfGoodsDzd) car IncludedInCostOfGoods
            //     = false -> il n'est ajouté NULLE PART comme "frais de revient" ; son seul effet sur le coût
            //     de revient est indirect, via l'assiette plus élevée des droits/taxes (customsDutyDzd /
            //     totalAdditionalTaxesDzd), ce qui est le comportement légal attendu, pas un double comptage.
            //   - Cas 2 (coût de revient seul) : le frais entre dans localFeesInCostOfGoodsDzd (une seule
            //     fois) et n'affecte PAS la valeur en douane (CustomsValueCalculator l'ignore car
            //     IncludeInCustomsValue=false) -> compté une seule fois, au bon endroit.
            //   - Cas 3 (les deux cases cochées) : le frais entre dans feesInCustomsValueDzd (une seule
            //     fois, via le filtre LINQ qui sélectionne CHAQUE FeeAllocationTrace au plus une fois, un
            //     seul enregistrement existant par (frais, ligne)). Son montant apparaît alors UNE SEULE
            //     fois dans la somme "purchaseDzd + totalAllocatedFeesForCostDzd" ci-dessous — et cette somme
            //     est par construction algébriquement égale à "customsValueDzd" (plus les éventuels frais
            //     locaux hors valeur en douane) : purchaseDzd + feesInCustomsValueDzd == customsValueDzd
            //     lorsque ce frais est la seule addition. Le frais n'est donc PAS additionné une deuxième
            //     fois "en plus" de la valeur en douane : la valeur en douane ET le coût de revient
            //     partagent la MÊME occurrence unique du montant, jamais deux occurrences distinctes.
            //   - Cas 4 (aucune case cochée) : le frais n'apparaît dans aucun des deux compartiments ni dans
            //     la valeur en douane -> aucun effet, conformément à l'attendu.
            // Ces 4 cas sont couverts par des tests de non-régression dédiés (voir
            // Task20WeightWarningAndFreightDoubleCountingTests.cs) qui vérifient explicitement valeur en douane, DD, CS,
            // PRCT, TVA, frais alloués et coût de revient total pour chaque combinaison, ainsi que l'absence
            // de toute transformation d'un frais en "article" (le frais reste et demeure exclusivement dans
            // operation.Fees, jamais ajouté à operation.Lines).
            decimal feesInCustomsValueDzd = lineAllocations
                .Where(a => a.IncludedInCustomsValue && a.IncludedInCostOfGoods)
                .Sum(a => a.AllocatedAmountDzd);

            decimal localFeesInCostOfGoodsDzd = lineAllocations
                .Where(a => !a.IncludedInCustomsValue && a.IncludedInCostOfGoods)
                .Sum(a => a.AllocatedAmountDzd);

            decimal totalAllocatedFeesForCostDzd = CurrencyCalculator.RoundDzd(feesInCustomsValueDzd + localFeesInCostOfGoodsDzd);

            // Coût d'acquisition hors TVA = Prix d'achat DZD + Frais d'approche retenus + DD + Taxes non récupérables
            decimal acquisitionCostExVatDzd = CurrencyCalculator.RoundDzd(
                purchaseDzd + totalAllocatedFeesForCostDzd + customsDutyDzd + totalAdditionalTaxesDzd);

            // Section 24 & 26 : La TVA d'importation est traitée comme non récupérable dans ce modèle métier (intégrée au coût de revient)
            decimal nonRecoverableVatDzd = company.IsImportVatNonRecoverable ? importVatDzd : 0m;
            decimal realCostOfGoodsTotalDzd = CurrencyCalculator.RoundDzd(acquisitionCostExVatDzd + nonRecoverableVatDzd);
            decimal unitCostOfGoodsDzd = CurrencyCalculator.RoundDzd(realCostOfGoodsTotalDzd / line.Quantity);
            decimal landedMultiplier = purchaseDzd > 0m
                ? Math.Round(realCostOfGoodsTotalDzd / purchaseDzd, 6, MidpointRounding.AwayFromZero)
                : 0m;

            var economicOutcome = new LineEconomicCostResult(
                PurchaseValueCurrency: purchaseCurrency,
                PurchaseValueDzd: purchaseDzd,
                AllocatedCustomsIncludedFeesDzd: feesInCustomsValueDzd,
                AllocatedLocalAndPostCustomsFeesDzd: localFeesInCostOfGoodsDzd,
                TotalAllocatedFeesDzd: totalAllocatedFeesForCostDzd,
                CustomsDutyDzd: customsDutyDzd,
                NonRecoverableAdditionalTaxesDzd: totalAdditionalTaxesDzd,
                NonRecoverableImportVatDzd: nonRecoverableVatDzd,
                AcquisitionCostExVatDzd: acquisitionCostExVatDzd,
                RealCostOfGoodsTotalDzd: realCostOfGoodsTotalDzd,
                UnitCostOfGoodsDzd: unitCostOfGoodsDzd,
                LandedCostCoefficient: landedMultiplier);

            // Section 12 du plan multi-devises : conversion COMMERCIALE (jamais réglementaire) de cette
            // ligne vers la devise d'autorisation d'importation, pour affichage/export uniquement.
            LineCommercialConversion? lineAuthorizationConversion = null;
            if (needsAuthorizationConversion && !string.Equals(line.CurrencyCode, operation.AuthorizationCurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                decimal effectiveLineAuthorizationRate = authorizationRate;
                bool lineRateIsManual = authorizationRateIsManual;

                if (!string.Equals(line.CurrencyCode, operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase))
                {
                    // Cas rare : une ligne dans une devise différente de la devise principale de l'opération.
                    var (lineAuthRate, _, lineAuthAnomaly) = _commercialConversionService.ResolveCrossRate(
                        line.CurrencyCode, operation.AuthorizationCurrencyCode, operation.ReferenceDate, null);
                    effectiveLineAuthorizationRate = lineAuthRate;
                    lineRateIsManual = false;
                    if (lineAuthAnomaly != null)
                        anomalies.Add((lineAuthAnomaly with { Severity = AnomalySeverity.Avertissement, LineNumber = line.LineNumber }));
                }

                // Correction (point 2 de l'audit) : le montant d'autorisation est exprimé dans
                // operation.AuthorizationCurrencyCode (ex: USD), jamais en DZD — arrondi selon cette devise.
                decimal lineAuthorizationTotal = CurrencyRounding.Round(purchaseCurrency * effectiveLineAuthorizationRate, operation.AuthorizationCurrencyCode);
                decimal lineAuthorizationUnit = line.Quantity == 0m
                    ? 0m
                    : Math.Round(lineAuthorizationTotal / line.Quantity, 4, MidpointRounding.AwayFromZero);

                lineAuthorizationConversion = new LineCommercialConversion(
                    AuthorizationCurrencyCode: operation.AuthorizationCurrencyCode,
                    AuthorizationUnitPrice: lineAuthorizationUnit,
                    AuthorizationTotalAmount: lineAuthorizationTotal,
                    EffectiveRate: effectiveLineAuthorizationRate,
                    IsManualRate: lineRateIsManual);
            }

            lineResults.Add(new LineFullCalculationResult(
                LineNumber: line.LineNumber,
                ProductReference: line.ProductReference,
                Designation: line.Designation,
                Quantity: line.Quantity,
                CurrencyCode: line.CurrencyCode,
                AppliedExchangeRateToDzd: lineRate,
                CustomsOutcome: customsOutcome,
                EconomicOutcome: economicOutcome,
                FeeAllocations: lineAllocations,
                AuthorizationConversion: lineAuthorizationConversion));
        }

        // Section 5, 6, 9, 11 & 13 du plan multi-devises : conversion commerciale globale de la facture
        // (somme des lignes dans la devise principale) vers la devise de l'autorisation d'importation.
        CommercialAuthorizationConversion? commercialAuthorizationConversion = null;
        if (needsAuthorizationConversion)
        {
            decimal totalMainCurrencyAmount = lineResults
                .Where(l => string.Equals(l.CurrencyCode, operation.MainCurrencyCode, StringComparison.OrdinalIgnoreCase))
                .Sum(l => l.EconomicOutcome.PurchaseValueCurrency);

            // Correction (point 2 de l'audit) : montant agrégé exprimé en operation.AuthorizationCurrencyCode
            // (ex: USD), jamais en DZD.
            decimal totalAuthorizationAmount = CurrencyRounding.Round(totalMainCurrencyAmount * authorizationRate, operation.AuthorizationCurrencyCode);

            commercialAuthorizationConversion = new CommercialAuthorizationConversion(
                OriginalCurrencyCode: operation.MainCurrencyCode,
                OriginalTotalAmount: totalMainCurrencyAmount,
                AuthorizationCurrencyCode: operation.AuthorizationCurrencyCode,
                AuthorizationTotalAmount: totalAuthorizationAmount,
                EffectiveRate: authorizationRate,
                IsManualRate: authorizationRateIsManual,
                OfficialRateSourceName: authorizationOfficialRate?.SourceName,
                OfficialRateValidFrom: authorizationOfficialRate?.ValidFrom,
                RateTypeLabelFr: authorizationRateIsManual ? "⚠️ TAUX MANUEL" : "Taux officiel enregistré");
        }

        return new ImportCalculationSummary(
            ImportOperationId: operation.Id,
            ImportNumber: operation.ImportNumber,
            ReferenceDate: operation.ReferenceDate,
            IsSimulation: operation.IsSimulation,
            TotalPurchaseValueMainCurrency: lineResults.Sum(l => l.EconomicOutcome.PurchaseValueCurrency),
            TotalPurchaseValueDzd: lineResults.Sum(l => l.EconomicOutcome.PurchaseValueDzd),
            TotalCustomsValueDzd: lineResults.Sum(l => l.CustomsOutcome.CustomsValueDzd),
            TotalCustomsDutyDzd: lineResults.Sum(l => l.CustomsOutcome.CustomsDutyAmountDzd),
            TotalAdditionalTaxesDzd: lineResults.Sum(l => l.CustomsOutcome.TotalAdditionalTaxesDzd),
            TotalImportVatDzd: lineResults.Sum(l => l.CustomsOutcome.ImportVatAmountDzd),
            TotalDutiesAndTaxesDzd: lineResults.Sum(l => l.CustomsOutcome.TotalDutiesAndTaxesDzd),
            TotalImportFeesDzd: lineResults.Sum(l => l.EconomicOutcome.TotalAllocatedFeesDzd),
            TotalAcquisitionCostExVatDzd: lineResults.Sum(l => l.EconomicOutcome.AcquisitionCostExVatDzd),
            TotalRealCostOfGoodsDzd: lineResults.Sum(l => l.EconomicOutcome.RealCostOfGoodsTotalDzd),
            LineResults: lineResults,
            Anomalies: anomalies,
            HasBlockingAnomalies: anomalies.Any(a => a.Severity == AnomalySeverity.Blocage),
            MandatoryLegalDisclaimerFr: OfficialLegalDisclaimer,
            CommercialAuthorizationConversion: commercialAuthorizationConversion);
    }
}
