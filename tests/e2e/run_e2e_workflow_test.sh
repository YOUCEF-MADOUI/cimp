#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# CIMP — Test de bout en bout (Section 26 du cahier des charges).
# Valide le pipeline complet : Entreprise -> Dossier -> Import Excel -> Mapping
# -> Règles réglementaires -> Frais -> Calcul -> Anomalies -> Export Excel/PDF.
#
# Prérequis : le serveur CIMP doit tourner sur http://localhost:3000
#   (cd src/ImportCostAlgeria.Presentation/WebPreview && node server.js)
# ---------------------------------------------------------------------------
set -e
BASE="${1:-http://localhost:3000}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

pass() { echo "[PASS] $1"; }
fail() { echo "[FAIL] $1"; exit 1; }

echo "=== CIMP — Test de bout en bout ==="

# 1. Créer une entreprise
COMPANY_ID=$(curl -s -X POST "$BASE/api/companies" -H "Content-Type: application/json" -d '{
  "legalName":"E2E TEST SARL","nif":"000000000000000","isVatNonRecoverable":true
}' | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['id'])")
[ -n "$COMPANY_ID" ] && pass "Création entreprise ($COMPANY_ID)" || fail "Création entreprise"

# 2. Créer un dossier d'importation (Incoterm FOB)
# NB: la devise principale du dossier est volontairement fixée à une devise fictive non publiée
# ("E2Z") afin de garantir un test de blocage indépendant de l'état d'exécutions précédentes.
IMPORT_ID=$(curl -s -X POST "$BASE/api/companies/$COMPANY_ID/imports" -H "Content-Type: application/json" -d '{
  "importNumber":"E2E-TEST-0001","referenceDate":"2026-09-15","supplierName":"E2E SUPPLIER",
  "exportShippingCountryIso2":"CN","defaultOriginCountryIso2":"CN","mainCurrencyCode":"E2Z","incoterm":"FOB"
}' | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['id'])")
[ -n "$IMPORT_ID" ] && pass "Création dossier d'importation ($IMPORT_ID)" || fail "Création dossier"

# 3. Import du fichier Excel fournisseur (colonne "Prix Fournisseur" non standard)
ANALYSIS=$(curl -s -X POST "$BASE/api/imports/$IMPORT_ID/excel/upload" -F "file=@samples/Facture_Fournisseur_X_Import.xlsx")
echo "$ANALYSIS" | python3 -c "import sys,json;d=json.load(sys.stdin)['data'];assert d['totalRows']==2;assert 'unitPrice' in d['suggestedMapping']" \
  && pass "Analyse Excel + auto-détection colonnes" || fail "Analyse Excel"

# 4. Confirmation du mapping + import des lignes
OUTCOME=$(curl -s -X POST "$BASE/api/imports/$IMPORT_ID/excel/confirm" \
  -F "file=@samples/Facture_Fournisseur_X_Import.xlsx" \
  -F 'mapping={"reference":0,"designation":1,"quantity":2,"unitPrice":3,"currency":4,"hsCode":5,"origin":6,"dutyRate":7,"weight":9}' \
  -F "supplierName=E2E SUPPLIER" -F "saveAsTemplate=true")
COUNT=$(echo "$OUTCOME" | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['insertedCount'])")
[ "$COUNT" = "2" ] && pass "Import des 2 lignes Excel" || fail "Import des lignes Excel (obtenu: $COUNT)"

# 5. Vérifier la réutilisation automatique du mapping sauvegardé
REUSE=$(curl -s -X POST "$BASE/api/imports/$IMPORT_ID/excel/upload" -F "file=@samples/Facture_Fournisseur_X_Import.xlsx" \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['templateFound'])")
[ "$REUSE" = "True" ] && pass "Réutilisation automatique du modèle de mapping" || fail "Modèle de mapping non réutilisé"

# 6. Calcul SANS règles réglementaires -> doit être bloqué
BLOCKED=$(curl -s -X POST "$BASE/api/imports/$IMPORT_ID/calculate" | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['blocked'])")
[ "$BLOCKED" = "True" ] && pass "Calcul correctement bloqué sans taux de change officiel" || fail "Le calcul aurait dû être bloqué"

# 7. Publier le taux de change et les règles réglementaires
curl -s -X POST "$BASE/api/exchange-rates" -H "Content-Type: application/json" -d '{
  "currencyCode":"EUR","rateToDzd":146.50,"validFrom":"2026-09-01","source":"E2E TEST"
}' > /dev/null
curl -s -X POST "$BASE/api/exchange-rates" -H "Content-Type: application/json" -d '{
  "currencyCode":"E2Z","rateToDzd":146.50,"validFrom":"2026-09-01","source":"E2E TEST (devise fictive du dossier de test)"
}' > /dev/null

for HS in 8708999000 8421299000; do
  RATE=15; [ "$HS" = "8421299000" ] && RATE=5
  curl -s -X POST "$BASE/api/regulatory/rules" -H "Content-Type: application/json" -d "{
    \"taxCode\":\"DD\",\"hsCode10\":\"$HS\",\"originCountryIso2\":\"CN\",\"ratePercent\":$RATE,
    \"validFrom\":\"2026-01-01\",\"legalSourceTitle\":\"E2E TEST — Code des Douanes\",\"articleReference\":\"Art. 9\"
  }" > /dev/null
  curl -s -X POST "$BASE/api/regulatory/rules" -H "Content-Type: application/json" -d "{
    \"taxCode\":\"TVA\",\"hsCode10\":\"$HS\",\"ratePercent\":19,\"calculationBase\":\"VALEUR_DOUANE_PLUS_DD\",
    \"validFrom\":\"2026-01-01\",\"legalSourceTitle\":\"E2E TEST — CTCA\",\"articleReference\":\"Art. 19\"
  }" > /dev/null
done
pass "Publication du taux de change et des règles réglementaires (DD, TVA)"

# 8. Ajouter un frais avec répartition au poids
curl -s -X POST "$BASE/api/imports/$IMPORT_ID/fees" -H "Content-Type: application/json" -d '{
  "feeName":"Fret international","categoryCode":"FRET_INTERNATIONAL","amount":1200,"currencyCode":"EUR",
  "allocationMethod":"PAR_POIDS","includeInCustomsValue":true,"includeInCostOfGoods":true
}' > /dev/null
pass "Ajout d'un frais avec répartition au poids"

# 9. Calcul complet -> doit réussir
RESULT=$(curl -s -X POST "$BASE/api/imports/$IMPORT_ID/calculate")
BLOCKED2=$(echo "$RESULT" | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['blocked'])")
[ "$BLOCKED2" = "False" ] && pass "Calcul complet réussi" || fail "Le calcul complet a échoué : $RESULT"

TOTAL=$(echo "$RESULT" | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['summary']['totalRealCostOfGoodsDzd'])")
echo "        -> Coût de revient total calculé : $TOTAL DZD"

# 10. Vérifier la détection d'anomalie (taux Excel 10% vs réglementaire 15%)
ANOMALY=$(echo "$RESULT" | python3 -c "
import sys,json
d=json.load(sys.stdin)['data']
print(any(a['code']=='DUTY_RATE_MISMATCH' for a in d['anomalies']))
")
[ "$ANOMALY" = "True" ] && pass "Anomalie de comparaison taux Excel / réglementaire détectée" || fail "Anomalie DUTY_RATE_MISMATCH non détectée"

# 11. Export Excel dynamique (5 feuilles)
curl -s -o /tmp/e2e_export.xlsx "$BASE/api/imports/$IMPORT_ID/export/excel"
SHEETS=$(python3 -c "
import zipfile
z = zipfile.ZipFile('/tmp/e2e_export.xlsx')
import re
wb = z.read('xl/workbook.xml').decode()
print(len(re.findall(r'name=\"([^\"]+)\"', wb)))
")
[ "$SHEETS" = "5" ] && pass "Export Excel généré avec 5 feuilles" || fail "Export Excel incorrect (feuilles: $SHEETS)"

# 12. Export PDF dynamique
curl -s -o /tmp/e2e_export.pdf "$BASE/api/imports/$IMPORT_ID/export/pdf"
MAGIC=$(head -c 5 /tmp/e2e_export.pdf)
[ "$MAGIC" = "%PDF-" ] && pass "Export PDF généré (fichier PDF valide)" || fail "Export PDF invalide"

# 13. Nettoyage des données de test
curl -s -X DELETE "$BASE/api/companies/$COMPANY_ID" > /dev/null
pass "Nettoyage des données de test"

echo ""
echo "=== TOUS LES TESTS DE BOUT EN BOUT ONT RÉUSSI ==="
