'use strict';
/* CIMP — Application front-end (vanilla JS, aucune dépendance externe). */

const state = {
  companies: [],
  users: [],
  currentCompanyId: localStorage.getItem('cimp_company_id') || null,
  currentUserId: localStorage.getItem('cimp_user_id') || null,
  currentView: 'dashboard',
  currentImportId: null,
  importSection: 'header',
  excelUploadState: null // { file, headers, previewRows, mapping, ... }
};

const $main = document.getElementById('main');
const $companySelect = document.getElementById('companySelect');
const $userSelect = document.getElementById('userSelect');

// ---------------------------------------------------------------------------
// UTILITAIRES
// ---------------------------------------------------------------------------
async function api(path, opts = {}) {
  const headers = opts.body instanceof FormData ? {} : { 'Content-Type': 'application/json' };
  if (state.currentUserId) headers['X-User-Id'] = state.currentUserId;
  const res = await fetch(path, {
    method: opts.method || 'GET',
    headers,
    body: opts.body instanceof FormData ? opts.body : (opts.body ? JSON.stringify(opts.body) : undefined)
  });
  const json = await res.json().catch(() => ({ success: false, error: 'Réponse serveur invalide.' }));
  if (!json.success) throw new Error(json.error || 'Erreur inconnue.');
  return json.data;
}

function toast(msg, type = '') {
  const el = document.createElement('div');
  el.className = 'toast ' + type;
  el.textContent = msg;
  document.body.appendChild(el);
  setTimeout(() => el.remove(), 4500);
}

function money(n) {
  if (n === null || n === undefined) return 'N/D';
  return Number(n).toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' DZD';
}

function esc(s) {
  return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function setView(view) {
  state.currentView = view;
  state.currentImportId = null;
  document.querySelectorAll('#mainNav a').forEach(a => a.classList.toggle('active', a.dataset.view === view));
  render();
}

document.querySelectorAll('#mainNav a').forEach(a => a.addEventListener('click', () => setView(a.dataset.view)));

$companySelect.addEventListener('change', () => {
  state.currentCompanyId = $companySelect.value || null;
  localStorage.setItem('cimp_company_id', state.currentCompanyId || '');
  render();
});

$userSelect.addEventListener('change', () => {
  state.currentUserId = $userSelect.value || null;
  localStorage.setItem('cimp_user_id', state.currentUserId || '');
});

async function refreshCompanies() {
  state.companies = await api('/api/companies');
  $companySelect.innerHTML = '<option value="">— Sélectionner —</option>' +
    state.companies.map(c => `<option value="${c.id}" ${c.id === state.currentCompanyId ? 'selected' : ''}>${esc(c.legal_name)}</option>`).join('');
  if (!state.currentCompanyId && state.companies.length) {
    state.currentCompanyId = state.companies[0].id;
    $companySelect.value = state.currentCompanyId;
  }
}

const ROLE_LABELS = { ADMINISTRATEUR: 'Administrateur', UTILISATEUR: 'Utilisateur', CONSULTATION: 'Consultation' };

async function refreshUsers() {
  state.users = await api('/api/users');
  $userSelect.innerHTML = state.users.map(u => `<option value="${u.id}" ${u.id === state.currentUserId ? 'selected' : ''}>${esc(u.full_name)} (${ROLE_LABELS[u.role] || u.role})</option>`).join('');
  if (!state.currentUserId && state.users.length) {
    state.currentUserId = state.users[0].id;
    localStorage.setItem('cimp_user_id', state.currentUserId);
    $userSelect.value = state.currentUserId;
  }
}

function currentUser() {
  return state.users.find(u => u.id === state.currentUserId) || null;
}

function currentCompany() {
  return state.companies.find(c => c.id === state.currentCompanyId) || null;
}

function requireCompanyGuard() {
  if (!state.currentCompanyId) {
    $main.innerHTML = `<div class="card empty-state">
      <h2>Aucune entreprise sélectionnée</h2>
      <p>Créez d'abord une entreprise dans l'onglet <b>Entreprises</b> pour commencer à utiliser CIMP.</p>
      <button onclick="setView('companies')">Créer mon entreprise</button>
    </div>`;
    return true;
  }
  return false;
}

// ---------------------------------------------------------------------------
// VUE : TABLEAU DE BORD
// ---------------------------------------------------------------------------
async function renderDashboard() {
  if (requireCompanyGuard()) return;
  const d = await api(`/api/companies/${state.currentCompanyId}/dashboard`);
  $main.innerHTML = `
    <h1>Tableau de bord</h1>
    <div class="subtitle">${esc(currentCompany()?.legal_name || '')}</div>
    <div class="kpis">
      <div class="kpi"><div class="num">${d.totalImports}</div><div class="lbl">Dossiers d'importation</div></div>
      <div class="kpi"><div class="num">${d.inProgress}</div><div class="lbl">Dossiers en brouillon</div></div>
      <div class="kpi"><div class="num">${money(d.totalCostAllDzd)}</div><div class="lbl">Coût de revient total calculé</div></div>
      <div class="kpi"><div class="num">${d.totalAlerts}</div><div class="lbl">Alertes / anomalies cumulées</div></div>
    </div>
    <div class="card">
      <h2>Dossiers récents</h2>
      ${d.recentImports.length ? `<table><thead><tr><th>N° Dossier</th><th>Fournisseur</th><th>Statut</th><th>Coût de revient</th><th>Anomalies</th><th></th></tr></thead><tbody>
        ${d.recentImports.map(r => `<tr>
          <td>${esc(r.importNumber)}</td><td>${esc(r.supplierName)}</td><td><span class="badge-status">${esc(r.status)}</span></td>
          <td>${r.totalRealCostOfGoodsDzd !== null ? money(r.totalRealCostOfGoodsDzd) : '<span class="muted">Non calculé</span>'}</td>
          <td>${r.anomalyCount}</td>
          <td><button onclick="openImport('${r.id}')">Ouvrir</button></td>
        </tr>`).join('')}
      </tbody></table>` : '<p class="muted">Aucun dossier pour le moment.</p>'}
    </div>
    <button onclick="setView('imports')">Voir tous les dossiers →</button>
  `;
}

// ---------------------------------------------------------------------------
// VUE : ENTREPRISES (CRUD)
// ---------------------------------------------------------------------------
async function renderCompanies() {
  $main.innerHTML = `
    <h1>Entreprises</h1>
    <div class="subtitle">Gestion des entreprises utilisatrices de CIMP (multi-entreprise).</div>
    <div class="card">
      <h2>Créer une nouvelle entreprise</h2>
      <form id="companyForm">
        <div class="grid grid-2">
          <div><label>Raison sociale *</label><input name="legalName" required /></div>
          <div><label>NIF</label><input name="nif" /></div>
          <div><label>Adresse</label><input name="address" /></div>
          <div><label>Activité</label><input name="activity" /></div>
        </div>
        <label><input type="checkbox" name="isVatNonRecoverable" checked style="width:auto;display:inline-block;margin-right:6px;" />TVA d'importation non récupérable (incluse au coût de revient)</label>
        <button type="submit">Créer l'entreprise</button>
      </form>
    </div>
    <div class="card">
      <h2>Entreprises existantes</h2>
      <table><thead><tr><th>Raison sociale</th><th>NIF</th><th>Régime TVA</th><th></th></tr></thead><tbody>
        ${state.companies.map(c => `<tr>
          <td>${esc(c.legal_name)}</td><td>${esc(c.nif || '—')}</td>
          <td>${c.is_vat_non_recoverable ? 'Non récupérable' : 'Récupérable'}</td>
          <td><button class="danger" onclick="deleteCompany('${c.id}')">Supprimer</button></td>
        </tr>`).join('')}
      </tbody></table>
    </div>
  `;
  document.getElementById('companyForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = new FormData(e.target);
    try {
      await api('/api/companies', { method: 'POST', body: {
        legalName: fd.get('legalName'), nif: fd.get('nif'), address: fd.get('address'),
        activity: fd.get('activity'), isVatNonRecoverable: fd.get('isVatNonRecoverable') === 'on'
      }});
      toast('Entreprise créée.', 'success');
      await refreshCompanies();
      render();
    } catch (err) { toast(err.message, 'error'); }
  });
}

async function deleteCompany(id) {
  if (!confirm('Supprimer cette entreprise et toutes ses données (produits, dossiers) ?')) return;
  await api(`/api/companies/${id}`, { method: 'DELETE' });
  if (state.currentCompanyId === id) state.currentCompanyId = null;
  await refreshCompanies();
  render();
}

// ---------------------------------------------------------------------------
// VUE : PRODUITS (CRUD, isolé par entreprise)
// ---------------------------------------------------------------------------
async function renderProducts() {
  if (requireCompanyGuard()) return;
  const products = await api(`/api/companies/${state.currentCompanyId}/products`);
  $main.innerHTML = `
    <h1>Catalogue Produits</h1>
    <div class="subtitle">${esc(currentCompany()?.legal_name)}</div>
    <div class="card">
      <h2>Ajouter un produit</h2>
      <form id="productForm">
        <div class="grid grid-3">
          <div><label>Référence *</label><input name="reference" required /></div>
          <div><label>Désignation *</label><input name="designation" required /></div>
          <div><label>Code SH (10 chiffres)</label><input name="hsCode10" maxlength="14" placeholder="ex: 8708999000" /></div>
          <div><label>Pays d'origine (ISO2)</label><input name="originCountryIso2" maxlength="2" placeholder="CN" /></div>
          <div><label>Unité</label><input name="unit" placeholder="U" /></div>
          <div><label>Prix unitaire par défaut</label><input name="defaultUnitPrice" type="number" step="0.01" /></div>
          <div><label>Devise</label><input name="currencyCode" placeholder="EUR" /></div>
        </div>
        <button type="submit">Ajouter</button>
      </form>
    </div>
    <div class="card">
      <h2>Produits enregistrés (${products.length})</h2>
      ${products.length ? `<table><thead><tr><th>Référence</th><th>Désignation</th><th>Code SH</th><th>Origine</th><th></th></tr></thead><tbody>
        ${products.map(p => `<tr><td>${esc(p.reference)}</td><td>${esc(p.designation)}</td><td>${esc(p.hs_code10 || '—')}</td><td>${esc(p.origin_country_iso2 || '—')}</td>
        <td><button class="danger" onclick="deleteProduct('${p.id}')">Suppr.</button></td></tr>`).join('')}
      </tbody></table>` : '<p class="muted">Aucun produit. Les produits sont aussi créés automatiquement lors de l\'import Excel.</p>'}
    </div>
  `;
  document.getElementById('productForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = new FormData(e.target);
    try {
      await api(`/api/companies/${state.currentCompanyId}/products`, { method: 'POST', body: Object.fromEntries(fd.entries()) });
      toast('Produit ajouté.', 'success');
      render();
    } catch (err) { toast(err.message, 'error'); }
  });
}

async function deleteProduct(id) {
  await api(`/api/products/${id}`, { method: 'DELETE' });
  render();
}

// ---------------------------------------------------------------------------
// VUE : LISTE DES IMPORTATIONS
// ---------------------------------------------------------------------------
async function renderImports() {
  if (requireCompanyGuard()) return;
  const imports = await api(`/api/companies/${state.currentCompanyId}/imports`);
  $main.innerHTML = `
    <h1>Dossiers d'Importation</h1>
    <div class="subtitle">${esc(currentCompany()?.legal_name)}</div>
    <div class="card">
      <h2>Créer un nouveau dossier</h2>
      <form id="importForm">
        <div class="grid grid-3">
          <div><label>Numéro de dossier *</label><input name="importNumber" required placeholder="IMP-2026-0001" /></div>
          <div><label>Date de référence *</label><input name="referenceDate" type="date" required /></div>
          <div><label>Fournisseur *</label><input name="supplierName" required /></div>
          <div><label>Pays d'expédition (ISO2) *</label><input name="exportShippingCountryIso2" maxlength="2" required placeholder="CN" /></div>
          <div><label>Pays d'origine par défaut (ISO2)</label><input name="defaultOriginCountryIso2" maxlength="2" placeholder="CN" /></div>
          <div><label>Devise principale *</label><input name="mainCurrencyCode" required placeholder="EUR" /></div>
          <div><label>Incoterm *</label>
            <select name="incoterm" required><option value="EXW">EXW</option><option value="FOB" selected>FOB</option><option value="CFR">CFR</option></select>
          </div>
          <div><label>Port / frontière</label><input name="arrivalPort" placeholder="Port d'Alger" /></div>
          <div><label>Mode de transport</label><input name="transportMode" placeholder="Maritime" /></div>
          <div><label>N° Facture</label><input name="invoiceNumber" /></div>
        </div>
        <label>Observations</label><textarea name="observations"></textarea>
        <button type="submit">Créer le dossier</button>
      </form>
    </div>
    <div class="card">
      <h2>Dossiers existants (${imports.length})</h2>
      ${imports.length ? `<table><thead><tr><th>N°</th><th>Date</th><th>Fournisseur</th><th>Incoterm</th><th>Statut</th><th></th></tr></thead><tbody>
        ${imports.map(op => `<tr>
          <td>${esc(op.import_number)}</td><td>${esc(op.reference_date)}</td><td>${esc(op.supplier_name)}</td>
          <td>${esc(op.incoterm)}</td><td><span class="badge-status">${esc(op.status)}</span></td>
          <td><button onclick="openImport('${op.id}')">Ouvrir</button> <button class="danger" onclick="deleteImportOp('${op.id}')">Suppr.</button></td>
        </tr>`).join('')}
      </tbody></table>` : '<p class="muted">Aucun dossier pour le moment.</p>'}
    </div>
  `;
  document.getElementById('importForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = new FormData(e.target);
    try {
      const created = await api(`/api/companies/${state.currentCompanyId}/imports`, { method: 'POST', body: Object.fromEntries(fd.entries()) });
      toast('Dossier créé.', 'success');
      openImport(created.id);
    } catch (err) { toast(err.message, 'error'); }
  });
}

async function deleteImportOp(id) {
  if (!confirm('Supprimer ce dossier ?')) return;
  await api(`/api/imports/${id}`, { method: 'DELETE' });
  render();
}

function openImport(id) {
  state.currentView = 'import-detail';
  state.currentImportId = id;
  state.importSection = 'header';
  render();
}

// ---------------------------------------------------------------------------
// VUE : DÉTAIL D'UN DOSSIER (Assistant complet)
// ---------------------------------------------------------------------------
async function renderImportDetail() {
  const full = await api(`/api/imports/${state.currentImportId}`);
  const { operation, lines, fees, company, lastSnapshot } = full;
  const sections = [
    ['header', '1. Informations'], ['lines', '2. Articles'], ['excel', '3. Import Excel'],
    ['fees', '4. Frais'], ['calc', '5. Calcul & Résultats']
  ];

  $main.innerHTML = `
    <div class="flex-between">
      <div><h1>Dossier ${esc(operation.import_number)}</h1><div class="subtitle">${esc(operation.supplier_name)} — ${esc(operation.incoterm)} — <span class="badge-status">${esc(operation.status)}</span></div></div>
      <button class="secondary" onclick="setView('imports')">← Retour à la liste</button>
    </div>
    <div class="wizard-steps">
      ${sections.map(([key, label]) => `<div class="step ${state.importSection === key ? 'active' : ''}" onclick="switchImportSection('${key}')">${label}</div>`).join('')}
    </div>
    <div id="importSectionContent"></div>
  `;

  const content = document.getElementById('importSectionContent');
  if (state.importSection === 'header') content.innerHTML = renderHeaderSection(operation);
  else if (state.importSection === 'lines') content.innerHTML = renderLinesSection(operation, lines);
  else if (state.importSection === 'excel') content.innerHTML = renderExcelSection(operation);
  else if (state.importSection === 'fees') content.innerHTML = renderFeesSection(operation, fees);
  else if (state.importSection === 'calc') content.innerHTML = renderCalcSection(operation, lines, fees, company, lastSnapshot);

  wireImportSectionEvents(operation, lines, fees);
}

function switchImportSection(key) {
  state.importSection = key;
  render();
}

function renderHeaderSection(op) {
  return `
    <div class="card">
      <h2>Informations générales du dossier</h2>
      <form id="headerForm">
        <div class="grid grid-3">
          <div><label>Date de référence</label><input name="referenceDate" type="date" value="${op.reference_date}" /></div>
          <div><label>Fournisseur</label><input name="supplierName" value="${esc(op.supplier_name)}" /></div>
          <div><label>Pays d'expédition (ISO2)</label><input name="exportShippingCountryIso2" maxlength="2" value="${esc(op.export_shipping_country_iso2)}" /></div>
          <div><label>Pays d'origine par défaut (ISO2)</label><input name="defaultOriginCountryIso2" maxlength="2" value="${esc(op.default_origin_country_iso2 || '')}" /></div>
          <div><label>Devise principale</label><input name="mainCurrencyCode" value="${esc(op.main_currency_code)}" /></div>
          <div><label>Incoterm</label>
            <select name="incoterm">
              ${['EXW', 'FOB', 'CFR'].map(v => `<option value="${v}" ${op.incoterm === v ? 'selected' : ''}>${v}</option>`).join('')}
            </select>
          </div>
          <div><label>Port / frontière</label><input name="arrivalPort" value="${esc(op.arrival_port || '')}" /></div>
          <div><label>Mode de transport</label><input name="transportMode" value="${esc(op.transport_mode || '')}" /></div>
          <div><label>N° Facture</label><input name="invoiceNumber" value="${esc(op.invoice_number || '')}" /></div>
        </div>
        <div class="card" style="background:#f8fafc;">
          <h3>Taux de change (Art. 16 decies Code des Douanes)</h3>
          <div class="grid grid-3">
            <div><label>Mode</label>
              <select name="exchangeRateMode">
                <option value="AUTO" ${op.exchange_rate_mode === 'AUTO' ? 'selected' : ''}>Automatique (taux officiel enregistré)</option>
                <option value="MANUEL" ${op.exchange_rate_mode === 'MANUEL' ? 'selected' : ''}>Manuel (saisie utilisateur)</option>
              </select>
            </div>
            <div><label>Taux manuel (si applicable)</label><input name="manualExchangeRate" type="number" step="0.0001" value="${op.manual_exchange_rate ?? ''}" /></div>
          </div>
          <p class="muted">En mode manuel, si le taux diffère du taux officiel enregistré pour la devise, une alerte ⚠️ TAUX MANUEL sera affichée au calcul et conservée dans l'audit.</p>
        </div>
        <label>Observations</label><textarea name="observations">${esc(op.observations || '')}</textarea>
        <button type="submit">Enregistrer les informations</button>
      </form>
    </div>
  `;
}

function renderLinesSection(op, lines) {
  return `
    <div class="card">
      <h2>Ajouter un article manuellement</h2>
      <form id="lineForm">
        <div class="grid grid-4">
          <div><label>Référence *</label><input name="reference" required /></div>
          <div><label>Désignation *</label><input name="designation" required /></div>
          <div><label>Quantité *</label><input name="quantity" type="number" step="0.01" required /></div>
          <div><label>Prix unitaire *</label><input name="unitPrice" type="number" step="0.0001" required /></div>
          <div><label>Devise *</label><input name="currencyCode" value="${esc(op.main_currency_code)}" required /></div>
          <div><label>Code SH (10 chiffres)</label><input name="hsCode10" maxlength="14" /></div>
          <div><label>Pays d'origine (ISO2)</label><input name="originCountryIso2" maxlength="2" value="${esc(op.default_origin_country_iso2 || '')}" /></div>
          <div><label>Taux DD indiqué (%) — optionnel</label><input name="dutyRate" type="number" step="0.01" /></div>
          <div><label>Poids (kg)</label><input name="weight" type="number" step="0.01" /></div>
          <div><label>Volume (m³)</label><input name="volume" type="number" step="0.001" /></div>
        </div>
        <button type="submit">Ajouter l'article</button>
      </form>
    </div>
    <div class="card">
      <h2>Articles du dossier (${lines.length})</h2>
      ${lines.length ? `<table><thead><tr><th>#</th><th>Réf.</th><th>Désignation</th><th>Qté</th><th>PU</th><th>Devise</th><th>Code SH</th><th>Statut SH</th><th>Origine</th><th></th></tr></thead><tbody>
        ${lines.map(l => `<tr>
          <td>${l.line_number}</td><td>${esc(l.product_reference)}</td><td>${esc(l.designation)}</td><td>${l.quantity}</td>
          <td>${l.unit_purchase_price}</td><td>${esc(l.currency_code)}</td>
          <td>${esc(l.hs_code10 || '<i>manquant</i>')}</td>
          <td>${hsStatusBadge(l)}</td>
          <td>${esc(l.origin_country_iso2 || '—')}</td>
          <td>
            <button onclick="proposeHsForLine('${l.id}')">IA Code SH</button>
            <button class="danger" onclick="deleteLine('${l.id}')">Suppr.</button>
          </td>
        </tr>`).join('')}
      </tbody></table>` : `<p class="muted">Aucun article. Ajoutez-en manuellement ci-dessus ou utilisez l'étape "3. Import Excel".</p>`}
    </div>
  `;
}

function hsStatusBadge(l) {
  const map = {
    'NON_RENSEIGNE': ['ERREUR', 'Non renseigné'],
    'A_CONFIRMER_EXCEL': ['AVERTISSEMENT', 'À confirmer (Excel)'],
    'PROPOSE_IA_NON_CONFIRME': ['AVERTISSEMENT', 'Proposé IA — non confirmé'],
    'NON_DETERMINE': ['ERREUR', 'IA : non déterminé'],
    'CONFIRME_IA': ['ok', 'Confirmé (IA validée)'],
    'CONFIRME_MANUEL': ['ok', 'Confirmé manuellement'],
    'REFUSE_IA': ['ERREUR', 'Proposition IA refusée']
  };
  const [cls, label] = map[l.hs_code_status] || ['INFO', l.hs_code_status];
  return `<span class="pill ${cls}">${label}</span>`;
}

async function proposeHsForLine(lineId) {
  try {
    const proposal = await api(`/api/lines/${lineId}/hs-propose`, { method: 'POST', body: {} });
    const msg = proposal.proposedHsCode10 === 'INFORMATION NON DÉTERMINÉE'
      ? `IA : ${proposal.justificationFr}`
      : `PROPOSITION IA : ${proposal.proposedHsCode10} (${proposal.tariffDescriptionFr}) — Confiance ${proposal.confidencePercent}%.\n${proposal.justificationFr}\n\nConfirmer cette proposition ?`;
    if (proposal.proposedHsCode10 !== 'INFORMATION NON DÉTERMINÉE' && confirm(msg)) {
      await api(`/api/lines/${lineId}/hs-decision`, { method: 'POST', body: { decision: 'CONFIRMER' } });
      toast('Code SH confirmé par l\'utilisateur.', 'success');
    } else {
      toast(msg);
    }
  } catch (err) { toast(err.message, 'error'); }
  render();
}

async function deleteLine(id) {
  await api(`/api/lines/${id}`, { method: 'DELETE' });
  render();
}

function renderExcelSection() {
  return `
    <div class="card">
      <h2>Assistant d'import Excel / CSV</h2>
      <p class="muted">Sélectionnez un fichier (.xlsx ou .csv) exporté par votre fournisseur. CIMP détecte automatiquement les colonnes connues et vous demande de préciser les colonnes non reconnues.</p>
      <div class="file-drop" onclick="document.getElementById('excelFileInput').click()">
        📁 Cliquez pour sélectionner un fichier .xlsx ou .csv
      </div>
      <input type="file" id="excelFileInput" accept=".xlsx,.xls,.csv" style="display:none" />
      <div id="excelWizardResult"></div>
    </div>
  `;
}

function renderFeesSection(op, fees) {
  const categories = window.__feeCatalog || [];
  return `
    <div class="card">
      <h2>Ajouter un frais d'importation</h2>
      <form id="feeForm">
        <div class="grid grid-4">
          <div><label>Libellé *</label><input name="feeName" required /></div>
          <div><label>Catégorie</label>
            <select name="categoryCode">${categories.map(c => `<option value="${c.code}">${c.labelFr}</option>`).join('')}</select>
          </div>
          <div><label>Montant *</label><input name="amount" type="number" step="0.01" required /></div>
          <div><label>Devise *</label><input name="currencyCode" value="DZD" required /></div>
          <div><label>Méthode de répartition *</label>
            <select name="allocationMethod">
              <option value="PAR_VALEUR">Par valeur</option>
              <option value="PAR_QUANTITE">Par quantité</option>
              <option value="PAR_POIDS">Par poids</option>
              <option value="PAR_VOLUME">Par volume</option>
              <option value="MONTANT_FIXE">Montant fixe (manuel)</option>
              <option value="POURCENTAGE">Pourcentage (de la valeur)</option>
              <option value="MANUEL">Manuel</option>
            </select>
          </div>
          <div><label><input type="checkbox" name="includeInCustomsValue" style="width:auto;display:inline-block;margin-right:6px;" />Inclus dans la valeur en douane</label></div>
          <div><label><input type="checkbox" name="includeInCostOfGoods" checked style="width:auto;display:inline-block;margin-right:6px;" />Inclus dans le coût de revient</label></div>
        </div>
        <button type="submit">Ajouter le frais</button>
      </form>
    </div>
    <div class="card">
      <h2>Frais du dossier (${fees.length})</h2>
      ${fees.length ? `<table><thead><tr><th>Libellé</th><th>Catégorie</th><th>Montant</th><th>Devise</th><th>Répartition</th><th>Val. Douane</th><th>Coût Revient</th><th></th></tr></thead><tbody>
        ${fees.map(f => `<tr>
          <td>${esc(f.fee_name)}</td><td>${esc(f.category_code)}</td><td>${f.amount}</td><td>${esc(f.currency_code)}</td>
          <td>${esc(f.allocation_method)}</td><td>${f.include_in_customs_value ? 'OUI' : 'NON'}</td><td>${f.include_in_cost_of_goods ? 'OUI' : 'NON'}</td>
          <td><button class="danger" onclick="deleteFee('${f.id}')">Suppr.</button></td>
        </tr>`).join('')}
      </tbody></table>` : '<p class="muted">Aucun frais renseigné (fret, assurance, port, transit, etc.).</p>'}
    </div>
  `;
}

async function deleteFee(id) {
  await api(`/api/fees/${id}`, { method: 'DELETE' });
  render();
}

function severityClass(sev) { return sev; }

function renderCalcSection(op, lines, fees, company, lastSnapshot) {
  const result = lastSnapshot ? lastSnapshot.result : null;
  return `
    <div class="card">
      <h2>Lancer le calcul</h2>
      <p class="muted">Le calcul applique le moteur réglementaire (règles versionnées enregistrées), le moteur de calcul (valeur en douane, droits, taxes, TVA) et le moteur de répartition des frais.</p>
      <button id="runCalcBtn">▶ Calculer le dossier</button>
      ${result ? `<span class="muted"> Dernier calcul : ${new Date(lastSnapshot.executed_at).toLocaleString('fr-FR')}</span>` : ''}
    </div>
    <div id="calcResultZone">${result ? renderCalcResult(result, op, company) : '<p class="muted">Aucun calcul effectué pour l\'instant.</p>'}</div>
  `;
}

function renderCalcResult(result, op, company) {
  if (result.blocked) {
    return `<div class="card">
      <h2>❌ Calcul bloqué</h2>
      ${result.anomalies.map(a => `<div class="pill ${a.severity}">${a.severity}</div> ${esc(a.message)}<br/>`).join('')}
    </div>`;
  }
  const s = result.summary;
  return `
    <div class="card">
      <div class="flex-between"><h2>Résumé financier</h2>
        <div class="actions-row">
          <a class="btn" href="/api/imports/${op.id}/export/excel">⬇ Export Excel</a>
          <a class="btn secondary" href="/api/imports/${op.id}/export/pdf">⬇ Export PDF</a>
        </div>
      </div>
      <div class="kpis">
        <div class="kpi"><div class="num">${money(s.totalPurchaseValueDzd)}</div><div class="lbl">Valeur fournisseur</div></div>
        <div class="kpi"><div class="num">${money(s.totalCustomsValueDzd)}</div><div class="lbl">Valeur en douane</div></div>
        <div class="kpi"><div class="num">${money(s.totalDutiesAndTaxesDzd)}</div><div class="lbl">Total droits & taxes</div></div>
        <div class="kpi"><div class="num">${money(s.totalRealCostOfGoodsDzd)}</div><div class="lbl">Coût de revient total</div></div>
      </div>
      <table><tbody>
        <tr><td>Total frais d'importation</td><td>${money(s.totalImportFeesDzd)}</td></tr>
        <tr><td>Total droits de douane</td><td>${money(s.totalCustomsDutyDzd)}</td></tr>
        <tr><td>Total autres taxes (DAPS/TIC…)</td><td>${money(s.totalAdditionalTaxesDzd)}</td></tr>
        <tr><td>Total TVA à l'importation</td><td>${money(s.totalImportVatDzd)}</td></tr>
        <tr><td>Coût d'acquisition hors TVA</td><td>${money(s.totalAcquisitionCostExVatDzd)}</td></tr>
        <tr><td>TVA non récupérable ?</td><td>${company.is_vat_non_recoverable ? 'Oui (incluse au coût)' : 'Non (récupérable)'}</td></tr>
      </tbody></table>
    </div>
    <div class="card">
      <h2>Détail par article</h2>
      <table><thead><tr><th>Réf.</th><th>Désignation</th><th>Qté</th><th>Val. Douane</th><th>DD</th><th>Taxes</th><th>TVA</th><th>Coût total</th><th>Coût unitaire</th></tr></thead><tbody>
        ${s.lineResults.map(l => `<tr>
          <td>${esc(l.productReference)}</td><td>${esc(l.designation)}</td><td>${l.quantity}</td>
          <td>${money(l.customsOutcome.customsValueDzd)}</td>
          <td>${money(l.customsOutcome.customsDutyAmountDzd)} (${l.customsOutcome.customsDutyRatePercent ?? 'N/D'}%)</td>
          <td>${money(l.customsOutcome.totalAdditionalTaxesDzd)}</td>
          <td>${money(l.customsOutcome.importVatAmountDzd)} (${l.customsOutcome.vatRatePercent ?? 'N/D'}%)</td>
          <td>${money(l.economicOutcome.realCostOfGoodsTotalDzd)}</td>
          <td><b>${money(l.economicOutcome.unitCostOfGoodsDzd)}</b></td>
        </tr>`).join('')}
      </tbody></table>
    </div>
    <div class="card">
      <h2>Anomalies et alertes (${result.anomalies.length})</h2>
      ${result.anomalies.length ? result.anomalies.map(a => `<div style="margin-bottom:6px;"><span class="pill ${a.severity}">${a.severity}</span> ${esc(a.message)}</div>`).join('') : '<p class="muted">Aucune anomalie détectée.</p>'}
    </div>
    <div class="card"><p class="muted"><b>Mention légale :</b> ${esc(s.mandatoryLegalDisclaimerFr)}</p></div>
  `;
}

function wireImportSectionEvents(op, lines, fees) {
  const headerForm = document.getElementById('headerForm');
  if (headerForm) headerForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = Object.fromEntries(new FormData(e.target).entries());
    try { await api(`/api/imports/${op.id}`, { method: 'PUT', body: fd }); toast('Informations enregistrées.', 'success'); render(); }
    catch (err) { toast(err.message, 'error'); }
  });

  const lineForm = document.getElementById('lineForm');
  if (lineForm) lineForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = Object.fromEntries(new FormData(e.target).entries());
    try { await api(`/api/imports/${op.id}/lines`, { method: 'POST', body: fd }); toast('Article ajouté.', 'success'); render(); }
    catch (err) { toast(err.message, 'error'); }
  });

  const feeForm = document.getElementById('feeForm');
  if (feeForm) feeForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = new FormData(e.target);
    const body = {
      feeName: fd.get('feeName'), categoryCode: fd.get('categoryCode'), amount: Number(fd.get('amount')),
      currencyCode: fd.get('currencyCode'), allocationMethod: fd.get('allocationMethod'),
      includeInCustomsValue: fd.get('includeInCustomsValue') === 'on', includeInCostOfGoods: fd.get('includeInCostOfGoods') === 'on'
    };
    try { await api(`/api/imports/${op.id}/fees`, { method: 'POST', body }); toast('Frais ajouté.', 'success'); render(); }
    catch (err) { toast(err.message, 'error'); }
  });

  const runCalcBtn = document.getElementById('runCalcBtn');
  if (runCalcBtn) runCalcBtn.addEventListener('click', async () => {
    runCalcBtn.disabled = true; runCalcBtn.textContent = 'Calcul en cours…';
    try {
      const result = await api(`/api/imports/${op.id}/calculate`, { method: 'POST', body: {} });
      document.getElementById('calcResultZone').innerHTML = renderCalcResult(result, op, (await api(`/api/imports/${op.id}`)).company);
      toast(result.blocked ? 'Calcul bloqué : voir anomalies.' : 'Calcul terminé.', result.blocked ? 'error' : 'success');
    } catch (err) { toast(err.message, 'error'); }
    runCalcBtn.disabled = false; runCalcBtn.textContent = '▶ Calculer le dossier';
  });

  const excelInput = document.getElementById('excelFileInput');
  if (excelInput) excelInput.addEventListener('change', (e) => handleExcelUpload(e, op));
}

// -------- Assistant Import Excel (multi-étapes) --------
async function handleExcelUpload(e, op) {
  const file = e.target.files[0];
  if (!file) return;
  const zone = document.getElementById('excelWizardResult');
  zone.innerHTML = '<p class="muted">Analyse du fichier…</p>';
  const fd = new FormData();
  fd.append('file', file);
  try {
    const analysis = await api(`/api/imports/${op.id}/excel/upload`, { method: 'POST', body: fd });
    state.excelUploadState = { file, analysis };
    renderExcelMappingStep(op);
  } catch (err) { zone.innerHTML = `<p class="pill ERREUR">${esc(err.message)}</p>`; }
}

function renderExcelMappingStep(op) {
  const { analysis } = state.excelUploadState;
  const zone = document.getElementById('excelWizardResult');
  const fieldOptions = (selectedIdx) => `<option value="">— Ignorer —</option>` +
    analysis.headers.map((h, i) => `<option value="${i}" ${Number(selectedIdx) === i ? 'selected' : ''}>${esc(h || '(colonne ' + (i + 1) + ')')}</option>`).join('');

  zone.innerHTML = `
    <div class="card" style="background:#f8fafc;">
      <h3>${analysis.templateFound ? `✅ Modèle de mapping retrouvé pour ce fournisseur (${esc(analysis.templateSupplierName)})` : '🆕 Nouveau modèle de fichier détecté'}</h3>
      <p class="muted">${analysis.totalRows} ligne(s) détectée(s). Aperçu des ${analysis.previewRows.length} premières lignes :</p>
      <div style="overflow-x:auto;"><table><thead><tr>${analysis.headers.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>
        ${analysis.previewRows.map(r => `<tr>${analysis.headers.map((_, i) => `<td>${esc(r[i] ?? '')}</td>`).join('')}</tr>`).join('')}
      </tbody></table></div>
    </div>
    <div class="card">
      <h3>Confirmez la correspondance des colonnes (mapping)</h3>
      ${analysis.unrecognizedColumns.length ? `<p class="pill AVERTISSEMENT">Colonnes non reconnues automatiquement : ${analysis.unrecognizedColumns.map(c => esc(c.header)).join(', ')} — veuillez préciser leur signification ci-dessous.</p>` : ''}
      <form id="mappingForm">
        <div class="grid grid-3">
          ${analysis.internalFields.map(f => `<div>
            <label>${esc(f.labelFr)}${f.required ? ' *' : ''}</label>
            <select name="map_${f.key}">${fieldOptions(analysis.suggestedMapping[f.key])}</select>
          </div>`).join('')}
        </div>
        <div><label>Nom du fournisseur (pour ce modèle)</label><input name="supplierName" value="${esc(op.supplier_name)}" /></div>
        <label><input type="checkbox" name="saveAsTemplate" checked style="width:auto;display:inline-block;margin-right:6px;" />Enregistrer ce mapping comme modèle pour ce fournisseur (réutilisation automatique au prochain import)</label>
        <button type="submit">Valider le mapping et importer les lignes</button>
      </form>
    </div>
    <div id="excelImportOutcome"></div>
  `;

  document.getElementById('mappingForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = new FormData(e.target);
    const mapping = {};
    analysis.internalFields.forEach(f => {
      const v = fd.get('map_' + f.key);
      if (v !== '' && v !== null) mapping[f.key] = Number(v);
    });
    const missing = analysis.internalFields.filter(f => f.required && mapping[f.key] === undefined);
    if (missing.length) { toast('Champs obligatoires non mappés : ' + missing.map(f => f.labelFr).join(', '), 'error'); return; }

    const uploadFd = new FormData();
    uploadFd.append('file', state.excelUploadState.file);
    uploadFd.append('mapping', JSON.stringify(mapping));
    uploadFd.append('supplierName', fd.get('supplierName'));
    uploadFd.append('saveAsTemplate', fd.get('saveAsTemplate') === 'on');
    try {
      const outcome = await api(`/api/imports/${op.id}/excel/confirm`, { method: 'POST', body: uploadFd });
      state.__lastExcelOutcome = { outcome, companyId: op.company_id };
      document.getElementById('excelImportOutcome').innerHTML = `
        <div class="card">
          <h3>Résultat de l'import</h3>
          <p><span class="pill ok">${outcome.insertedCount} ligne(s) importée(s)</span>
             <span class="pill INFO">${outcome.matchedCatalogCount} rapprochée(s) du catalogue produit</span>
             ${outcome.errors.length ? `<span class="pill ERREUR">${outcome.errors.length} ligne(s) en erreur</span>` : ''}</p>
          ${outcome.errors.length ? `<ul>${outcome.errors.map(e => `<li>Ligne ${e.rowIndex} : ${e.errors.join(', ')}</li>`).join('')}</ul>` : ''}
          ${outcome.newProductCandidates.length ? `
            <div class="card" style="background:#f8fafc;">
              <p>${outcome.newProductCandidates.length} nouvelle(s) référence(s) absente(s) du catalogue produit :
                 ${outcome.newProductCandidates.map(p => esc(p.reference)).join(', ')}</p>
              <button onclick="addCandidatesToCatalog()">Ajouter ces références au catalogue produit</button>
            </div>` : ''}
          <button onclick="switchImportSection('lines')">Voir les articles importés →</button>
        </div>`;
      toast('Import Excel terminé.', 'success');
    } catch (err) { toast(err.message, 'error'); }
  });
}

async function addCandidatesToCatalog() {
  const ctx = state.__lastExcelOutcome;
  if (!ctx) return;
  try {
    const result = await api(`/api/companies/${ctx.companyId}/products/bulk`, { method: 'POST', body: { items: ctx.outcome.newProductCandidates } });
    toast(`${result.created} produit(s) ajouté(s) au catalogue (${result.skipped} ignoré(s)).`, 'success');
  } catch (err) { toast(err.message, 'error'); }
}

// ---------------------------------------------------------------------------
// VUE : RÉGLEMENTATION (Section 12 — jamais de taux en dur, saisie contrôlée)
// ---------------------------------------------------------------------------
const SUGGESTED_RULES = [
  { taxCode: 'TVA', taxNameFr: 'TVA à l\'importation (taux normal)', hsCode10: '', ratePercent: 19, calculationBase: 'VALEUR_DOUANE_PLUS_DD', legalSourceTitle: 'Code des Taxes sur le Chiffre d\'Affaires (CTCA)', articleReference: 'Art. 21', joraReference: 'Ordonnance n° 76-102' },
  { taxCode: 'TVA', taxNameFr: 'TVA à l\'importation (taux réduit)', hsCode10: '', ratePercent: 9, calculationBase: 'VALEUR_DOUANE_PLUS_DD', legalSourceTitle: 'Code des Taxes sur le Chiffre d\'Affaires (CTCA)', articleReference: 'Art. 23', joraReference: 'Ordonnance n° 76-102' }
];

async function renderRegulatory() {
  const rules = await api('/api/regulatory/rules');
  $main.innerHTML = `
    <h1>Réglementation Douanière & Fiscale</h1>
    <div class="subtitle">Règles versionnées, jamais écrasées. Aucune règle n'est inventée : toute donnée manquante doit être saisie ici avec sa source officielle.</div>
    <div class="card">
      <h2>Ajouter / publier une nouvelle règle</h2>
      <p class="muted">Suggestions de référence (loi générale, à vérifier et publier vous-même) :
        ${SUGGESTED_RULES.map((r, i) => `<button type="button" class="outline" onclick='prefillRule(${i})'>${esc(r.taxNameFr)} — ${r.ratePercent}%</button>`).join(' ')}
      </p>
      <form id="ruleForm">
        <div class="grid grid-3">
          <div><label>Code taxe * (DD, TVA, DAPS, TIC…)</label><input name="taxCode" required /></div>
          <div><label>Nom de la taxe</label><input name="taxNameFr" /></div>
          <div><label>Code SH (10 chiffres) *</label><input name="hsCode10" required maxlength="14" /></div>
          <div><label>Pays d'origine (ISO2, vide = tous)</label><input name="originCountryIso2" maxlength="2" /></div>
          <div><label>Régime</label><input name="regimeCode" value="DROIT_COMMUN_4000" /></div>
          <div><label>Taux (%) *</label><input name="ratePercent" type="number" step="0.01" required /></div>
          <div><label>Base de calcul</label>
            <select name="calculationBase">
              <option value="VALEUR_DOUANE">Valeur en douane</option>
              <option value="VALEUR_DOUANE_PLUS_DD">Valeur en douane + Droit de douane</option>
            </select>
          </div>
          <div><label>Date d'effet *</label><input name="validFrom" type="date" required /></div>
          <div><label>Code de version</label><input name="versionCode" placeholder="V-2026.01" /></div>
        </div>
        <div><label>Source juridique officielle * (ex: Code des Douanes Algérien)</label><input name="legalSourceTitle" required /></div>
        <div class="grid grid-2">
          <div><label>Référence Article</label><input name="articleReference" placeholder="Art. 16 ter" /></div>
          <div><label>Référence JORA</label><input name="joraReference" placeholder="JORA n° 11" /></div>
        </div>
        <button type="submit">Publier cette règle (nouvelle version)</button>
      </form>
    </div>
    <div class="card">
      <h2>Règles publiées (${rules.length})</h2>
      ${rules.length ? `<table><thead><tr><th>Taxe</th><th>Code SH</th><th>Origine</th><th>Taux</th><th>Validité</th><th>Source</th><th>Version</th><th></th></tr></thead><tbody>
        ${rules.map(r => `<tr>
          <td>${esc(r.tax_code)} — ${esc(r.tax_name_fr)}</td><td>${esc(r.hs_code10)}</td><td>${esc(r.origin_country_iso2 || 'Tous')}</td>
          <td>${r.rate_percent}%</td><td>${esc(r.valid_from)} → ${esc(r.valid_to || 'en cours')}</td>
          <td>${esc(r.legal_source_title)} ${esc(r.article_reference || '')} (${esc(r.jora_reference || '')})</td>
          <td>${esc(r.version_code)}</td>
          <td><button class="outline" onclick="showRuleHistory('${esc(r.hs_code10)}','${esc(r.tax_code)}')">Historique</button></td>
        </tr>`).join('')}
      </tbody></table>` : '<p class="muted">Aucune règle publiée. Le calcul retournera "INFORMATION NON DÉTERMINÉE" tant qu\'aucune règle n\'est ajoutée pour un Code SH donné.</p>'}
    </div>
    <div id="ruleHistoryZone"></div>
  `;
  document.getElementById('ruleForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const body = Object.fromEntries(new FormData(e.target).entries());
    try { await api('/api/regulatory/rules', { method: 'POST', body }); toast('Règle publiée.', 'success'); render(); }
    catch (err) { toast(err.message, 'error'); }
  });
}

async function showRuleHistory(hsCode10, taxCode) {
  const zone = document.getElementById('ruleHistoryZone');
  zone.innerHTML = '<p class="muted">Chargement de l\'historique…</p>';
  try {
    const h = await api(`/api/regulatory/rules/history/${encodeURIComponent(hsCode10)}/${encodeURIComponent(taxCode)}`);
    zone.innerHTML = `
      <div class="card">
        <h3>Historique des versions — ${esc(taxCode)} / SH ${esc(hsCode10)}</h3>
        ${h.diffs.length ? `<table><thead><tr><th>Ancienne version</th><th>Ancien taux</th><th>Nouvelle version</th><th>Nouveau taux</th><th>Écart</th><th>Effet</th><th>Source</th></tr></thead><tbody>
          ${h.diffs.map(d => `<tr><td>${esc(d.oldVersion)}</td><td>${d.oldRate}%</td><td>${esc(d.newVersion)}</td><td>${d.newRate}%</td>
            <td>${d.deltaPercentagePoints > 0 ? '+' : ''}${d.deltaPercentagePoints} pts</td><td>${esc(d.newValidFrom)}</td><td>${esc(d.legalSource)}</td></tr>`).join('')}
        </tbody></table>` : `<p class="muted">Une seule version publiée (aucun changement historique) : ${h.versions.length ? h.versions[0].rate_percent + '% depuis le ' + h.versions[0].valid_from : 'aucune donnée'}.</p>`}
      </div>`;
  } catch (err) { zone.innerHTML = `<p class="pill ERREUR">${esc(err.message)}</p>`; }
}

function prefillRule(i) {
  const r = SUGGESTED_RULES[i];
  const f = document.getElementById('ruleForm');
  f.taxCode.value = r.taxCode; f.taxNameFr.value = r.taxNameFr; f.ratePercent.value = r.ratePercent;
  f.calculationBase.value = r.calculationBase; f.legalSourceTitle.value = r.legalSourceTitle;
  f.articleReference.value = r.articleReference; f.joraReference.value = r.joraReference;
  f.hsCode10.focus();
  toast('Formulaire pré-rempli. Complétez le Code SH concerné et la date d\'effet avant de publier.');
}

// ---------------------------------------------------------------------------
// VUE : TAUX DE CHANGE
// ---------------------------------------------------------------------------
async function renderRates() {
  const rates = await api('/api/exchange-rates');
  $main.innerHTML = `
    <h1>Taux de Change Officiels</h1>
    <div class="subtitle">Conformément à l'Art. 16 decies du Code des Douanes Algérien. Source recommandée : Banque d'Algérie / ALCES.</div>
    <div class="card">
      <h2>Publier un taux officiel</h2>
      <form id="rateForm">
        <div class="grid grid-3">
          <div><label>Devise (ISO3) *</label><input name="currencyCode" required placeholder="EUR" /></div>
          <div><label>Taux vers DZD *</label><input name="rateToDzd" type="number" step="0.0001" required /></div>
          <div><label>Date d'effet *</label><input name="validFrom" type="date" required /></div>
        </div>
        <div><label>Source</label><input name="source" value="Banque d'Algérie / ALCES" /></div>
        <button type="submit">Publier</button>
      </form>
    </div>
    <div class="card">
      <h2>Historique (${rates.length})</h2>
      ${rates.length ? `<table><thead><tr><th>Devise</th><th>Taux</th><th>Validité</th><th>Source</th></tr></thead><tbody>
        ${rates.map(r => `<tr><td>${esc(r.currency_code)}</td><td>${r.rate_to_dzd}</td><td>${esc(r.valid_from)} → ${esc(r.valid_to || 'en cours')}</td><td>${esc(r.source)}</td></tr>`).join('')}
      </tbody></table>` : '<p class="muted">Aucun taux enregistré.</p>'}
    </div>
  `;
  document.getElementById('rateForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const body = Object.fromEntries(new FormData(e.target).entries());
    try { await api('/api/exchange-rates', { method: 'POST', body }); toast('Taux publié.', 'success'); render(); }
    catch (err) { toast(err.message, 'error'); }
  });
}

// ---------------------------------------------------------------------------
// VUE : UTILISATEURS & RÔLES (Sections 21, 39)
// ---------------------------------------------------------------------------
async function renderUsers() {
  $main.innerHTML = `
    <h1>Utilisateurs & Rôles</h1>
    <div class="subtitle">Seul un utilisateur au rôle <b>Administrateur</b> peut publier une règle réglementaire ou un taux de change officiel.</div>
    <div class="card">
      <h2>Ajouter un utilisateur</h2>
      <form id="userForm">
        <div class="grid grid-3">
          <div><label>Nom complet *</label><input name="fullName" required /></div>
          <div><label>Rôle</label>
            <select name="role">
              <option value="ADMINISTRATEUR">Administrateur</option>
              <option value="UTILISATEUR" selected>Utilisateur</option>
              <option value="CONSULTATION">Consultation</option>
            </select>
          </div>
        </div>
        <button type="submit">Créer l'utilisateur</button>
      </form>
    </div>
    <div class="card">
      <h2>Utilisateurs (${state.users.length})</h2>
      <table><thead><tr><th>Nom</th><th>Rôle</th><th></th></tr></thead><tbody>
        ${state.users.map(u => `<tr>
          <td>${esc(u.full_name)} ${u.id === state.currentUserId ? '<span class="pill ok">actif</span>' : ''}</td>
          <td><span class="badge-status">${ROLE_LABELS[u.role] || u.role}</span></td>
          <td><button class="danger" onclick="deleteUser('${u.id}')">Suppr.</button></td>
        </tr>`).join('')}
      </tbody></table>
    </div>
  `;
  document.getElementById('userForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const fd = Object.fromEntries(new FormData(e.target).entries());
    try { await api('/api/users', { method: 'POST', body: fd }); toast('Utilisateur créé.', 'success'); await refreshUsers(); render(); }
    catch (err) { toast(err.message, 'error'); }
  });
}

async function deleteUser(id) {
  try { await api(`/api/users/${id}`, { method: 'DELETE' }); await refreshUsers(); render(); }
  catch (err) { toast(err.message, 'error'); }
}

// ---------------------------------------------------------------------------
// VUE : JOURNAL D'AUDIT
// ---------------------------------------------------------------------------
async function renderAudit() {
  const logs = await api('/api/audit');
  $main.innerHTML = `
    <h1>Journal d'Audit</h1>
    <div class="subtitle">Traçabilité complète de toutes les actions et calculs (Section 25).</div>
    <div class="card">
      <table><thead><tr><th>Date</th><th>Entité</th><th>Action</th><th>Détails</th></tr></thead><tbody>
        ${logs.map(l => `<tr><td>${new Date(l.created_at).toLocaleString('fr-FR')}</td><td>${esc(l.entity_type)}</td><td>${esc(l.action)}</td><td><code style="font-size:0.7rem;">${esc((l.details_json || '').slice(0, 200))}</code></td></tr>`).join('')}
      </tbody></table>
    </div>
  `;
}

// ---------------------------------------------------------------------------
// ROUTAGE PRINCIPAL
// ---------------------------------------------------------------------------
async function render() {
  try {
    if (state.currentView === 'dashboard') return renderDashboard();
    if (state.currentView === 'companies') return renderCompanies();
    if (state.currentView === 'products') return renderProducts();
    if (state.currentView === 'imports') return renderImports();
    if (state.currentView === 'import-detail') return renderImportDetail();
    if (state.currentView === 'regulatory') return renderRegulatory();
    if (state.currentView === 'rates') return renderRates();
    if (state.currentView === 'users') return renderUsers();
    if (state.currentView === 'audit') return renderAudit();
  } catch (err) {
    $main.innerHTML = `<div class="card"><p class="pill ERREUR">Erreur : ${esc(err.message)}</p></div>`;
  }
}

(async function init() {
  window.__feeCatalog = await api('/api/fee-catalog');
  await refreshUsers();
  await refreshCompanies();
  document.querySelector('#mainNav a[data-view="dashboard"]').classList.add('active');
  render();
})();
