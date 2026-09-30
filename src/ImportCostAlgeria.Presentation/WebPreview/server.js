'use strict';
/**
 * CIMP — Serveur applicatif complet (Express + SQLite persistant).
 * Remplace l'ancienne maquette statique : toutes les opérations sont réelles et persistées.
 */
const path = require('path');
const fs = require('fs');
const express = require('express');
const multer = require('multer');

const { db, uuid, nowIso, logAudit, ensureDefaultAdmin } = require('./lib/db');
const reg = require('./lib/regulatoryEngine');
const excel = require('./lib/excelEngine');
const calc = require('./lib/calculationEngine');
const report = require('./lib/reportingEngine');
const ai = require('./lib/aiHsClassifier');

const app = express();
const PORT = process.env.PORT || 3000;
const upload = multer({ storage: multer.memoryStorage(), limits: { fileSize: 15 * 1024 * 1024 } });

app.use(express.json({ limit: '5mb' }));
app.use(express.static(path.join(__dirname, 'public')));

ensureDefaultAdmin();

function ok(res, data) { res.json({ success: true, data }); }
function fail(res, code, message) { res.status(code).json({ success: false, error: message }); }

/** Récupère l'utilisateur agissant depuis l'en-tête X-User-Id (ou req.body.actingUserId) et vérifie son rôle. */
function resolveActingUser(req) {
  const userId = req.get('X-User-Id') || (req.body && req.body.actingUserId) || null;
  if (!userId) return null;
  return db.prepare('SELECT * FROM users WHERE id = ?').get(userId) || null;
}

/**
 * Garde de sécurité (Section 21 & 39) : seul un utilisateur au rôle ADMINISTRATEUR peut publier
 * une règle réglementaire ou un taux de change officiel. Retourne l'utilisateur si autorisé, sinon
 * répond 403 et retourne null.
 */
function requireAdmin(req, res) {
  const user = resolveActingUser(req);
  if (!user) {
    fail(res, 403, "Utilisateur non identifié. Sélectionnez votre profil utilisateur (menu Utilisateurs) avant de publier une règle.");
    return null;
  }
  if (user.role !== 'ADMINISTRATEUR') {
    fail(res, 403, `Seul un utilisateur avec le rôle Administrateur peut effectuer cette action (rôle actuel : ${user.role}).`);
    return null;
  }
  return user;
}

// ---------------------------------------------------------------------------
// UTILISATEURS & RÔLES (Sections 21, 39 — Administrateur / Utilisateur / Consultation)
// ---------------------------------------------------------------------------
app.get('/api/users', (req, res) => {
  ok(res, db.prepare('SELECT * FROM users ORDER BY role, full_name').all());
});

app.post('/api/users', (req, res) => {
  const { fullName, role, companyId } = req.body || {};
  if (!fullName || !fullName.trim()) return fail(res, 400, 'Le nom complet est obligatoire.');
  const validRoles = ['ADMINISTRATEUR', 'UTILISATEUR', 'CONSULTATION'];
  const finalRole = validRoles.includes(role) ? role : 'UTILISATEUR';
  const id = uuid();
  db.prepare(`INSERT INTO users (id, company_id, full_name, role, created_at) VALUES (?, ?, ?, ?, ?)`)
    .run(id, companyId || null, fullName.trim(), finalRole, nowIso());
  logAudit('user', id, 'CREATED', req.body);
  ok(res, db.prepare('SELECT * FROM users WHERE id = ?').get(id));
});

app.put('/api/users/:id', (req, res) => {
  const existing = db.prepare('SELECT * FROM users WHERE id = ?').get(req.params.id);
  if (!existing) return fail(res, 404, 'Utilisateur introuvable.');
  const { fullName, role } = req.body || {};
  const validRoles = ['ADMINISTRATEUR', 'UTILISATEUR', 'CONSULTATION'];
  db.prepare(`UPDATE users SET full_name=?, role=? WHERE id=?`)
    .run(fullName ?? existing.full_name, validRoles.includes(role) ? role : existing.role, req.params.id);
  logAudit('user', req.params.id, 'UPDATED', req.body);
  ok(res, db.prepare('SELECT * FROM users WHERE id = ?').get(req.params.id));
});

app.delete('/api/users/:id', (req, res) => {
  const remainingAdmins = db.prepare(`SELECT COUNT(*) as c FROM users WHERE role='ADMINISTRATEUR' AND id != ?`).get(req.params.id).c;
  const target = db.prepare('SELECT * FROM users WHERE id = ?').get(req.params.id);
  if (target && target.role === 'ADMINISTRATEUR' && remainingAdmins === 0) {
    return fail(res, 400, "Impossible de supprimer le dernier compte Administrateur du système.");
  }
  db.prepare('DELETE FROM users WHERE id = ?').run(req.params.id);
  logAudit('user', req.params.id, 'DELETED', {});
  ok(res, { deleted: true });
});

// ---------------------------------------------------------------------------
// ENTREPRISES (Section 5 — CRUD complet)
// ---------------------------------------------------------------------------
app.get('/api/companies', (req, res) => {
  ok(res, db.prepare('SELECT * FROM companies ORDER BY legal_name').all());
});

app.post('/api/companies', (req, res) => {
  const { legalName, nif, address, activity, isVatNonRecoverable } = req.body || {};
  if (!legalName || !legalName.trim()) return fail(res, 400, 'La raison sociale est obligatoire.');
  const id = uuid();
  db.prepare(`INSERT INTO companies (id, legal_name, nif, address, activity, is_vat_non_recoverable, created_at, updated_at)
              VALUES (?, ?, ?, ?, ?, ?, ?, ?)`)
    .run(id, legalName.trim(), nif || null, address || null, activity || null, isVatNonRecoverable === false ? 0 : 1, nowIso(), nowIso());
  logAudit('company', id, 'CREATED', req.body);
  ok(res, db.prepare('SELECT * FROM companies WHERE id = ?').get(id));
});

app.put('/api/companies/:id', (req, res) => {
  const { legalName, nif, address, activity, isVatNonRecoverable } = req.body || {};
  const existing = db.prepare('SELECT * FROM companies WHERE id = ?').get(req.params.id);
  if (!existing) return fail(res, 404, 'Entreprise introuvable.');
  db.prepare(`UPDATE companies SET legal_name=?, nif=?, address=?, activity=?, is_vat_non_recoverable=?, updated_at=? WHERE id=?`)
    .run(legalName ?? existing.legal_name, nif ?? existing.nif, address ?? existing.address, activity ?? existing.activity,
      isVatNonRecoverable === undefined ? existing.is_vat_non_recoverable : (isVatNonRecoverable ? 1 : 0), nowIso(), req.params.id);
  logAudit('company', req.params.id, 'UPDATED', req.body);
  ok(res, db.prepare('SELECT * FROM companies WHERE id = ?').get(req.params.id));
});

app.delete('/api/companies/:id', (req, res) => {
  db.prepare('DELETE FROM companies WHERE id = ?').run(req.params.id);
  logAudit('company', req.params.id, 'DELETED', {});
  ok(res, { deleted: true });
});

// ---------------------------------------------------------------------------
// PRODUITS (Section 5 — CRUD complet, isolé par entreprise)
// ---------------------------------------------------------------------------
app.get('/api/companies/:companyId/products', (req, res) => {
  ok(res, db.prepare('SELECT * FROM products WHERE company_id = ? ORDER BY reference').all(req.params.companyId));
});

app.post('/api/companies/:companyId/products', (req, res) => {
  const { reference, designation, hsCode10, originCountryIso2, unit, defaultUnitPrice, currencyCode } = req.body || {};
  if (!reference || !designation) return fail(res, 400, 'Référence et désignation sont obligatoires.');
  const id = uuid();
  try {
    db.prepare(`INSERT INTO products (id, company_id, reference, designation, hs_code10, origin_country_iso2, unit, default_unit_price, currency_code, created_at, updated_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`)
      .run(id, req.params.companyId, reference.trim(), designation.trim(), hsCode10 || null, originCountryIso2 || null,
        unit || 'U', defaultUnitPrice || null, currencyCode || null, nowIso(), nowIso());
  } catch (e) {
    return fail(res, 409, `Un produit avec la référence "${reference}" existe déjà pour cette entreprise.`);
  }
  logAudit('product', id, 'CREATED', req.body);
  ok(res, db.prepare('SELECT * FROM products WHERE id = ?').get(id));
});

app.put('/api/products/:id', (req, res) => {
  const existing = db.prepare('SELECT * FROM products WHERE id = ?').get(req.params.id);
  if (!existing) return fail(res, 404, 'Produit introuvable.');
  const b = req.body || {};
  db.prepare(`UPDATE products SET designation=?, hs_code10=?, origin_country_iso2=?, unit=?, default_unit_price=?, currency_code=?, updated_at=? WHERE id=?`)
    .run(b.designation ?? existing.designation, b.hsCode10 ?? existing.hs_code10, b.originCountryIso2 ?? existing.origin_country_iso2,
      b.unit ?? existing.unit, b.defaultUnitPrice ?? existing.default_unit_price, b.currencyCode ?? existing.currency_code, nowIso(), req.params.id);
  logAudit('product', req.params.id, 'UPDATED', b);
  ok(res, db.prepare('SELECT * FROM products WHERE id = ?').get(req.params.id));
});

app.delete('/api/products/:id', (req, res) => {
  db.prepare('DELETE FROM products WHERE id = ?').run(req.params.id);
  logAudit('product', req.params.id, 'DELETED', {});
  ok(res, { deleted: true });
});

// ---------------------------------------------------------------------------
// TAUX DE CHANGE (Art. 16 decies CDA)
// ---------------------------------------------------------------------------
app.get('/api/exchange-rates', (req, res) => ok(res, reg.listExchangeRates()));

app.post('/api/exchange-rates', (req, res) => {
  const admin = requireAdmin(req, res);
  if (!admin) return;
  try {
    const created = reg.publishExchangeRate(req.body, admin.full_name);
    ok(res, created);
  } catch (e) { fail(res, 400, e.message); }
});

// ---------------------------------------------------------------------------
// RÉGLEMENTATION (Sections 12, 20, 21, 30 — versionné, jamais écrasé, jamais inventé)
// ---------------------------------------------------------------------------
app.get('/api/regulatory/rules', (req, res) => {
  if (req.query.hsCode10) return ok(res, reg.listRulesForHs(req.query.hsCode10));
  ok(res, reg.listAllRules());
});

// Historique détaillé des versions successives d'une taxe pour un Code SH donné (Section 19)
app.get('/api/regulatory/rules/history/:hsCode10/:taxCode', (req, res) => {
  const rows = reg.listRulesForHs(req.params.hsCode10).filter(r => r.tax_code === req.params.taxCode)
    .sort((a, b) => a.valid_from.localeCompare(b.valid_from));
  const diffs = [];
  for (let i = 1; i < rows.length; i++) {
    diffs.push({
      oldVersion: rows[i - 1].version_code, oldRate: rows[i - 1].rate_percent, oldValidFrom: rows[i - 1].valid_from, oldValidTo: rows[i - 1].valid_to,
      newVersion: rows[i].version_code, newRate: rows[i].rate_percent, newValidFrom: rows[i].valid_from,
      deltaPercentagePoints: Math.round((rows[i].rate_percent - rows[i - 1].rate_percent) * 100) / 100,
      legalSource: `${rows[i].legal_source_title} (${rows[i].jora_reference || ''})`
    });
  }
  ok(res, { versions: rows, diffs });
});

app.post('/api/regulatory/rules', (req, res) => {
  const admin = requireAdmin(req, res);
  if (!admin) return;
  try {
    const created = reg.publishNewRule(req.body, admin.full_name);
    ok(res, created);
  } catch (e) { fail(res, 400, e.message); }
});

// ---------------------------------------------------------------------------
// DOSSIERS D'IMPORTATION (Sections 8, 9 — CRUD complet)
// ---------------------------------------------------------------------------
app.get('/api/companies/:companyId/imports', (req, res) => {
  ok(res, db.prepare('SELECT * FROM import_operations WHERE company_id = ? ORDER BY created_at DESC').all(req.params.companyId));
});

app.post('/api/companies/:companyId/imports', (req, res) => {
  const b = req.body || {};
  if (!b.importNumber || !b.referenceDate || !b.supplierName || !b.exportShippingCountryIso2 || !b.mainCurrencyCode || !b.incoterm) {
    return fail(res, 400, 'Numéro, date de référence, fournisseur, pays d\'expédition, devise et Incoterm sont obligatoires.');
  }
  const id = uuid();
  try {
    db.prepare(`INSERT INTO import_operations
      (id, company_id, import_number, reference_date, supplier_name, export_shipping_country_iso2, default_origin_country_iso2,
       main_currency_code, exchange_rate_mode, manual_exchange_rate, incoterm, arrival_port, transport_mode, invoice_number,
       observations, status, created_at, updated_at)
      VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'BROUILLON', ?, ?)`)
      .run(id, req.params.companyId, b.importNumber.trim(), b.referenceDate, b.supplierName, b.exportShippingCountryIso2,
        b.defaultOriginCountryIso2 || null, b.mainCurrencyCode, b.exchangeRateMode || 'AUTO', b.manualExchangeRate || null,
        b.incoterm, b.arrivalPort || null, b.transportMode || null, b.invoiceNumber || null, b.observations || null, nowIso(), nowIso());
  } catch (e) {
    return fail(res, 409, `Un dossier avec le numéro "${b.importNumber}" existe déjà pour cette entreprise.`);
  }
  logAudit('import_operation', id, 'CREATED', b);
  ok(res, db.prepare('SELECT * FROM import_operations WHERE id = ?').get(id));
});

function getFullImport(id) {
  const operation = db.prepare('SELECT * FROM import_operations WHERE id = ?').get(id);
  if (!operation) return null;
  const lines = db.prepare('SELECT * FROM import_lines WHERE import_operation_id = ? ORDER BY line_number').all(id);
  const fees = db.prepare('SELECT * FROM import_fees WHERE import_operation_id = ? ORDER BY created_at').all(id);
  const company = db.prepare('SELECT * FROM companies WHERE id = ?').get(operation.company_id);
  const lastSnapshot = db.prepare('SELECT * FROM calculation_snapshots WHERE import_operation_id = ? ORDER BY executed_at DESC LIMIT 1').get(id);
  return { operation, lines, fees, company, lastSnapshot: lastSnapshot ? { ...lastSnapshot, result: JSON.parse(lastSnapshot.result_json) } : null };
}

app.get('/api/imports/:id', (req, res) => {
  const full = getFullImport(req.params.id);
  if (!full) return fail(res, 404, 'Dossier introuvable.');
  ok(res, full);
});

app.put('/api/imports/:id', (req, res) => {
  const existing = db.prepare('SELECT * FROM import_operations WHERE id = ?').get(req.params.id);
  if (!existing) return fail(res, 404, 'Dossier introuvable.');
  const b = req.body || {};
  db.prepare(`UPDATE import_operations SET
      reference_date=?, supplier_name=?, export_shipping_country_iso2=?, default_origin_country_iso2=?,
      main_currency_code=?, exchange_rate_mode=?, manual_exchange_rate=?, incoterm=?, arrival_port=?, transport_mode=?,
      invoice_number=?, observations=?, status=?, updated_at=?
    WHERE id=?`)
    .run(
      b.referenceDate ?? existing.reference_date, b.supplierName ?? existing.supplier_name,
      b.exportShippingCountryIso2 ?? existing.export_shipping_country_iso2, b.defaultOriginCountryIso2 ?? existing.default_origin_country_iso2,
      b.mainCurrencyCode ?? existing.main_currency_code, b.exchangeRateMode ?? existing.exchange_rate_mode,
      b.manualExchangeRate ?? existing.manual_exchange_rate, b.incoterm ?? existing.incoterm,
      b.arrivalPort ?? existing.arrival_port, b.transportMode ?? existing.transport_mode,
      b.invoiceNumber ?? existing.invoice_number, b.observations ?? existing.observations,
      b.status ?? existing.status, nowIso(), req.params.id
    );
  logAudit('import_operation', req.params.id, 'UPDATED', b);
  ok(res, db.prepare('SELECT * FROM import_operations WHERE id = ?').get(req.params.id));
});

app.delete('/api/imports/:id', (req, res) => {
  db.prepare('DELETE FROM import_operations WHERE id = ?').run(req.params.id);
  logAudit('import_operation', req.params.id, 'DELETED', {});
  ok(res, { deleted: true });
});

// ---------------------------------------------------------------------------
// LIGNES D'ARTICLES (saisie manuelle directe, en plus de l'import Excel)
// ---------------------------------------------------------------------------
function nextLineNumber(importId) {
  const row = db.prepare('SELECT MAX(line_number) as m FROM import_lines WHERE import_operation_id = ?').get(importId);
  return (row.m || 0) + 1;
}

app.post('/api/imports/:id/lines', (req, res) => {
  const b = req.body || {};
  if (!b.reference || !b.designation || !(b.quantity > 0) || !(b.unitPrice > 0) || !b.currencyCode) {
    return fail(res, 400, 'Référence, désignation, quantité, prix unitaire et devise sont obligatoires.');
  }
  const id = uuid();
  const lineNumber = b.lineNumber || nextLineNumber(req.params.id);
  db.prepare(`INSERT INTO import_lines
    (id, import_operation_id, line_number, product_reference, designation, quantity, unit_purchase_price, currency_code,
     hs_code10, hs_code_status, ai_proposed_hs_code10, origin_country_iso2, excel_duty_rate_percent, weight_kg, volume_m3, created_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`)
    .run(id, req.params.id, lineNumber, b.reference, b.designation, b.quantity, b.unitPrice, b.currencyCode,
      b.hsCode10 || null, b.hsCode10 ? 'CONFIRME_MANUEL' : 'NON_RENSEIGNE', null, b.originCountryIso2 || null,
      b.dutyRate ?? null, b.weight ?? null, b.volume ?? null, nowIso());
  logAudit('import_line', id, 'CREATED', b);
  ok(res, db.prepare('SELECT * FROM import_lines WHERE id = ?').get(id));
});

app.put('/api/lines/:id', (req, res) => {
  const existing = db.prepare('SELECT * FROM import_lines WHERE id = ?').get(req.params.id);
  if (!existing) return fail(res, 404, 'Ligne introuvable.');
  const b = req.body || {};
  db.prepare(`UPDATE import_lines SET product_reference=?, designation=?, quantity=?, unit_purchase_price=?, currency_code=?,
      hs_code10=?, hs_code_status=?, origin_country_iso2=?, excel_duty_rate_percent=?, weight_kg=?, volume_m3=? WHERE id=?`)
    .run(
      b.reference ?? existing.product_reference, b.designation ?? existing.designation, b.quantity ?? existing.quantity,
      b.unitPrice ?? existing.unit_purchase_price, b.currencyCode ?? existing.currency_code, b.hsCode10 ?? existing.hs_code10,
      b.hsCode10 ? 'CONFIRME_MANUEL' : existing.hs_code_status, b.originCountryIso2 ?? existing.origin_country_iso2,
      b.dutyRate ?? existing.excel_duty_rate_percent, b.weight ?? existing.weight_kg, b.volume ?? existing.volume_m3, req.params.id
    );
  logAudit('import_line', req.params.id, 'UPDATED', b);
  ok(res, db.prepare('SELECT * FROM import_lines WHERE id = ?').get(req.params.id));
});

app.delete('/api/lines/:id', (req, res) => {
  db.prepare('DELETE FROM import_lines WHERE id = ?').run(req.params.id);
  logAudit('import_line', req.params.id, 'DELETED', {});
  ok(res, { deleted: true });
});

// Proposition IA de Code SH (Section 16) — ne modifie jamais automatiquement la ligne
app.post('/api/lines/:id/hs-propose', (req, res) => {
  const line = db.prepare('SELECT * FROM import_lines WHERE id = ?').get(req.params.id);
  if (!line) return fail(res, 404, 'Ligne introuvable.');
  const proposal = ai.proposeHsCode(line.product_reference, line.designation, req.body?.description);
  db.prepare('UPDATE import_lines SET ai_proposed_hs_code10=?, hs_code_status=? WHERE id=?')
    .run(proposal.proposedHsCode10, proposal.status, req.params.id);
  logAudit('import_line', req.params.id, 'AI_HS_PROPOSED', proposal);
  ok(res, proposal);
});

// Décision explicite utilisateur : CONFIRMER / MODIFIER / REFUSER (Section 16 & 39)
app.post('/api/lines/:id/hs-decision', (req, res) => {
  const line = db.prepare('SELECT * FROM import_lines WHERE id = ?').get(req.params.id);
  if (!line) return fail(res, 404, 'Ligne introuvable.');
  const { decision, hsCode10 } = req.body || {};
  if (decision === 'CONFIRMER') {
    db.prepare('UPDATE import_lines SET hs_code10=?, hs_code_status=? WHERE id=?').run(line.ai_proposed_hs_code10, 'CONFIRME_IA', req.params.id);
  } else if (decision === 'MODIFIER') {
    if (!hsCode10) return fail(res, 400, 'Le nouveau Code SH est obligatoire.');
    db.prepare('UPDATE import_lines SET hs_code10=?, hs_code_status=? WHERE id=?').run(hsCode10, 'CONFIRME_MANUEL', req.params.id);
  } else if (decision === 'REFUSER') {
    db.prepare('UPDATE import_lines SET hs_code_status=? WHERE id=?').run('REFUSE_IA', req.params.id);
  } else {
    return fail(res, 400, 'Décision invalide (CONFIRMER, MODIFIER ou REFUSER attendu).');
  }
  logAudit('import_line', req.params.id, 'HS_DECISION_' + decision, req.body);
  ok(res, db.prepare('SELECT * FROM import_lines WHERE id = ?').get(req.params.id));
});

// ---------------------------------------------------------------------------
// FRAIS D'IMPORTATION (Sections 15, 16 — dynamique, 7 méthodes de répartition)
// ---------------------------------------------------------------------------
const FEE_CATALOG = [
  { code: 'FRET_INTERNATIONAL', labelFr: 'Fret international' },
  { code: 'ASSURANCE', labelFr: 'Assurance transport' },
  { code: 'FRAIS_PORTUAIRES', labelFr: 'Frais portuaires' },
  { code: 'THC', labelFr: 'THC (Terminal Handling Charges)' },
  { code: 'MAGASINAGE', labelFr: 'Magasinage' },
  { code: 'DEPOTAGE', labelFr: 'Dépotage conteneur' },
  { code: 'TRANSIT', labelFr: 'Transit / Transitaire' },
  { code: 'TRANSPORT_LOCAL', labelFr: 'Transport local (post-douane)' },
  { code: 'FRAIS_BANCAIRES', labelFr: 'Frais bancaires / Domiciliation' },
  { code: 'INSPECTION', labelFr: 'Inspection / Certification' },
  { code: 'AUTRE', labelFr: 'Autre frais personnalisé' }
];
app.get('/api/fee-catalog', (req, res) => ok(res, FEE_CATALOG));

app.post('/api/imports/:id/fees', (req, res) => {
  const b = req.body || {};
  if (!b.feeName || !(b.amount > 0) || !b.currencyCode || !b.allocationMethod) {
    return fail(res, 400, 'Libellé, montant, devise et méthode de répartition sont obligatoires.');
  }
  const id = uuid();
  db.prepare(`INSERT INTO import_fees
    (id, import_operation_id, fee_name, category_code, amount, currency_code, allocation_method,
     include_in_customs_value, include_in_cost_of_goods, manual_allocations_json, created_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`)
    .run(id, req.params.id, b.feeName, b.categoryCode || 'AUTRE', b.amount, b.currencyCode, b.allocationMethod,
      b.includeInCustomsValue ? 1 : 0, b.includeInCostOfGoods === false ? 0 : 1,
      b.manualAllocations ? JSON.stringify(b.manualAllocations) : null, nowIso());
  logAudit('import_fee', id, 'CREATED', b);
  ok(res, db.prepare('SELECT * FROM import_fees WHERE id = ?').get(id));
});

app.put('/api/fees/:id', (req, res) => {
  const existing = db.prepare('SELECT * FROM import_fees WHERE id = ?').get(req.params.id);
  if (!existing) return fail(res, 404, 'Frais introuvable.');
  const b = req.body || {};
  db.prepare(`UPDATE import_fees SET fee_name=?, category_code=?, amount=?, currency_code=?, allocation_method=?,
      include_in_customs_value=?, include_in_cost_of_goods=?, manual_allocations_json=? WHERE id=?`)
    .run(b.feeName ?? existing.fee_name, b.categoryCode ?? existing.category_code, b.amount ?? existing.amount,
      b.currencyCode ?? existing.currency_code, b.allocationMethod ?? existing.allocation_method,
      b.includeInCustomsValue !== undefined ? (b.includeInCustomsValue ? 1 : 0) : existing.include_in_customs_value,
      b.includeInCostOfGoods !== undefined ? (b.includeInCostOfGoods ? 1 : 0) : existing.include_in_cost_of_goods,
      b.manualAllocations ? JSON.stringify(b.manualAllocations) : existing.manual_allocations_json, req.params.id);
  logAudit('import_fee', req.params.id, 'UPDATED', b);
  ok(res, db.prepare('SELECT * FROM import_fees WHERE id = ?').get(req.params.id));
});

app.delete('/api/fees/:id', (req, res) => {
  db.prepare('DELETE FROM import_fees WHERE id = ?').run(req.params.id);
  logAudit('import_fee', req.params.id, 'DELETED', {});
  ok(res, { deleted: true });
});

// ---------------------------------------------------------------------------
// IMPORT EXCEL / CSV RÉEL (Section 6 — assistant complet)
// ---------------------------------------------------------------------------
app.post('/api/imports/:id/excel/upload', upload.single('file'), (req, res) => {
  const operation = db.prepare('SELECT * FROM import_operations WHERE id = ?').get(req.params.id);
  if (!operation) return fail(res, 404, 'Dossier introuvable.');
  if (!req.file) return fail(res, 400, 'Aucun fichier reçu.');

  let parsed;
  try {
    parsed = excel.parseWorkbookBuffer(req.file.buffer, req.file.originalname);
  } catch (e) {
    return fail(res, 400, `Impossible de lire le fichier : ${e.message}`);
  }

  const signature = excel.computeHeaderSignature(parsed.headers);
  const savedTemplate = excel.findSavedTemplate(operation.company_id, signature);
  const { mapping: autoMapping, unrecognizedColumns } = excel.autoDetectMapping(parsed.headers);
  const effectiveMapping = savedTemplate ? JSON.parse(savedTemplate.mapping_json) : autoMapping;

  logAudit('excel_import', req.params.id, 'FILE_UPLOADED', { filename: req.file.originalname, rowCount: parsed.rows.length });

  ok(res, {
    headers: parsed.headers,
    previewRows: parsed.rows.slice(0, 10),
    totalRows: parsed.rows.length,
    headerSignature: signature,
    templateFound: !!savedTemplate,
    templateSupplierName: savedTemplate ? savedTemplate.supplier_name : null,
    suggestedMapping: effectiveMapping,
    unrecognizedColumns,
    internalFields: excel.INTERNAL_FIELDS,
    // Les lignes brutes sont réencodées en base64 côté client pour l'étape de confirmation (pas de stockage serveur temporaire)
  });
});

app.post('/api/imports/:id/excel/confirm', upload.single('file'), (req, res) => {
  const operation = db.prepare('SELECT * FROM import_operations WHERE id = ?').get(req.params.id);
  if (!operation) return fail(res, 404, 'Dossier introuvable.');
  if (!req.file) return fail(res, 400, 'Aucun fichier reçu.');

  let mapping;
  try { mapping = JSON.parse(req.body.mapping || '{}'); } catch { return fail(res, 400, 'Mapping invalide.'); }
  const saveAsTemplate = req.body.saveAsTemplate === 'true' || req.body.saveAsTemplate === true;
  const supplierName = req.body.supplierName || operation.supplier_name;

  let parsed;
  try {
    parsed = excel.parseWorkbookBuffer(req.file.buffer, req.file.originalname);
  } catch (e) {
    return fail(res, 400, `Impossible de lire le fichier : ${e.message}`);
  }

  const { structured, errors } = excel.buildStructuredRows(parsed.headers, parsed.rows, mapping);

  if (saveAsTemplate) {
    const signature = excel.computeHeaderSignature(parsed.headers);
    excel.saveTemplate(operation.company_id, supplierName, signature, mapping);
  }

  // Rapprochement Catalogue Produits (Section 31 & 32) : une référence déjà connue de l'entreprise
  // complète automatiquement le Code SH / Origine si l'Excel ne les fournit pas ; une référence
  // inconnue est signalée pour proposition d'ajout au catalogue après import.
  const catalogProducts = db.prepare('SELECT * FROM products WHERE company_id = ?').all(operation.company_id);
  const catalogByRef = new Map(catalogProducts.map(p => [p.reference.toUpperCase(), p]));
  const newProductCandidates = [];

  const insertable = structured.filter(r => !r.hasErrors).map(r => {
    const match = catalogByRef.get((r.reference || '').toUpperCase());
    if (match) {
      return { ...r, hsCode: r.hsCode || match.hs_code10, origin: r.origin || match.origin_country_iso2, matchedCatalog: true };
    }
    newProductCandidates.push({ reference: r.reference, designation: r.designation, hsCode10: r.hsCode, originCountryIso2: r.origin, currencyCode: r.currency, defaultUnitPrice: r.unitPrice });
    return { ...r, matchedCatalog: false };
  });

  const startLineNumber = nextLineNumber(req.params.id);
  const insertStmt = db.prepare(`INSERT INTO import_lines
    (id, import_operation_id, line_number, product_reference, designation, quantity, unit_purchase_price, currency_code,
     hs_code10, hs_code_status, ai_proposed_hs_code10, origin_country_iso2, excel_duty_rate_percent, weight_kg, volume_m3, created_at)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`);

  insertable.forEach((r, idx) => {
    insertStmt.run(
      uuid(), req.params.id, startLineNumber + idx, r.reference, r.designation, r.quantity, r.unitPrice,
      r.currency || operation.main_currency_code, r.hsCode || null, r.hsCode ? 'A_CONFIRMER_EXCEL' : 'NON_RENSEIGNE',
      null, r.origin || operation.default_origin_country_iso2 || null, r.dutyRate, r.weight, r.volume, nowIso()
    );
  });

  logAudit('excel_import', req.params.id, 'IMPORTED', { insertedCount: insertable.length, errorCount: errors.length, matchedCatalog: insertable.filter(r => r.matchedCatalog).length, newCandidates: newProductCandidates.length });

  ok(res, { insertedCount: insertable.length, errors, totalRows: structured.length, matchedCatalogCount: insertable.filter(r => r.matchedCatalog).length, newProductCandidates });
});

// Ajout en masse au catalogue produit des références nouvellement rencontrées lors d'un import Excel
app.post('/api/companies/:companyId/products/bulk', (req, res) => {
  const items = Array.isArray(req.body?.items) ? req.body.items : [];
  let created = 0, skipped = 0;
  for (const it of items) {
    if (!it.reference || !it.designation) { skipped++; continue; }
    const exists = db.prepare('SELECT id FROM products WHERE company_id = ? AND reference = ?').get(req.params.companyId, it.reference);
    if (exists) { skipped++; continue; }
    db.prepare(`INSERT INTO products (id, company_id, reference, designation, hs_code10, origin_country_iso2, unit, default_unit_price, currency_code, created_at, updated_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`)
      .run(uuid(), req.params.companyId, it.reference, it.designation, it.hsCode10 || null, it.originCountryIso2 || null, 'U', it.defaultUnitPrice || null, it.currencyCode || null, nowIso(), nowIso());
    created++;
  }
  logAudit('product', null, 'BULK_CREATED_FROM_EXCEL', { created, skipped });
  ok(res, { created, skipped });
});

app.get('/api/companies/:companyId/excel-templates', (req, res) => {
  ok(res, excel.listTemplates(req.params.companyId));
});

// ---------------------------------------------------------------------------
// CALCUL (Sections 13, 14, 17, 18, 19 — moteur complet)
// ---------------------------------------------------------------------------
app.post('/api/imports/:id/calculate', (req, res) => {
  const full = getFullImport(req.params.id);
  if (!full) return fail(res, 404, 'Dossier introuvable.');

  const result = calc.runFullCalculation(full.operation, full.lines, full.fees, full.company);

  const snapshotId = uuid();
  db.prepare(`INSERT INTO calculation_snapshots (id, import_operation_id, executed_at, regulatory_version_used, result_json)
              VALUES (?, ?, ?, ?, ?)`)
    .run(snapshotId, req.params.id, nowIso(), 'VOIR_REGLES_UTILISEES', JSON.stringify(result));

  if (!result.blocked) {
    db.prepare(`UPDATE import_operations SET status='CALCULE', updated_at=? WHERE id=?`).run(nowIso(), req.params.id);
  }

  logAudit('import_operation', req.params.id, 'CALCULATED', { blocked: result.blocked, anomalyCount: result.anomalies.length });
  ok(res, result);
});

app.get('/api/imports/:id/history', (req, res) => {
  const rows = db.prepare('SELECT id, executed_at, regulatory_version_used, result_json FROM calculation_snapshots WHERE import_operation_id = ? ORDER BY executed_at DESC').all(req.params.id);
  ok(res, rows.map(r => {
    const parsed = JSON.parse(r.result_json);
    return {
      id: r.id, executedAt: r.executed_at, blocked: parsed.blocked,
      totalRealCostOfGoodsDzd: parsed.summary ? parsed.summary.totalRealCostOfGoodsDzd : null,
      anomalyCount: (parsed.anomalies || []).length
    };
  }));
});

app.get('/api/audit', (req, res) => {
  const rows = db.prepare('SELECT * FROM audit_log ORDER BY created_at DESC LIMIT 300').all();
  ok(res, rows);
});

// ---------------------------------------------------------------------------
// EXPORTS DYNAMIQUES EXCEL & PDF (Sections 23, 24 — générés à partir du dernier calcul réel)
// ---------------------------------------------------------------------------
app.get('/api/imports/:id/export/excel', (req, res) => {
  const full = getFullImport(req.params.id);
  if (!full) return fail(res, 404, 'Dossier introuvable.');
  const result = full.lastSnapshot ? full.lastSnapshot.result : calc.runFullCalculation(full.operation, full.lines, full.fees, full.company);
  if (result.blocked) return fail(res, 400, "Le calcul n'a pas pu aboutir (anomalies bloquantes). Impossible de générer le rapport.");
  const buffer = report.buildExcelReportBuffer(full.company, full.operation, full.fees, result);
  res.setHeader('Content-Type', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
  res.setHeader('Content-Disposition', `attachment; filename="Rapport_${full.operation.import_number}.xlsx"`);
  res.send(buffer);
});

app.get('/api/imports/:id/export/pdf', async (req, res) => {
  const full = getFullImport(req.params.id);
  if (!full) return fail(res, 404, 'Dossier introuvable.');
  const result = full.lastSnapshot ? full.lastSnapshot.result : calc.runFullCalculation(full.operation, full.lines, full.fees, full.company);
  if (result.blocked) return fail(res, 400, "Le calcul n'a pas pu aboutir (anomalies bloquantes). Impossible de générer le rapport.");
  try {
    const buffer = await report.buildPdfReportBuffer(full.company, full.operation, full.fees, result);
    res.setHeader('Content-Type', 'application/pdf');
    res.setHeader('Content-Disposition', `attachment; filename="Rapport_${full.operation.import_number}.pdf"`);
    res.send(buffer);
  } catch (e) {
    fail(res, 500, `Erreur de génération PDF : ${e.message}`);
  }
});

// ---------------------------------------------------------------------------
// TABLEAU DE BORD
// ---------------------------------------------------------------------------
app.get('/api/companies/:companyId/dashboard', (req, res) => {
  const companyId = req.params.companyId;
  const imports = db.prepare('SELECT * FROM import_operations WHERE company_id = ?').all(companyId);
  let totalCost = 0, alerts = 0;
  const recent = imports.slice(0, 5).map(op => {
    const snap = db.prepare('SELECT * FROM calculation_snapshots WHERE import_operation_id = ? ORDER BY executed_at DESC LIMIT 1').get(op.id);
    let cost = null, anomalyCount = 0;
    if (snap) {
      const parsed = JSON.parse(snap.result_json);
      if (!parsed.blocked) { cost = parsed.summary.totalRealCostOfGoodsDzd; totalCost += cost; }
      anomalyCount = (parsed.anomalies || []).length;
      alerts += anomalyCount;
    }
    return { id: op.id, importNumber: op.import_number, status: op.status, supplierName: op.supplier_name, totalRealCostOfGoodsDzd: cost, anomalyCount };
  });
  ok(res, {
    totalImports: imports.length,
    totalCostAllDzd: report ? Math.round(totalCost * 100) / 100 : totalCost,
    totalAlerts: alerts,
    inProgress: imports.filter(i => i.status === 'BROUILLON').length,
    recentImports: recent
  });
});

// ---------------------------------------------------------------------------
app.listen(PORT, '0.0.0.0', () => {
  console.log(`CIMP — Application complète (backend + persistance SQLite) démarrée sur http://0.0.0.0:${PORT}`);
});
