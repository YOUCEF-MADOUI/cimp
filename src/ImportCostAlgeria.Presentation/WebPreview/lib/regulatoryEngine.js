'use strict';
/**
 * Moteur Réglementaire CIMP (port fonctionnel du moteur C# ImportCostAlgeria.RegulatoryEngine).
 * RÈGLE ABSOLUE : Aucun taux, aucune règle, aucune date d'application n'est codée en dur ici.
 * Toutes les règles proviennent de la table SQL `regulatory_rules`, alimentée exclusivement
 * par saisie contrôlée de l'utilisateur/administrateur depuis l'interface (Section 30).
 * Si aucune règle correspondante n'est trouvée : retour explicite "INFORMATION NON DÉTERMINÉE".
 */
const { db, uuid, nowIso, logAudit } = require('./db');

const INFO_NON_DETERMINEE = 'INFORMATION NON DÉTERMINÉE';

function normalizeHs(hs) {
  return (hs || '').replace(/[^0-9]/g, '');
}

/**
 * Recherche la règle réglementaire applicable la plus spécifique et valide à la date donnée.
 * Ordre de spécificité : (HS + Origine + Régime) > (HS + Régime) > (HS uniquement, origine NULL).
 */
function findApplicableRule(taxCode, hsCode10, originCountryIso2, regimeCode, referenceDateIso) {
  const hs = normalizeHs(hsCode10);
  if (!hs) {
    return { found: false, reason: INFO_NON_DETERMINEE, detail: 'Code SH manquant ou invalide : impossible de rechercher une règle réglementaire.' };
  }

  const rows = db.prepare(
    `SELECT * FROM regulatory_rules
     WHERE tax_code = ? AND hs_code10 = ? AND status = 'PUBLIEE'
       AND valid_from <= ?
       AND (valid_to IS NULL OR valid_to >= ?)
     ORDER BY
       CASE WHEN origin_country_iso2 = ? THEN 0 WHEN origin_country_iso2 IS NULL THEN 2 ELSE 1 END,
       CASE WHEN regime_code = ? THEN 0 ELSE 1 END,
       valid_from DESC`
  ).all(taxCode, hs, referenceDateIso, referenceDateIso, originCountryIso2 || '', regimeCode || 'DROIT_COMMUN_4000');

  if (!rows.length) {
    return {
      found: false,
      reason: INFO_NON_DETERMINEE,
      detail: `Aucune règle réglementaire publiée pour la taxe '${taxCode}' sur le Code SH ${hsCode10} à la date du ${referenceDateIso}. Saisie manuelle requise depuis l'écran Réglementation.`
    };
  }

  // Ne conserve que les correspondances dont l'origine est soit exactement identique, soit générique (NULL)
  const candidate = rows.find(r => !r.origin_country_iso2 || r.origin_country_iso2 === originCountryIso2) || rows[0];
  return { found: true, rule: candidate };
}

/** Liste toutes les règles (pour l'écran Réglementation), triées par SH puis date. */
function listAllRules() {
  return db.prepare(`SELECT * FROM regulatory_rules ORDER BY hs_code10, tax_code, valid_from DESC`).all();
}

function listRulesForHs(hsCode10) {
  const hs = normalizeHs(hsCode10);
  return db.prepare(`SELECT * FROM regulatory_rules WHERE hs_code10 = ? ORDER BY tax_code, valid_from DESC`).all(hs);
}

/**
 * Publie une nouvelle règle réglementaire versionnée SANS jamais écraser une règle existante :
 * si une règle antérieure couvre la même clé (taxe + SH + origine + régime) et est encore ouverte
 * (valid_to NULL), elle est automatiquement close à la veille de la nouvelle date d'effet.
 */
function publishNewRule(input, enteredBy) {
  const hs = normalizeHs(input.hsCode10);
  if (!hs || hs.length !== 10) {
    throw new Error('Le Code SH doit comporter exactement 10 chiffres (nomenclature douanière algérienne).');
  }
  if (!input.legalSourceTitle || !input.legalSourceTitle.trim()) {
    throw new Error("La source juridique (texte officiel) est obligatoire : une règle ne peut jamais être ajoutée sans référence à une source (Section 30).");
  }
  if (input.ratePercent === undefined || input.ratePercent === null || isNaN(Number(input.ratePercent))) {
    throw new Error('Le taux (%) est obligatoire et doit être numérique.');
  }

  const previous = db.prepare(
    `SELECT * FROM regulatory_rules
     WHERE tax_code = ? AND hs_code10 = ? AND IFNULL(origin_country_iso2,'') = ? AND regime_code = ?
       AND status = 'PUBLIEE' AND valid_to IS NULL`
  ).get(input.taxCode, hs, input.originCountryIso2 || '', input.regimeCode || 'DROIT_COMMUN_4000');

  if (previous) {
    const newFrom = new Date(input.validFrom);
    const dayBefore = new Date(newFrom.getTime() - 86400000).toISOString().slice(0, 10);
    db.prepare(`UPDATE regulatory_rules SET valid_to = ? WHERE id = ?`).run(dayBefore, previous.id);
    logAudit('regulatory_rule', previous.id, 'CLOSED_BY_NEW_VERSION', {
      closedAt: dayBefore, supersededBy: 'pending-new-id', oldRatePercent: previous.rate_percent
    });
  }

  const id = uuid();
  db.prepare(
    `INSERT INTO regulatory_rules
     (id, tax_code, tax_name_fr, hs_code10, origin_country_iso2, regime_code, rate_percent, calculation_base,
      valid_from, valid_to, legal_source_title, jora_reference, article_reference, status, version_code,
      superseded_rule_id, entered_by, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, ?, ?, ?, 'PUBLIEE', ?, ?, ?, ?)`
  ).run(
    id, input.taxCode, input.taxNameFr || input.taxCode, hs, input.originCountryIso2 || null,
    input.regimeCode || 'DROIT_COMMUN_4000', Number(input.ratePercent), input.calculationBase || 'VALEUR_DOUANE',
    input.validFrom, input.legalSourceTitle, input.joraReference || null, input.articleReference || null,
    input.versionCode || `V-${new Date().getFullYear()}.${String(new Date().getMonth() + 1).padStart(2, '0')}`,
    previous ? previous.id : null, enteredBy || 'admin', nowIso()
  );

  logAudit('regulatory_rule', id, 'PUBLISHED', { taxCode: input.taxCode, hsCode10: hs, ratePercent: input.ratePercent, validFrom: input.validFrom });
  return db.prepare('SELECT * FROM regulatory_rules WHERE id = ?').get(id);
}

/** Taux de change officiel applicable à une date donnée pour une devise (Art. 16 decies CDA). */
function findOfficialExchangeRate(currencyCode, referenceDateIso) {
  if (currencyCode === 'DZD') return { found: true, rate: 1, source: 'Devise nationale' };
  const row = db.prepare(
    `SELECT * FROM exchange_rates WHERE currency_code = ? AND is_official = 1
       AND valid_from <= ? AND (valid_to IS NULL OR valid_to >= ?)
     ORDER BY valid_from DESC LIMIT 1`
  ).get(currencyCode, referenceDateIso, referenceDateIso);
  if (!row) {
    return { found: false, reason: INFO_NON_DETERMINEE, detail: `Aucun taux de change officiel enregistré pour ${currencyCode} à la date du ${referenceDateIso}.` };
  }
  return { found: true, rate: row.rate_to_dzd, source: row.source, id: row.id };
}

function publishExchangeRate(input, enteredBy) {
  if (!input.currencyCode || !input.rateToDzd || !input.validFrom) {
    throw new Error('Devise, taux et date de validité sont obligatoires.');
  }
  const previous = db.prepare(
    `SELECT * FROM exchange_rates WHERE currency_code = ? AND is_official = 1 AND valid_to IS NULL ORDER BY valid_from DESC LIMIT 1`
  ).get(input.currencyCode);
  if (previous) {
    const dayBefore = new Date(new Date(input.validFrom).getTime() - 86400000).toISOString().slice(0, 10);
    db.prepare(`UPDATE exchange_rates SET valid_to = ? WHERE id = ?`).run(dayBefore, previous.id);
  }
  const id = uuid();
  db.prepare(
    `INSERT INTO exchange_rates (id, currency_code, rate_to_dzd, valid_from, valid_to, source, is_official, entered_by, created_at)
     VALUES (?, ?, ?, ?, NULL, ?, 1, ?, ?)`
  ).run(id, input.currencyCode, Number(input.rateToDzd), input.validFrom, input.source || 'Saisie manuelle ALCES', enteredBy || 'admin', nowIso());
  logAudit('exchange_rate', id, 'PUBLISHED', input);
  return db.prepare('SELECT * FROM exchange_rates WHERE id = ?').get(id);
}

function listExchangeRates() {
  return db.prepare('SELECT * FROM exchange_rates ORDER BY currency_code, valid_from DESC').all();
}

module.exports = {
  INFO_NON_DETERMINEE,
  normalizeHs,
  findApplicableRule,
  listAllRules,
  listRulesForHs,
  publishNewRule,
  findOfficialExchangeRate,
  publishExchangeRate,
  listExchangeRates
};
