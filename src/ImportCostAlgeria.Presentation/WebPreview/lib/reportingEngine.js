'use strict';
/**
 * Moteur de Reporting CIMP (port fonctionnel de ImportCostAlgeria.Reporting).
 * Génère de VRAIS fichiers .xlsx (5 feuilles) et .pdf à partir des données réellement
 * calculées du dossier sélectionné (aucun fichier statique d'exemple).
 */
const XLSX = require('xlsx');
const PDFDocument = require('pdfkit');

function buildExcelReportBuffer(company, operation, fees, calcResult) {
  const wb = XLSX.utils.book_new();
  const s = calcResult.summary;

  // Feuille 1 : DETAIL_ARTICLES
  const detailHeader = ['Ligne', 'Référence', 'Désignation', 'Qté', 'Prix Unitaire', 'Devise', 'Taux Change',
    'Valeur Achat DZD', 'Code SH', 'Origine', 'Part Frais Douane DZD', 'Valeur Douanière DZD', 'Droit Douane %',
    'Droit Douane DZD', 'Autres Taxes DZD', 'TVA %', 'TVA DZD', 'Autres Frais Locaux DZD', 'Total Frais DZD',
    'Coût Total Revient DZD', 'Coût Unitaire DZD'];
  const detailRows = s.lineResults.map(l => ([
    l.lineNumber, l.productReference, l.designation, l.quantity, null, l.currencyCode, l.appliedExchangeRateToDzd,
    l.purchaseValueDzd, l.hsCode10, l.originCountryIso2, l.economicOutcome.allocatedCustomsIncludedFeesDzd,
    l.customsOutcome.customsValueDzd, l.customsOutcome.customsDutyRatePercent, l.customsOutcome.customsDutyAmountDzd,
    l.customsOutcome.totalAdditionalTaxesDzd, l.customsOutcome.vatRatePercent, l.customsOutcome.importVatAmountDzd,
    l.economicOutcome.allocatedLocalAndPostCustomsFeesDzd, l.economicOutcome.totalAllocatedFeesDzd,
    l.economicOutcome.realCostOfGoodsTotalDzd, l.economicOutcome.unitCostOfGoodsDzd
  ]));
  const wsDetail = XLSX.utils.aoa_to_sheet([detailHeader, ...detailRows]);
  XLSX.utils.book_append_sheet(wb, wsDetail, 'DETAIL_ARTICLES');

  // Feuille 2 : RECAPITULATIF
  const recapRows = [
    ['RÉCAPITULATIF — Dossier', operation.import_number],
    ['Entreprise', company.legal_name],
    ['Fournisseur', operation.supplier_name],
    ['Incoterm', operation.incoterm],
    ['Devise', operation.main_currency_code],
    ['Taux de change appliqué', calcResult.exchangeRateUsed],
    [],
    ['Valeur fournisseur (achat) DZD', s.totalPurchaseValueDzd],
    ['Total frais d\'importation DZD', s.totalImportFeesDzd],
    ['Valeur en douane totale DZD', s.totalCustomsValueDzd],
    ['Total droits de douane DZD', s.totalCustomsDutyDzd],
    ['Total autres taxes (DAPS/TIC…) DZD', s.totalAdditionalTaxesDzd],
    ['Total TVA import DZD', s.totalImportVatDzd],
    ['TOTAL DROITS ET TAXES DZD', s.totalDutiesAndTaxesDzd],
    ["Coût d'acquisition hors TVA DZD", s.totalAcquisitionCostExVatDzd],
    ['TVA non récupérable', company.is_vat_non_recoverable ? 'OUI (incluse au coût)' : 'NON (récupérable)'],
    ['COÛT TOTAL DE REVIENT RÉEL DZD', s.totalRealCostOfGoodsDzd],
    [],
    ['Mention légale', s.mandatoryLegalDisclaimerFr]
  ];
  const wsRecap = XLSX.utils.aoa_to_sheet(recapRows);
  XLSX.utils.book_append_sheet(wb, wsRecap, 'RECAPITULATIF');

  // Feuille 3 : FRAIS
  const feeHeader = ['Libellé', 'Catégorie', 'Montant', 'Devise', 'Méthode de répartition', 'Inclus Valeur Douanière', 'Inclus Coût de Revient'];
  const feeRows = fees.map(f => [f.fee_name, f.category_code, f.amount, f.currency_code, f.allocation_method,
    f.include_in_customs_value ? 'OUI' : 'NON', f.include_in_cost_of_goods ? 'OUI' : 'NON']);
  const wsFees = XLSX.utils.aoa_to_sheet([feeHeader, ...feeRows]);
  XLSX.utils.book_append_sheet(wb, wsFees, 'FRAIS');

  // Feuille 4 : TAXES
  const taxHeader = ['Ligne', 'Référence', 'Code SH', 'Code Taxe', 'Nom Taxe', 'Base Imposable DZD', 'Taux %', 'Montant DZD', 'Source Juridique'];
  const taxRows = [];
  s.lineResults.forEach(l => {
    taxRows.push([l.lineNumber, l.productReference, l.hsCode10, 'DD', 'Droit de Douane', l.customsOutcome.customsValueDzd, l.customsOutcome.customsDutyRatePercent, l.customsOutcome.customsDutyAmountDzd, l.customsOutcome.ddSource || '']);
    l.customsOutcome.additionalTaxes.forEach(t => {
      taxRows.push([l.lineNumber, l.productReference, l.hsCode10, t.taxCode, t.taxNameFr, t.taxableBaseDzd, t.ratePercent, t.taxAmountDzd, `${t.legalArticleReference} (${t.joraReference})`]);
    });
    taxRows.push([l.lineNumber, l.productReference, l.hsCode10, 'TVA', 'TVA à l\'importation', l.customsOutcome.vatTaxableBaseDzd, l.customsOutcome.vatRatePercent, l.customsOutcome.importVatAmountDzd, 'CTCA Art. 19, 21 & 23']);
  });
  const wsTaxes = XLSX.utils.aoa_to_sheet([taxHeader, ...taxRows]);
  XLSX.utils.book_append_sheet(wb, wsTaxes, 'TAXES');

  // Feuille 5 : CONTROLES
  const ctrlHeader = ['Sévérité', 'Code', 'Ligne', 'Message', 'Valeur attendue', 'Valeur constatée'];
  const ctrlRows = (calcResult.anomalies || []).map(a => [a.severity, a.code, a.lineNumber ?? '', a.message, a.expectedValue ?? '', a.actualValue ?? '']);
  const wsCtrl = XLSX.utils.aoa_to_sheet([ctrlHeader, ...ctrlRows]);
  XLSX.utils.book_append_sheet(wb, wsCtrl, 'CONTROLES');

  return XLSX.write(wb, { type: 'buffer', bookType: 'xlsx' });
}

function buildPdfReportBuffer(company, operation, fees, calcResult) {
  return new Promise((resolve, reject) => {
    const doc = new PDFDocument({ size: 'A4', margin: 40 });
    const chunks = [];
    doc.on('data', c => chunks.push(c));
    doc.on('end', () => resolve(Buffer.concat(chunks)));
    doc.on('error', reject);

    const s = calcResult.summary;
    const money = (n) => Number(n || 0).toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' DZD';

    doc.fontSize(14).font('Helvetica-Bold').text('CIMP — RAPPORT OFFICIEL DE CALCUL DU COÛT DE REVIENT', { align: 'center' });
    doc.fontSize(8).font('Helvetica').text('Conforme au Code des Douanes Algérien (Loi 17-04), au CTCA (Art. 19) et au SCF (Art. 121-3)', { align: 'center' });
    doc.moveDown(0.8);

    doc.fontSize(10).font('Helvetica-Bold').text('1. Informations Importation & Entreprise');
    doc.fontSize(9).font('Helvetica');
    doc.text(`Entreprise : ${company.legal_name}${company.nif ? ' (NIF: ' + company.nif + ')' : ''} | Régime TVA : ${company.is_vat_non_recoverable ? 'Non récupérable' : 'Récupérable'}`);
    doc.text(`Dossier : ${operation.import_number} | Date de référence : ${operation.reference_date} | Statut : ${operation.status}`);
    doc.text(`Port : ${operation.arrival_port || 'N/A'} | Transport : ${operation.transport_mode || 'N/A'}`);
    doc.moveDown(0.5);

    doc.fontSize(10).font('Helvetica-Bold').text('2. Fournisseur, Origine & Incoterm');
    doc.fontSize(9).font('Helvetica');
    doc.text(`Fournisseur : ${operation.supplier_name} | Pays d'expédition : ${operation.export_shipping_country_iso2}`);
    doc.text(`Incoterm : ${operation.incoterm} | Devise : ${operation.main_currency_code} | Taux appliqué : ${calcResult.exchangeRateUsed} (${calcResult.exchangeRateSourceNote || ''})`);
    doc.moveDown(0.5);

    doc.fontSize(10).font('Helvetica-Bold').text('3. Résumé Financier');
    doc.fontSize(9).font('Helvetica');
    doc.text(`Valeur en douane totale : ${money(s.totalCustomsValueDzd)}`);
    doc.text(`Droits de douane : ${money(s.totalCustomsDutyDzd)}  |  Autres taxes : ${money(s.totalAdditionalTaxesDzd)}  |  TVA : ${money(s.totalImportVatDzd)}`);
    doc.font('Helvetica-Bold').text(`TOTAL DROITS ET TAXES : ${money(s.totalDutiesAndTaxesDzd)}`);
    doc.font('Helvetica').text(`Coût d'acquisition hors TVA : ${money(s.totalAcquisitionCostExVatDzd)}`);
    doc.font('Helvetica-Bold').text(`COÛT TOTAL DE REVIENT RÉEL : ${money(s.totalRealCostOfGoodsDzd)}`);
    doc.moveDown(0.5);

    doc.fontSize(10).font('Helvetica-Bold').text('4. Détail par Article');
    doc.fontSize(8).font('Helvetica');
    s.lineResults.forEach(l => {
      doc.font('Helvetica-Bold').text(`Ligne ${l.lineNumber} : ${l.productReference} — ${l.designation} (Qté ${l.quantity}, SH ${l.hsCode10}, Origine ${l.originCountryIso2 || 'N/A'})`);
      doc.font('Helvetica').text(`  Valeur Douane : ${money(l.customsOutcome.customsValueDzd)} | DD (${l.customsOutcome.customsDutyRatePercent ?? 'N/D'}%) : ${money(l.customsOutcome.customsDutyAmountDzd)} | TVA (${l.customsOutcome.vatRatePercent ?? 'N/D'}%) : ${money(l.customsOutcome.importVatAmountDzd)}`);
      doc.text(`  COÛT DE REVIENT : ${money(l.economicOutcome.realCostOfGoodsTotalDzd)} → UNITAIRE : ${money(l.economicOutcome.unitCostOfGoodsDzd)} / unité`);
    });
    doc.moveDown(0.5);

    if (calcResult.anomalies && calcResult.anomalies.length) {
      doc.fontSize(10).font('Helvetica-Bold').text('5. Anomalies et Alertes');
      doc.fontSize(8).font('Helvetica');
      calcResult.anomalies.forEach(a => {
        doc.text(`[${a.severity}] ${a.message}`);
      });
      doc.moveDown(0.5);
    }

    doc.fontSize(8).font('Helvetica-Bold').text('MENTION LÉGALE OBLIGATOIRE :');
    doc.font('Helvetica').text(s.mandatoryLegalDisclaimerFr);
    doc.text(`Version réglementaire appliquée : voir écran Réglementation | Généré le : ${new Date().toLocaleString('fr-FR')}`);

    doc.end();
  });
}

module.exports = { buildExcelReportBuffer, buildPdfReportBuffer };
