'use strict';
/**
 * Moteur de Calcul CIMP (port fonctionnel de ImportCostAlgeria.CalculationEngine).
 * Zéro taux ou règle en dur : toutes les valeurs fiscales proviennent du RegulatoryEngine (SQL).
 * Sépare strictement :
 *   - Résultat 1 : Valeur / Coût Douanier (Art. 16 bis à 16 decies Code des Douanes)
 *   - Résultat 2 : Coût de Revient Économique (SCF Algérien, Art. 121-3 & 123-1)
 */
const { db } = require('./db');
const reg = require('./regulatoryEngine');

const round2 = (n) => Math.round((Number(n) + Number.EPSILON) * 100) / 100;

const SEVERITY = { INFO: 'INFO', AVERTISSEMENT: 'AVERTISSEMENT', ERREUR: 'ERREUR', BLOCAGE: 'BLOCAGE' };

/**
 * Calcule la clé de répartition (ratio 0..1) de chaque ligne pour une méthode donnée.
 */
function computeAllocationRatios(lines, method, fee) {
  const n = lines.length;
  if (n === 0) return [];

  if (method === 'MANUEL' || method === 'MONTANT_FIXE') {
    let manual = {};
    try { manual = fee.manual_allocations_json ? JSON.parse(fee.manual_allocations_json) : {}; } catch { manual = {}; }
    const total = lines.reduce((s, l) => s + (Number(manual[l.line_number]) || 0), 0);
    if (total <= 0) return null; // signale une anomalie BLOCAGE en amont
    return lines.map(l => (Number(manual[l.line_number]) || 0) / total);
  }

  let bases;
  switch (method) {
    case 'PAR_QUANTITE':
      bases = lines.map(l => Number(l.quantity) || 0);
      break;
    case 'PAR_POIDS':
      bases = lines.map(l => Number(l.weight_kg) || 0);
      break;
    case 'PAR_VOLUME':
      bases = lines.map(l => Number(l.volume_m3) || 0);
      break;
    case 'POURCENTAGE':
    case 'PAR_VALEUR':
    default:
      bases = lines.map(l => l.__purchaseValueDzd || 0);
      break;
  }

  const total = bases.reduce((a, b) => a + b, 0);
  if (total <= 0) return null;
  return bases.map(b => b / total);
}

/**
 * Exécute le calcul complet d'un dossier d'importation.
 * @param {object} operation - ligne de import_operations
 * @param {Array} lines - lignes de import_lines
 * @param {Array} fees - lignes de import_fees
 * @param {object} company - ligne de companies
 */
function runFullCalculation(operation, lines, fees, company) {
  const anomalies = [];
  const referenceDateIso = operation.reference_date;

  if (!lines || lines.length === 0) {
    anomalies.push({ severity: SEVERITY.BLOCAGE, code: 'NO_LINES', lineNumber: null, message: "Aucune ligne d'article n'est présente dans ce dossier. Le calcul est impossible." });
    return { blocked: true, anomalies };
  }

  // 1. Taux de change (Art. 16 decies CDA)
  const currency = operation.main_currency_code;
  let exchangeRateToUse;
  let exchangeRateSourceNote;
  const officialRate = reg.findOfficialExchangeRate(currency, referenceDateIso);

  if (operation.exchange_rate_mode === 'MANUEL') {
    if (!operation.manual_exchange_rate || operation.manual_exchange_rate <= 0) {
      anomalies.push({ severity: SEVERITY.BLOCAGE, code: 'MANUAL_RATE_MISSING', lineNumber: null, message: 'Mode de taux manuel sélectionné mais aucun taux manuel valide renseigné.' });
      return { blocked: true, anomalies };
    }
    exchangeRateToUse = operation.manual_exchange_rate;
    exchangeRateSourceNote = 'TAUX MANUEL saisi par l\'utilisateur';
    if (officialRate.found && Math.abs(officialRate.rate - exchangeRateToUse) > 0.0001) {
      anomalies.push({
        severity: SEVERITY.AVERTISSEMENT,
        code: 'MANUAL_RATE_DIFFERS',
        lineNumber: null,
        message: `⚠️ TAUX MANUEL : Le taux utilisé (${exchangeRateToUse}) diffère du taux officiel enregistré (${officialRate.rate}) pour ${currency} au ${referenceDateIso}. Écart : ${round2(exchangeRateToUse - officialRate.rate)}.`,
        expectedValue: String(officialRate.rate),
        actualValue: String(exchangeRateToUse)
      });
    } else if (!officialRate.found) {
      anomalies.push({ severity: SEVERITY.INFO, code: 'MANUAL_RATE_NO_OFFICIAL_REF', lineNumber: null, message: `Taux manuel utilisé (${exchangeRateToUse}) : aucun taux officiel de référence disponible pour comparaison.` });
    }
  } else {
    if (!officialRate.found) {
      anomalies.push({ severity: SEVERITY.BLOCAGE, code: 'NO_OFFICIAL_RATE', lineNumber: null, message: officialRate.detail });
      return { blocked: true, anomalies };
    }
    exchangeRateToUse = officialRate.rate;
    exchangeRateSourceNote = `Taux officiel (${officialRate.source})`;
  }

  // 2. Valeur d'achat par ligne + validations de base
  for (const l of lines) {
    if (!l.currency_code) {
      anomalies.push({ severity: SEVERITY.ERREUR, code: 'MISSING_CURRENCY', lineNumber: l.line_number, message: `Ligne ${l.line_number} (${l.product_reference}) : devise manquante.` });
    }
    if (!(Number(l.quantity) > 0)) {
      anomalies.push({ severity: SEVERITY.ERREUR, code: 'INVALID_QUANTITY', lineNumber: l.line_number, message: `Ligne ${l.line_number} (${l.product_reference}) : quantité invalide (${l.quantity}).` });
    }
    if (!(Number(l.unit_purchase_price) > 0)) {
      anomalies.push({ severity: SEVERITY.ERREUR, code: 'INVALID_PRICE', lineNumber: l.line_number, message: `Ligne ${l.line_number} (${l.product_reference}) : prix unitaire invalide (${l.unit_purchase_price}).` });
    }
    if (!l.hs_code10 || reg.normalizeHs(l.hs_code10).length !== 10) {
      anomalies.push({ severity: SEVERITY.BLOCAGE, code: 'MISSING_HS_CODE', lineNumber: l.line_number, message: `Ligne ${l.line_number} (${l.product_reference}) : Code SH à 10 chiffres obligatoire manquant ou incomplet. Le calcul des droits est impossible sans classification confirmée.` });
    }
    if (l.hs_code_status === 'PROPOSE_IA_NON_CONFIRME') {
      anomalies.push({ severity: SEVERITY.AVERTISSEMENT, code: 'HS_AI_NOT_CONFIRMED', lineNumber: l.line_number, message: `Ligne ${l.line_number} (${l.product_reference}) : Code SH proposé par l'IA (${l.ai_proposed_hs_code10}) non encore confirmé par l'utilisateur.` });
    }
  }

  const blockingBase = anomalies.some(a => a.severity === SEVERITY.BLOCAGE);
  if (blockingBase) {
    return { blocked: true, anomalies };
  }

  const lineRates = {};
  for (const l of lines) {
    const rateForLine = l.currency_code === currency
      ? exchangeRateToUse
      : (reg.findOfficialExchangeRate(l.currency_code, referenceDateIso).rate || exchangeRateToUse);
    lineRates[l.line_number] = rateForLine;
    l.__purchaseValueDzd = round2(Number(l.quantity) * Number(l.unit_purchase_price) * rateForLine);
  }

  const totalPurchaseValueDzd = round2(lines.reduce((s, l) => s + l.__purchaseValueDzd, 0));

  // 3. Répartition des frais (7 méthodes)
  const feeAllocations = {}; // feeId -> [amounts per line index]
  for (const fee of fees) {
    let feeRateToDzd = 1;
    if (fee.currency_code !== 'DZD') {
      const r = reg.findOfficialExchangeRate(fee.currency_code, referenceDateIso);
      feeRateToDzd = r.found ? r.rate : exchangeRateToUse;
    }
    const feeAmountDzd = round2(Number(fee.amount) * feeRateToDzd);

    const ratios = computeAllocationRatios(lines, fee.allocation_method, fee);
    if (!ratios) {
      anomalies.push({ severity: SEVERITY.BLOCAGE, code: 'ALLOCATION_IMPOSSIBLE', lineNumber: null, message: `Frais "${fee.fee_name}" : impossible de calculer la répartition (méthode ${fee.allocation_method}) — base de répartition nulle ou allocation manuelle incomplète.` });
      feeAllocations[fee.id] = lines.map(() => 0);
      continue;
    }

    const rawAmounts = ratios.map(r => feeAmountDzd * r);
    // Réconciliation d'arrondi : le dernier montant absorbe l'écart pour que la somme = montant exact du frais
    const rounded = rawAmounts.map(a => round2(a));
    const sumRounded = round2(rounded.reduce((a, b) => a + b, 0));
    const diff = round2(feeAmountDzd - sumRounded);
    if (diff !== 0 && rounded.length > 0) rounded[rounded.length - 1] = round2(rounded[rounded.length - 1] + diff);

    feeAllocations[fee.id] = rounded;
    fee.__amountDzd = feeAmountDzd;
    fee.__rateToDzd = feeRateToDzd;
  }

  if (anomalies.some(a => a.severity === SEVERITY.BLOCAGE)) {
    return { blocked: true, anomalies };
  }

  // 4. Calcul ligne par ligne : Valeur en Douane -> DD -> Taxes additionnelles -> TVA -> Coût de revient
  const lineResults = [];
  let totalCustomsValueDzd = 0, totalCustomsDutyDzd = 0, totalAdditionalTaxesDzd = 0, totalVatDzd = 0, totalRealCostDzd = 0, totalAllocatedFeesDzd = 0;

  lines.forEach((l, idx) => {
    const hs = reg.normalizeHs(l.hs_code10);
    const origin = l.origin_country_iso2 || operation.default_origin_country_iso2 || null;

    let allocatedCustomsIncludedFeesDzd = 0;
    let allocatedLocalFeesDzd = 0;
    const feeAllocDetails = [];

    fees.forEach(fee => {
      const amt = feeAllocations[fee.id] ? feeAllocations[fee.id][idx] : 0;
      feeAllocDetails.push({ feeId: fee.id, feeName: fee.fee_name, allocatedAmountDzd: amt, includedInCustomsValue: !!fee.include_in_customs_value });
      if (fee.include_in_customs_value) allocatedCustomsIncludedFeesDzd += amt;
      if (fee.include_in_cost_of_goods) allocatedLocalFeesDzd += fee.include_in_customs_value ? 0 : amt;
    });
    allocatedCustomsIncludedFeesDzd = round2(allocatedCustomsIncludedFeesDzd);
    allocatedLocalFeesDzd = round2(allocatedLocalFeesDzd);

    // Valeur en Douane (Art. 16 bis, 16 ter, 16 octies CDA)
    const customsValueDzd = round2(l.__purchaseValueDzd + allocatedCustomsIncludedFeesDzd);

    // Incoterm cohérence (avertissement anti-double comptage / sous-évaluation)
    if (operation.incoterm === 'CFR' && fees.some(f => f.category_code === 'FRET_INTERNATIONAL' && f.include_in_customs_value)) {
      anomalies.push({ severity: SEVERITY.AVERTISSEMENT, code: 'CFR_FREIGHT_DOUBLE_COUNT', lineNumber: l.line_number, message: `Incoterm CFR : le prix inclut déjà le fret international jusqu'au port de destination. Vérifier qu'un frais "Fret international" additionnel inclus dans la valeur en douane ne fait pas double emploi.` });
    }
    if (operation.incoterm === 'EXW' && !fees.some(f => f.category_code === 'FRET_INTERNATIONAL' && f.include_in_customs_value)) {
      anomalies.push({ severity: SEVERITY.AVERTISSEMENT, code: 'EXW_MISSING_FREIGHT', lineNumber: l.line_number, message: `Incoterm EXW : le prix n'inclut aucun frais de transport. Aucun frais "Fret international" n'est inclus dans la valeur en douane — la valeur en douane risque d'être sous-évaluée (Art. 16 octies CDA).` });
    }

    // Droit de Douane (DD)
    const ddRule = reg.findApplicableRule('DD', hs, origin, 'DROIT_COMMUN_4000', referenceDateIso);
    let customsDutyRatePercent = null, customsDutyAmountDzd = 0, ddSource = null;
    if (ddRule.found) {
      customsDutyRatePercent = ddRule.rule.rate_percent;
      customsDutyAmountDzd = round2(customsValueDzd * customsDutyRatePercent / 100);
      ddSource = `${ddRule.rule.legal_source_title}${ddRule.rule.article_reference ? ' — ' + ddRule.rule.article_reference : ''}`;
    } else {
      anomalies.push({ severity: SEVERITY.BLOCAGE, code: 'DD_RULE_NOT_FOUND', lineNumber: l.line_number, message: `Ligne ${l.line_number} (${l.product_reference}, SH ${l.hs_code10}) : ${ddRule.detail}` });
    }

    // Comparaison Taux Excel vs Réglementaire (Section 20)
    if (l.excel_duty_rate_percent !== null && l.excel_duty_rate_percent !== undefined && customsDutyRatePercent !== null) {
      if (Math.abs(Number(l.excel_duty_rate_percent) - customsDutyRatePercent) > 0.0001) {
        anomalies.push({
          severity: SEVERITY.AVERTISSEMENT, code: 'DUTY_RATE_MISMATCH', lineNumber: l.line_number,
          message: `Ligne ${l.line_number} (${l.product_reference}) : Taux Excel (${l.excel_duty_rate_percent}%) différent du taux réglementaire officiel (${customsDutyRatePercent}%). Le taux réglementaire est appliqué en priorité.`,
          expectedValue: `${customsDutyRatePercent}%`, actualValue: `${l.excel_duty_rate_percent}%`
        });
      }
    } else if (l.excel_duty_rate_percent !== null && l.excel_duty_rate_percent !== undefined && customsDutyRatePercent === null) {
      anomalies.push({ severity: SEVERITY.AVERTISSEMENT, code: 'DUTY_RATE_NO_REG_REFERENCE', lineNumber: l.line_number, message: `Ligne ${l.line_number} : aucune règle réglementaire trouvée pour confirmer/infirmer le taux Excel (${l.excel_duty_rate_percent}%). Confirmation utilisateur requise avant utilisation de ce taux.` });
    }

    // Taxes additionnelles (DAPS, TIC, etc.) — toute règle publiée avec un tax_code hors DD/TVA s'applique automatiquement si trouvée, sinon ignorée (pas de taxe par défaut)
    const additionalTaxes = [];
    const otherRules = db.prepare(
      `SELECT DISTINCT tax_code FROM regulatory_rules WHERE hs_code10 = ? AND tax_code NOT IN ('DD','TVA') AND status='PUBLIEE'`
    ).all(hs);
    for (const { tax_code } of otherRules) {
      const r = reg.findApplicableRule(tax_code, hs, origin, 'DROIT_COMMUN_4000', referenceDateIso);
      if (r.found) {
        const base = r.rule.calculation_base === 'VALEUR_DOUANE_PLUS_DD' ? customsValueDzd + customsDutyAmountDzd : customsValueDzd;
        const amount = round2(base * r.rule.rate_percent / 100);
        additionalTaxes.push({
          taxCode: r.rule.tax_code, taxNameFr: r.rule.tax_name_fr, taxableBaseDzd: base,
          ratePercent: r.rule.rate_percent, taxAmountDzd: amount, isNonRecoverable: true,
          legalArticleReference: r.rule.article_reference || '', joraReference: r.rule.jora_reference || '',
          regulatoryVersionCode: r.rule.version_code
        });
      }
    }
    const totalAdditionalTaxesLineDzd = round2(additionalTaxes.reduce((s, t) => s + t.taxAmountDzd, 0));

    // TVA à l'importation (Art. 19, 21, 23 CTCA)
    const vatRule = reg.findApplicableRule('TVA', hs, origin, 'DROIT_COMMUN_4000', referenceDateIso);
    let vatRatePercent = null, importVatAmountDzd = 0, vatTaxableBaseDzd = round2(customsValueDzd + customsDutyAmountDzd + totalAdditionalTaxesLineDzd);
    if (vatRule.found) {
      vatRatePercent = vatRule.rule.rate_percent;
      importVatAmountDzd = round2(vatTaxableBaseDzd * vatRatePercent / 100);
    } else {
      anomalies.push({ severity: SEVERITY.BLOCAGE, code: 'VAT_RULE_NOT_FOUND', lineNumber: l.line_number, message: `Ligne ${l.line_number} (${l.product_reference}, SH ${l.hs_code10}) : ${vatRule.detail}` });
    }

    const totalDutiesAndTaxesLineDzd = round2(customsDutyAmountDzd + totalAdditionalTaxesLineDzd + importVatAmountDzd);
    const customsCostTotalDzd = round2(customsValueDzd + totalDutiesAndTaxesLineDzd);

    // Coût de Revient Économique (SCF, Art. 121-3 & 123-1)
    const vatNonRecoverable = !!company.is_vat_non_recoverable;
    const realCostOfGoodsTotalDzd = round2(
      l.__purchaseValueDzd + allocatedCustomsIncludedFeesDzd + allocatedLocalFeesDzd +
      customsDutyAmountDzd + totalAdditionalTaxesLineDzd + (vatNonRecoverable ? importVatAmountDzd : 0)
    );
    const unitCostOfGoodsDzd = round2(realCostOfGoodsTotalDzd / Number(l.quantity));

    lineResults.push({
      lineNumber: l.line_number, productReference: l.product_reference, designation: l.designation,
      quantity: Number(l.quantity), currencyCode: l.currency_code, appliedExchangeRateToDzd: lineRates[l.line_number],
      hsCode10: l.hs_code10, originCountryIso2: origin,
      purchaseValueDzd: l.__purchaseValueDzd,
      customsOutcome: {
        customsValueDzd, customsDutyRatePercent, customsDutyAmountDzd, ddSource,
        additionalTaxes, totalAdditionalTaxesDzd: totalAdditionalTaxesLineDzd,
        vatTaxableBaseDzd, vatRatePercent, importVatAmountDzd,
        totalDutiesAndTaxesDzd: totalDutiesAndTaxesLineDzd, customsCostTotalDzd,
        excelDutyRatePercent: l.excel_duty_rate_percent
      },
      economicOutcome: {
        purchaseValueDzd: l.__purchaseValueDzd,
        allocatedCustomsIncludedFeesDzd, allocatedLocalAndPostCustomsFeesDzd: allocatedLocalFeesDzd,
        totalAllocatedFeesDzd: round2(allocatedCustomsIncludedFeesDzd + allocatedLocalFeesDzd),
        vatIncludedInCost: vatNonRecoverable,
        realCostOfGoodsTotalDzd, unitCostOfGoodsDzd
      },
      feeAllocations: feeAllocDetails
    });

    totalCustomsValueDzd += customsValueDzd;
    totalCustomsDutyDzd += customsDutyAmountDzd;
    totalAdditionalTaxesDzd += totalAdditionalTaxesLineDzd;
    totalVatDzd += importVatAmountDzd;
    totalRealCostDzd += realCostOfGoodsTotalDzd;
    totalAllocatedFeesDzd += round2(allocatedCustomsIncludedFeesDzd + allocatedLocalFeesDzd);
  });

  if (anomalies.some(a => a.severity === SEVERITY.BLOCAGE)) {
    return { blocked: true, anomalies };
  }

  const totalImportFeesDzd = round2(fees.reduce((s, f) => s + (f.__amountDzd || 0), 0));
  const totalDutiesAndTaxesDzd = round2(totalCustomsDutyDzd + totalAdditionalTaxesDzd + totalVatDzd);
  const totalAcquisitionCostExVatDzd = round2(totalRealCostDzd - (company.is_vat_non_recoverable ? totalVatDzd : 0));

  return {
    blocked: false,
    anomalies,
    exchangeRateSourceNote,
    exchangeRateUsed: exchangeRateToUse,
    summary: {
      importOperationId: operation.id,
      importNumber: operation.import_number,
      totalPurchaseValueDzd,
      totalImportFeesDzd,
      totalCustomsValueDzd: round2(totalCustomsValueDzd),
      totalCustomsDutyDzd: round2(totalCustomsDutyDzd),
      totalAdditionalTaxesDzd: round2(totalAdditionalTaxesDzd),
      totalImportVatDzd: round2(totalVatDzd),
      totalDutiesAndTaxesDzd,
      totalAcquisitionCostExVatDzd,
      totalRealCostOfGoodsDzd: round2(totalRealCostDzd),
      lineResults,
      mandatoryLegalDisclaimerFr: "Les résultats sont calculés à partir des données saisies, des règles réglementaires enregistrées dans le système et des paramètres sélectionnés par l'utilisateur. Ils doivent être vérifiés au regard de la déclaration en détail et des documents douaniers officiels avant toute utilisation comptable, fiscale ou déclarative. CIMP ne remplace pas l'expertise d'un commissionnaire en douane agréé."
    }
  };
}

module.exports = { runFullCalculation, SEVERITY, round2 };
