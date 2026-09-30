'use strict';
/**
 * Moteur Excel CIMP (port fonctionnel de ImportCostAlgeria.ExcelEngine).
 * Lit réellement des fichiers .xlsx / .csv envoyés par l'utilisateur (aucune donnée statique),
 * détecte les colonnes connues via un dictionnaire de synonymes, calcule une signature d'en-têtes
 * pour retrouver/365 réutiliser automatiquement un modèle de mapping déjà enregistré pour un fournisseur.
 */
const XLSX = require('xlsx');
const crypto = require('crypto');
const { db, uuid, nowIso, logAudit } = require('./db');

/** Champs internes CIMP que l'on cherche à reconnaître dans le fichier fournisseur. */
const INTERNAL_FIELDS = [
  { key: 'reference', labelFr: 'Référence article', required: true },
  { key: 'designation', labelFr: 'Désignation', required: true },
  { key: 'quantity', labelFr: 'Quantité', required: true },
  { key: 'unitPrice', labelFr: 'Prix unitaire', required: true },
  { key: 'currency', labelFr: 'Devise', required: false },
  { key: 'hsCode', labelFr: 'Code SH', required: false },
  { key: 'origin', labelFr: "Pays d'origine", required: false },
  { key: 'dutyRate', labelFr: 'Taux de droit de douane (Excel)', required: false },
  { key: 'weight', labelFr: 'Poids (kg)', required: false },
  { key: 'volume', labelFr: 'Volume (m³)', required: false }
];

/** Dictionnaire de synonymes (Section 6) — insensible à la casse, aux accents et aux espaces. */
const SYNONYMS = {
  reference: ['reference', 'référence', 'ref', 'code article', 'code produit', 'article', 'sku', 'item code', 'item ref'],
  designation: ['designation', 'désignation', 'description', 'libelle', 'libellé', 'product name', 'nom produit', 'article description'],
  quantity: ['quantite', 'quantité', 'qte', 'qty', 'quantity', 'nombre'],
  unitPrice: ['prix unitaire', 'prix unit', 'pu', 'unit price', 'prix', 'prix fournisseur', 'unitprice', 'price'],
  currency: ['devise', 'currency', 'monnaie', 'cur'],
  hsCode: ['code sh', 'hs code', 'code douanier', 'nomenclature', 'tariff code', 'sh', 'hscode'],
  origin: ['origine', 'origin', 'pays origine', "pays d'origine", 'country of origin', 'made in'],
  dutyRate: ['dd', 'droit de douane', 'duty rate', 'taux dd', 'customs duty', 'droit douane'],
  weight: ['poids', 'poids brut', 'weight', 'gross weight', 'poids kg'],
  volume: ['volume', 'cbm', 'volume m3', 'm3']
};

function normalizeHeader(h) {
  return String(h || '')
    .normalize('NFD').replace(/[\u0300-\u036f]/g, '') // enlève les accents
    .toLowerCase()
    .replace(/[_\-]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function computeHeaderSignature(headers) {
  const normalized = headers.map(normalizeHeader).sort().join('|');
  return crypto.createHash('sha256').update(normalized).digest('hex');
}

/** Lit un fichier .xlsx ou .csv depuis un buffer et retourne { headers, rows } (première feuille). */
function parseWorkbookBuffer(buffer, originalFilename) {
  const wb = XLSX.read(buffer, { type: 'buffer', cellDates: false });
  const sheetName = wb.SheetNames[0];
  const sheet = wb.Sheets[sheetName];
  const aoa = XLSX.utils.sheet_to_json(sheet, { header: 1, raw: true, defval: null });
  if (!aoa.length) throw new Error("Le fichier est vide ou n'a pas pu être lu.");

  const headers = (aoa[0] || []).map(h => (h === null ? '' : String(h)));
  const rows = aoa.slice(1).filter(r => r.some(c => c !== null && String(c).trim() !== ''));

  return { sheetName, headers, rows, rowObjects: rows.map(r => headers.map((h, i) => r[i] ?? null)) };
}

/** Propose un mapping automatique {internalKey: columnIndex} en s'appuyant sur le dictionnaire de synonymes. */
function autoDetectMapping(headers) {
  const normalizedHeaders = headers.map(normalizeHeader);
  const mapping = {};
  const unrecognizedColumns = [];

  normalizedHeaders.forEach((nh, idx) => {
    if (!nh) return;
    let matchedKey = null;
    for (const [key, syns] of Object.entries(SYNONYMS)) {
      if (syns.includes(nh) || syns.some(s => nh === s)) { matchedKey = key; break; }
    }
    if (!matchedKey) {
      // correspondance partielle (contient)
      for (const [key, syns] of Object.entries(SYNONYMS)) {
        if (syns.some(s => nh.includes(s) || s.includes(nh))) { matchedKey = key; break; }
      }
    }
    if (matchedKey && mapping[matchedKey] === undefined) {
      mapping[matchedKey] = idx;
    } else if (!matchedKey) {
      unrecognizedColumns.push({ index: idx, header: headers[idx] });
    }
  });

  return { mapping, unrecognizedColumns };
}

function findSavedTemplate(companyId, headerSignature) {
  return db.prepare(`SELECT * FROM excel_mapping_templates WHERE company_id = ? AND header_signature = ?`).get(companyId, headerSignature);
}

function saveTemplate(companyId, supplierName, headerSignature, mapping) {
  const existing = findSavedTemplate(companyId, headerSignature);
  if (existing) {
    db.prepare(`UPDATE excel_mapping_templates SET mapping_json = ?, supplier_name = ?, updated_at = ? WHERE id = ?`)
      .run(JSON.stringify(mapping), supplierName, nowIso(), existing.id);
    logAudit('excel_mapping_template', existing.id, 'UPDATED', { supplierName, mapping });
    return existing.id;
  }
  const id = uuid();
  db.prepare(
    `INSERT INTO excel_mapping_templates (id, company_id, supplier_name, header_signature, mapping_json, created_at, updated_at)
     VALUES (?, ?, ?, ?, ?, ?, ?)`
  ).run(id, companyId, supplierName, headerSignature, JSON.stringify(mapping), nowIso(), nowIso());
  logAudit('excel_mapping_template', id, 'CREATED', { supplierName, mapping });
  return id;
}

function listTemplates(companyId) {
  return db.prepare(`SELECT * FROM excel_mapping_templates WHERE company_id = ? ORDER BY updated_at DESC`).all(companyId);
}

/** Convertit les lignes brutes + mapping confirmé en lignes structurées prêtes à être importées. */
function buildStructuredRows(headers, rows, mapping) {
  const errors = [];
  const structured = rows.map((row, i) => {
    const get = (key) => (mapping[key] !== undefined && mapping[key] !== null && mapping[key] !== '' ? row[mapping[key]] : null);

    const reference = get('reference');
    const designation = get('designation');
    const quantity = Number(get('quantity'));
    const unitPrice = Number(get('unitPrice'));
    const currency = get('currency') ? String(get('currency')).toUpperCase().trim() : null;
    const hsCode = get('hsCode') ? String(get('hsCode')).replace(/[^0-9]/g, '') : null;
    const origin = get('origin') ? String(get('origin')).toUpperCase().trim() : null;
    const dutyRate = get('dutyRate') !== null && get('dutyRate') !== undefined && get('dutyRate') !== '' ? Number(get('dutyRate')) : null;
    const weight = get('weight') !== null ? Number(get('weight')) : null;
    const volume = get('volume') !== null ? Number(get('volume')) : null;

    const rowErrors = [];
    if (!reference) rowErrors.push('Référence manquante');
    if (!designation) rowErrors.push('Désignation manquante');
    if (!(quantity > 0)) rowErrors.push('Quantité invalide');
    if (!(unitPrice > 0)) rowErrors.push('Prix unitaire invalide');

    if (rowErrors.length) errors.push({ rowIndex: i + 1, errors: rowErrors });

    return {
      lineNumber: i + 1,
      reference: reference ? String(reference).trim() : null,
      designation: designation ? String(designation).trim() : null,
      quantity: isNaN(quantity) ? null : quantity,
      unitPrice: isNaN(unitPrice) ? null : unitPrice,
      currency, hsCode, origin, dutyRate,
      weight: isNaN(weight) ? null : weight,
      volume: isNaN(volume) ? null : volume,
      hasErrors: rowErrors.length > 0
    };
  });

  return { structured, errors };
}

module.exports = {
  INTERNAL_FIELDS, SYNONYMS,
  normalizeHeader, computeHeaderSignature,
  parseWorkbookBuffer, autoDetectMapping,
  findSavedTemplate, saveTemplate, listTemplates,
  buildStructuredRows
};
