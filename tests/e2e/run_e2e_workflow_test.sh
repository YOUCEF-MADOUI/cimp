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

# 0. Récupérer un utilisateur Administrateur (créé automatiquement au premier démarrage)
ADMIN_ID=$(curl -s "$BASE/api/users" | python3 -c "
import sys,json
users=json.load(sys.stdin)['data']
admin=next((u for u in users if u['role']=='ADMINISTRATEUR'), None)
print(admin['id'] if admin else '')
")
[ -n "$ADMIN_ID" ] && pass "Compte Administrateur disponible ($ADMIN_ID)" || fail "Aucun compte Administrateur trouvé"

# 1. Créer une entreprise
COMPANY_ID=$(curl -s -X POST "$BASE/api/companies" -H "Content-Type: application/json" -d '{
  "legalName":"E2E TEST SARL","nif":"000000000000000","isVatNonRecoverable":true
}' | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['id'])")
[ -n "$COMPANY_ID" ] && pass "Création entreprise ($COMPANY_ID)" || fail "Création entreprise"

# 2. Créer un dossier d'importation (Incoterm FOB)
# NB: la devise principale du dossier est volontairement fixée à une devise fictive à usage unique
# (jamais publiée) afin de garantir un test de blocage indépendant de l'état d'exécutions précédentes.
# Forte entropie (timestamp nanosecondes + PID + aléatoire) pour garantir l'unicité même en cas
# d'exécutions répétées très rapprochées du script (aucune collision avec une devise déjà publiée
# par une exécution précédente, ce qui fausserait le test de blocage ci-dessous).
FAKE_CCY="Z$(( (RANDOM * RANDOM + $$ + $(date +%s%N)) % 900000 + 100000 ))"
IMPORT_ID=$(curl -s -X POST "$BASE/api/companies/$COMPANY_ID/imports" -H "Content-Type: application/json" -d "{
  \"importNumber\":\"E2E-TEST-0001\",\"referenceDate\":\"2026-09-15\",\"supplierName\":\"E2E SUPPLIER\",
  \"exportShippingCountryIso2\":\"CN\",\"defaultOriginCountryIso2\":\"CN\",\"mainCurrencyCode\":\"$FAKE_CCY\",\"incoterm\":\"FOB\"
}" | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['id'])")
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

# 6b. Vérifier qu'un utilisateur non-Administrateur ne peut PAS publier de règle (Sections 21 & 39)
NONADMIN_ID=$(curl -s -X POST "$BASE/api/users" -H "Content-Type: application/json" -d '{"fullName":"E2E Non Admin","role":"UTILISATEUR"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['id'])")
NONADMIN_HTTP=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE/api/exchange-rates" -H "Content-Type: application/json" -H "X-User-Id: $NONADMIN_ID" -d "{
  \"currencyCode\":\"$FAKE_CCY\",\"rateToDzd\":1,\"validFrom\":\"2026-01-01\"
}")
[ "$NONADMIN_HTTP" = "403" ] && pass "Publication refusée pour un utilisateur non-Administrateur (HTTP 403)" || fail "Un non-Administrateur a pu publier une règle (HTTP $NONADMIN_HTTP)"
curl -s -X DELETE "$BASE/api/users/$NONADMIN_ID" > /dev/null

# 7. Publier le taux de change et les règles réglementaires (en tant qu'Administrateur)
curl -s -X POST "$BASE/api/exchange-rates" -H "Content-Type: application/json" -H "X-User-Id: $ADMIN_ID" -d '{
  "currencyCode":"EUR","rateToDzd":146.50,"validFrom":"2026-09-01","source":"E2E TEST"
}' > /dev/null
curl -s -X POST "$BASE/api/exchange-rates" -H "Content-Type: application/json" -H "X-User-Id: $ADMIN_ID" -d "{
  \"currencyCode\":\"$FAKE_CCY\",\"rateToDzd\":146.50,\"validFrom\":\"2026-09-01\",\"source\":\"E2E TEST (devise fictive du dossier de test)\"
}" > /dev/null

for HS in 8708999000 8421299000; do
  RATE=15; [ "$HS" = "8421299000" ] && RATE=5
  curl -s -X POST "$BASE/api/regulatory/rules" -H "Content-Type: application/json" -H "X-User-Id: $ADMIN_ID" -d "{
    \"taxCode\":\"DD\",\"hsCode10\":\"$HS\",\"originCountryIso2\":\"CN\",\"ratePercent\":$RATE,
    \"validFrom\":\"2026-01-01\",\"legalSourceTitle\":\"E2E TEST — Code des Douanes\",\"articleReference\":\"Art. 9\"
  }" > /dev/null
  curl -s -X POST "$BASE/api/regulatory/rules" -H "Content-Type: application/json" -H "X-User-Id: $ADMIN_ID" -d "{
    \"taxCode\":\"TVA\",\"hsCode10\":\"$HS\",\"ratePercent\":19,\"calculationBase\":\"VALEUR_DOUANE_PLUS_DD\",
    \"validFrom\":\"2026-01-01\",\"legalSourceTitle\":\"E2E TEST — CTCA\",\"articleReference\":\"Art. 19\"
  }" > /dev/null
done
pass "Publication du taux de change et des règles réglementaires (DD, TVA) en tant qu'Administrateur"

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

# 13. Vérifier le rapprochement automatique du catalogue produit (Section 31 & 32)
curl -s -X POST "$BASE/api/companies/$COMPANY_ID/products" -H "Content-Type: application/json" -d '{
  "reference":"PROD-A","designation":"Catalogue existant","hsCode10":"8708999000","originCountryIso2":"CN"
}' > /dev/null
CATALOG_OUTCOME=$(curl -s -X POST "$BASE/api/imports/$IMPORT_ID/excel/confirm" \
  -F "file=@samples/Facture_Fournisseur_X_Import.xlsx" \
  -F 'mapping={"reference":0,"designation":1,"quantity":2,"unitPrice":3,"currency":4}' \
  -F "supplierName=E2E SUPPLIER" -F "saveAsTemplate=false")
MATCHED=$(echo "$CATALOG_OUTCOME" | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['matchedCatalogCount'])")
[ "$MATCHED" = "1" ] && pass "Rapprochement automatique du catalogue produit (1 référence reconnue)" || fail "Rapprochement catalogue incorrect (obtenu: $MATCHED)"

# 14. Vérifier l'historique des versions réglementaires
curl -s -X POST "$BASE/api/regulatory/rules" -H "Content-Type: application/json" -H "X-User-Id: $ADMIN_ID" -d '{
  "taxCode":"DD","hsCode10":"8708999000","originCountryIso2":"CN","ratePercent":18,
  "validFrom":"2027-01-01","legalSourceTitle":"E2E TEST — Nouvelle version","articleReference":"Art. 9"
}' > /dev/null
DIFF_COUNT=$(curl -s "$BASE/api/regulatory/rules/history/8708999000/DD" | python3 -c "import sys,json;print(len(json.load(sys.stdin)['data']['diffs']))")
[ "$DIFF_COUNT" -ge "1" ] && pass "Historique des versions réglementaires disponible ($DIFF_COUNT écart(s))" || fail "Aucun écart d'historique détecté"

# 15. Confirmation manuelle tracée du taux Excel en l'absence de toute règle officielle (Section 12)
# Réalisé dans un dossier dédié et isolé, pour ne pas être bloqué par les lignes d'autres étapes du test.
DD_IMPORT_ID=$(curl -s -X POST "$BASE/api/companies/$COMPANY_ID/imports" -H "Content-Type: application/json" -d "{
  \"importNumber\":\"E2E-DD-CONFIRM\",\"referenceDate\":\"2026-09-15\",\"supplierName\":\"E2E SUPPLIER\",
  \"exportShippingCountryIso2\":\"CN\",\"mainCurrencyCode\":\"$FAKE_CCY\",\"incoterm\":\"FOB\"
}" | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['id'])")
FAKE_HS="99$(date +%s | tail -c 9)"
DD_LINE_ID=$(curl -s -X POST "$BASE/api/imports/$DD_IMPORT_ID/lines" -H "Content-Type: application/json" -d "{
  \"reference\":\"DD-CONFIRM-TEST\",\"designation\":\"Article sans règle\",\"quantity\":1,\"unitPrice\":100,
  \"currencyCode\":\"$FAKE_CCY\",\"hsCode10\":\"$FAKE_HS\",\"dutyRate\":12
}" | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['id'])")
BLOCKED3=$(curl -s -X POST "$BASE/api/imports/$DD_IMPORT_ID/calculate" | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['blocked'])")
[ "$BLOCKED3" = "True" ] && pass "Calcul bloqué tant que le taux Excel n'est pas confirmé (aucune règle officielle)" || fail "Le calcul aurait dû rester bloqué sans confirmation ni règle"
curl -s -X POST "$BASE/api/lines/$DD_LINE_ID/confirm-excel-duty-rate" -H "X-User-Id: $ADMIN_ID" -d '{}' -H "Content-Type: application/json" > /dev/null
curl -s -X POST "$BASE/api/regulatory/rules" -H "Content-Type: application/json" -H "X-User-Id: $ADMIN_ID" -d "{
  \"taxCode\":\"TVA\",\"hsCode10\":\"$FAKE_HS\",\"ratePercent\":19,\"validFrom\":\"2026-01-01\",
  \"legalSourceTitle\":\"E2E TEST\",\"calculationBase\":\"VALEUR_DOUANE_PLUS_DD\"
}" > /dev/null
CONFIRM_RESULT=$(curl -s -X POST "$BASE/api/imports/$DD_IMPORT_ID/calculate")
UNOFFICIAL=$(echo "$CONFIRM_RESULT" | python3 -c "
import sys,json
d=json.load(sys.stdin)['data']
print(any(a['code']=='DUTY_RATE_UNOFFICIAL_CONFIRMED' for a in d.get('anomalies',[])))
")
[ "$UNOFFICIAL" = "True" ] && pass "Taux Excel confirmé manuellement utilisé avec avertissement tracé (aucun taux inventé)" || fail "La confirmation manuelle du taux Excel n'a pas produit l'avertissement attendu"

# 16. Nettoyage des données de test
curl -s -X DELETE "$BASE/api/companies/$COMPANY_ID" > /dev/null
pass "Nettoyage des données de test"

echo ""
echo "=== TOUS LES TESTS DE BOUT EN BOUT ONT RÉUSSI ==="
