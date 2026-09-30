'use strict';
/**
 * Couche de persistance CIMP — SQLite embarqué (node:sqlite).
 * Fournit une base de données réelle et persistante (fichier data/cimp.db),
 * qui survit à la fermeture et à la réouverture de l'application (Exigence Section 5 & 31).
 *
 * Aucune donnée métier (taux, règles, entreprises) n'est codée en dur ici : ce fichier ne fait
 * que créer la structure des tables. Les données réelles sont saisies depuis l'interface.
 */
const path = require('path');
const fs = require('fs');
const { DatabaseSync } = require('node:sqlite');
const crypto = require('crypto');

const DATA_DIR = path.join(__dirname, '..', 'data');
if (!fs.existsSync(DATA_DIR)) fs.mkdirSync(DATA_DIR, { recursive: true });
const DB_PATH = path.join(DATA_DIR, 'cimp.db');

const db = new DatabaseSync(DB_PATH);
db.exec('PRAGMA journal_mode = WAL;');
db.exec('PRAGMA foreign_keys = ON;');

db.exec(`
CREATE TABLE IF NOT EXISTS companies (
  id TEXT PRIMARY KEY,
  legal_name TEXT NOT NULL,
  nif TEXT,
  address TEXT,
  activity TEXT,
  is_vat_non_recoverable INTEGER NOT NULL DEFAULT 1,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS products (
  id TEXT PRIMARY KEY,
  company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  reference TEXT NOT NULL,
  designation TEXT NOT NULL,
  hs_code10 TEXT,
  origin_country_iso2 TEXT,
  unit TEXT DEFAULT 'U',
  default_unit_price REAL,
  currency_code TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(company_id, reference)
);

CREATE TABLE IF NOT EXISTS exchange_rates (
  id TEXT PRIMARY KEY,
  currency_code TEXT NOT NULL,
  rate_to_dzd REAL NOT NULL,
  valid_from TEXT NOT NULL,
  valid_to TEXT,
  source TEXT NOT NULL,
  is_official INTEGER NOT NULL DEFAULT 1,
  entered_by TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS regulatory_rules (
  id TEXT PRIMARY KEY,
  tax_code TEXT NOT NULL,
  tax_name_fr TEXT NOT NULL,
  hs_code10 TEXT NOT NULL,
  origin_country_iso2 TEXT,
  regime_code TEXT NOT NULL DEFAULT 'DROIT_COMMUN_4000',
  rate_percent REAL NOT NULL,
  calculation_base TEXT NOT NULL DEFAULT 'VALEUR_DOUANE',
  valid_from TEXT NOT NULL,
  valid_to TEXT,
  legal_source_title TEXT NOT NULL,
  jora_reference TEXT,
  article_reference TEXT,
  status TEXT NOT NULL DEFAULT 'PUBLIEE',
  version_code TEXT NOT NULL,
  superseded_rule_id TEXT,
  entered_by TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS excel_mapping_templates (
  id TEXT PRIMARY KEY,
  company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  supplier_name TEXT NOT NULL,
  header_signature TEXT NOT NULL,
  mapping_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(company_id, header_signature)
);

CREATE TABLE IF NOT EXISTS import_operations (
  id TEXT PRIMARY KEY,
  company_id TEXT NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  import_number TEXT NOT NULL,
  reference_date TEXT NOT NULL,
  supplier_name TEXT NOT NULL,
  export_shipping_country_iso2 TEXT NOT NULL,
  default_origin_country_iso2 TEXT,
  main_currency_code TEXT NOT NULL,
  exchange_rate_mode TEXT NOT NULL DEFAULT 'AUTO',
  manual_exchange_rate REAL,
  incoterm TEXT NOT NULL,
  arrival_port TEXT,
  transport_mode TEXT,
  invoice_number TEXT,
  observations TEXT,
  status TEXT NOT NULL DEFAULT 'BROUILLON',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE(company_id, import_number)
);

CREATE TABLE IF NOT EXISTS import_lines (
  id TEXT PRIMARY KEY,
  import_operation_id TEXT NOT NULL REFERENCES import_operations(id) ON DELETE CASCADE,
  line_number INTEGER NOT NULL,
  product_reference TEXT NOT NULL,
  designation TEXT NOT NULL,
  quantity REAL NOT NULL,
  unit_purchase_price REAL NOT NULL,
  currency_code TEXT NOT NULL,
  hs_code10 TEXT,
  hs_code_status TEXT NOT NULL DEFAULT 'NON_RENSEIGNE',
  ai_proposed_hs_code10 TEXT,
  origin_country_iso2 TEXT,
  excel_duty_rate_percent REAL,
  weight_kg REAL,
  volume_m3 REAL,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS import_fees (
  id TEXT PRIMARY KEY,
  import_operation_id TEXT NOT NULL REFERENCES import_operations(id) ON DELETE CASCADE,
  fee_name TEXT NOT NULL,
  category_code TEXT NOT NULL,
  amount REAL NOT NULL,
  currency_code TEXT NOT NULL,
  allocation_method TEXT NOT NULL,
  include_in_customs_value INTEGER NOT NULL DEFAULT 0,
  include_in_cost_of_goods INTEGER NOT NULL DEFAULT 1,
  manual_allocations_json TEXT,
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS calculation_snapshots (
  id TEXT PRIMARY KEY,
  import_operation_id TEXT NOT NULL REFERENCES import_operations(id) ON DELETE CASCADE,
  executed_at TEXT NOT NULL,
  regulatory_version_used TEXT,
  result_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS audit_log (
  id TEXT PRIMARY KEY,
  entity_type TEXT NOT NULL,
  entity_id TEXT,
  action TEXT NOT NULL,
  details_json TEXT,
  user_name TEXT NOT NULL DEFAULT 'admin',
  created_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS users (
  id TEXT PRIMARY KEY,
  company_id TEXT REFERENCES companies(id) ON DELETE CASCADE,
  full_name TEXT NOT NULL,
  role TEXT NOT NULL DEFAULT 'UTILISATEUR',
  created_at TEXT NOT NULL
);
`);

/** Ajoute une colonne si elle n'existe pas déjà (migration légère, sans perte de données). */
function ensureColumn(table, column, definition) {
  const cols = db.prepare(`PRAGMA table_info(${table})`).all();
  if (!cols.some(c => c.name === column)) {
    db.exec(`ALTER TABLE ${table} ADD COLUMN ${column} ${definition}`);
  }
}
ensureColumn('import_fees', 'manual_allocations_json', 'TEXT');
ensureColumn('import_lines', 'hs_code_status', "TEXT NOT NULL DEFAULT 'NON_RENSEIGNE'");
ensureColumn('import_lines', 'ai_proposed_hs_code10', 'TEXT');
ensureColumn('import_operations', 'exchange_rate_mode', "TEXT NOT NULL DEFAULT 'AUTO'");
ensureColumn('import_operations', 'manual_exchange_rate', 'REAL');

function uuid() {
  return crypto.randomUUID();
}

/** Crée un compte Administrateur par défaut au tout premier démarrage (table users vide). */
function ensureDefaultAdmin() {
  const count = db.prepare('SELECT COUNT(*) as c FROM users').get().c;
  if (count === 0) {
    db.prepare(`INSERT INTO users (id, company_id, full_name, role, created_at) VALUES (?, NULL, ?, 'ADMINISTRATEUR', ?)`)
      .run(uuid(), 'Administrateur Principal', nowIso());
  }
}

function nowIso() {
  return new Date().toISOString();
}

function logAudit(entityType, entityId, action, details) {
  const stmt = db.prepare(
    `INSERT INTO audit_log (id, entity_type, entity_id, action, details_json, user_name, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?)`
  );
  stmt.run(uuid(), entityType, entityId || null, action, JSON.stringify(details || {}), 'admin', nowIso());
}

module.exports = { db, uuid, nowIso, logAudit, DB_PATH, ensureDefaultAdmin };
