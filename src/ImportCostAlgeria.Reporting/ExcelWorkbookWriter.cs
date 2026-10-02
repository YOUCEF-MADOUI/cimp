using System;
using System.Linq;
using ClosedXML.Excel;

namespace ImportCostAlgeria.Reporting;

/// <summary>
/// Écriture RÉELLE du classeur Excel à 5 feuilles minimum (Section 19 du plan de finalisation Windows) :
/// DETAIL_ARTICLES, RECAPITULATIF, FRAIS, TAXES, CONTROLES.
/// Contrairement à <see cref="ReportBuilderService"/> (qui ne fait que construire le MODÈLE de données
/// en mémoire), cette classe produit un fichier .xlsx réellement exploitable sous Microsoft Excel /
/// LibreOffice Calc, avec mise en forme minimale (en-têtes en gras, largeur de colonnes automatique).
/// </summary>
public static class ExcelWorkbookWriter
{
    public static void WriteToFile(ExcelWorkbookReportModel model, string filePath)
    {
        using var workbook = new XLWorkbook();

        WriteDetailArticlesSheet(workbook, model);
        WriteRecapitulatifSheet(workbook, model);
        WriteFraisSheet(workbook, model);
        WriteTaxesSheet(workbook, model);
        WriteControlesSheet(workbook, model);

        workbook.SaveAs(filePath);
    }

    private static void WriteDetailArticlesSheet(XLWorkbook workbook, ExcelWorkbookReportModel model)
    {
        var ws = workbook.Worksheets.Add("DETAIL_ARTICLES");
        string[] headers =
        {
            "N° Ligne", "Référence", "Désignation", "Quantité", "Prix Fournisseur Unitaire", "Devise",
            "Taux de Change", "Valeur Convertie (DZD)", "Code SH", "Origine", "Incoterm",
            "Part Frais Douane (DZD)", "Valeur Douanière (DZD)", "Droit de Douane (DZD)",
            "Autres Taxes (DZD)", "TVA (DZD)", "Autres Frais Locaux (DZD)", "Total Frais Répartis (DZD)",
            "Coût Total de Revient (DZD)", "Coût Unitaire de Revient (DZD)",
            // Section 12 & 15 du plan multi-devises : conversion commerciale (jamais réglementaire) vers
            // la devise de l'autorisation d'importation ; colonnes laissées vides quand non applicable.
            "Devise Autorisation", "PU Autorisation", "Total Autorisation"
        };
        WriteHeaderRow(ws, headers);

        int row = 2;
        foreach (var r in model.DetailArticlesSheet)
        {
            int c = 1;
            ws.Cell(row, c++).Value = r.LineNumber;
            ws.Cell(row, c++).Value = r.Reference;
            ws.Cell(row, c++).Value = r.Designation;
            ws.Cell(row, c++).Value = r.Quantite;
            ws.Cell(row, c++).Value = r.PrixFournisseurUnitaire;
            ws.Cell(row, c++).Value = r.Devise;
            ws.Cell(row, c++).Value = r.TauxChange;
            ws.Cell(row, c++).Value = r.ValeurConvertieDzd;
            ws.Cell(row, c++).Value = r.CodeSh;
            ws.Cell(row, c++).Value = r.Origine;
            ws.Cell(row, c++).Value = r.Incoterm;
            ws.Cell(row, c++).Value = r.PartFraisDouaneDzd;
            ws.Cell(row, c++).Value = r.ValeurDouaniereDzd;
            ws.Cell(row, c++).Value = r.DroitDouaneDzd;
            ws.Cell(row, c++).Value = r.AutresTaxesDzd;
            ws.Cell(row, c++).Value = r.TvaDzd;
            ws.Cell(row, c++).Value = r.AutresFraisLocauxDzd;
            ws.Cell(row, c++).Value = r.TotalFraisRepartisDzd;
            ws.Cell(row, c++).Value = r.CoutTotalRevientDzd;
            ws.Cell(row, c++).Value = r.CoutUnitaireRevientDzd;
            ws.Cell(row, c++).Value = r.DeviseAutorisation ?? string.Empty;
            if (r.PrixUnitaireAutorisation.HasValue) ws.Cell(row, c).Value = r.PrixUnitaireAutorisation.Value;
            c++;
            if (r.TotalAutorisation.HasValue) ws.Cell(row, c).Value = r.TotalAutorisation.Value;
            c++;
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    private static void WriteRecapitulatifSheet(XLWorkbook workbook, ExcelWorkbookReportModel model)
    {
        var ws = workbook.Worksheets.Add("RECAPITULATIF");
        var r = model.RecapitulatifSheet;

        int row = 1;
        void Line(string label, object value)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.Bold = true;
            if (value is decimal dec) ws.Cell(row, 2).Value = dec;
            else ws.Cell(row, 2).Value = value?.ToString() ?? string.Empty;
            row++;
        }

        ws.Cell(row, 1).Value = model.WorkbookTitle;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;

        Line("Valeur Fournisseur (DZD)", r.ValeurFournisseurDzd);
        Line("Transport International (DZD)", r.TransportInternationalDzd);
        Line("Assurance (DZD)", r.AssuranceDzd);
        Line("Autres Frais (DZD)", r.AutresFraisDzd);
        Line("Total Frais (DZD)", r.TotalFraisDzd);
        Line("Valeur Douanière (DZD)", r.ValeurDouaniereDzd);
        Line("Total Droits de Douane (DZD)", r.TotalDroitsDouaneDzd);
        // Revue du 2026-10-02 (correction urgente, Section 14) : chaque taxe additionnelle affichée
        // SÉPARÉMENT — une ligne combinée "Autres taxes" unique est explicitement rejetée.
        Line("Total CS (DZD)", r.TotalCsDzd);
        Line("Total PRCT (DZD)", r.TotalPrctDzd);
        // Correction 2026-10-02 (demande utilisateur — ambiguïté CS/TCS) : la ligne "Total TCS" est retirée
        // du rapport exporté (CS reste la seule taxe de ce type affichée, cohérence avec l'écran
        // Importation) — TotalTcsDzd continue d'alimenter normalement Total Droits et Taxes/Coût de
        // revient ci-dessous, rien n'est supprimé du calcul.
        Line("Total DAPS (DZD)", r.TotalDapsDzd);
        Line("Total RPS (DZD)", r.TotalRpsDzd);
        Line("Total TVA (DZD)", r.TotalTvaDzd);
        Line("Total Droits et Taxes (DZD)", r.TotalDroitsEtTaxesDzd);
        Line("Coût d'Acquisition Hors TVA (DZD)", r.CoutAcquisitionHorsTvaDzd);
        Line("Coût Total de Revient (DZD)", r.CoutTotalRevientDzd);
        Line("Coût Moyen Unitaire (DZD)", r.CoutMoyenUnitaireDzd);
        Line("TVA Non Récupérable", r.IsVatNonRecoverable ? "OUI (incluse au coût de revient)" : "NON (récupérable)");
        Line("Version Réglementaire Appliquée", r.RegulatoryVersionCode);
        row++;

        // Section 6, 11 & 15 du plan multi-devises : bloc "CONVERSION COMMERCIALE" affiché UNIQUEMENT
        // lorsqu'une conversion est réellement nécessaire (devise facture != devise d'autorisation),
        // clairement séparé de la conversion réglementaire vers DZD ci-dessus (Section 14 & 22).
        if (r.DeviseAutorisation != null && r.MontantAutorisation.HasValue)
        {
            ws.Cell(row, 1).Value = "CONVERSION COMMERCIALE (AUTORISATION D'IMPORTATION)";
            ws.Cell(row, 1).Style.Font.Bold = true;
            row++;
            Line($"Devise de la facture", r.DeviseOriginale ?? "-");
            Line($"Montant facture ({r.DeviseOriginale})", r.MontantOriginal ?? 0m);
            Line($"Taux {r.DeviseOriginale}/{r.DeviseAutorisation}", r.TauxChangeAutorisation ?? 0m);
            Line($"Type de taux", r.TypeDeTauxAutorisationFr ?? "-");
            Line($"Montant équivalent ({r.DeviseAutorisation})", r.MontantAutorisation.Value);
            row++;
        }
        ws.Cell(row, 1).Value = "MENTION LÉGALE OBLIGATOIRE";
        ws.Cell(row, 1).Style.Font.Bold = true;
        row++;
        ws.Cell(row, 1).Value = r.LegalDisclaimerFr;
        ws.Range(row, 1, row, 2).Merge().Style.Alignment.WrapText = true;

        ws.Columns().AdjustToContents();
    }

    private static void WriteFraisSheet(XLWorkbook workbook, ExcelWorkbookReportModel model)
    {
        var ws = workbook.Worksheets.Add("FRAIS");
        string[] headers =
        {
            "Frais", "Catégorie", "Montant (Devise)", "Devise", "Taux de Change", "Montant Converti (DZD)",
            "Méthode de Répartition", "Inclus Valeur en Douane", "Inclus Coût de Revient",
            "Somme Répartie sur Lignes (DZD)", "Répartition Exacte ?"
        };
        WriteHeaderRow(ws, headers);

        int row = 2;
        foreach (var f in model.FraisSheet)
        {
            int c = 1;
            ws.Cell(row, c++).Value = f.FeeName;
            ws.Cell(row, c++).Value = f.CategoryCode;
            ws.Cell(row, c++).Value = f.AmountCurrency;
            ws.Cell(row, c++).Value = f.CurrencyCode;
            ws.Cell(row, c++).Value = f.ExchangeRateToDzd;
            ws.Cell(row, c++).Value = f.AmountConvertedDzd;
            ws.Cell(row, c++).Value = f.AllocationMethod;
            ws.Cell(row, c++).Value = f.IncludedInCustomsValue ? "OUI" : "NON";
            ws.Cell(row, c++).Value = f.IncludedInCostOfGoods ? "OUI" : "NON";
            ws.Cell(row, c++).Value = f.SumOfLineAllocationsDzd;
            ws.Cell(row, c++).Value = f.IsAllocationSumExact ? "OUI" : "⚠️ ÉCART";
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    private static void WriteTaxesSheet(XLWorkbook workbook, ExcelWorkbookReportModel model)
    {
        var ws = workbook.Worksheets.Add("TAXES");
        string[] headers =
        {
            "N° Ligne", "Référence", "Code SH", "Code Taxe", "Nom de la Taxe", "Base Imposable (DZD)",
            "Taux (%)", "Montant (DZD)", "Non Récupérable", "Source Légale et Article", "Version Réglementaire"
        };
        WriteHeaderRow(ws, headers);

        int row = 2;
        foreach (var t in model.TaxesSheet)
        {
            int c = 1;
            ws.Cell(row, c++).Value = t.LineNumber;
            ws.Cell(row, c++).Value = t.ProductReference;
            ws.Cell(row, c++).Value = t.HsCode10;
            ws.Cell(row, c++).Value = t.TaxCode;
            ws.Cell(row, c++).Value = t.TaxNameFr;
            ws.Cell(row, c++).Value = t.TaxableBaseDzd;
            ws.Cell(row, c++).Value = t.RatePercent;
            ws.Cell(row, c++).Value = t.TaxAmountDzd;
            ws.Cell(row, c++).Value = t.IsNonRecoverable ? "OUI" : "NON";
            ws.Cell(row, c++).Value = t.LegalSourceAndArticle;
            ws.Cell(row, c++).Value = t.RegulatoryVersionCode;
            row++;
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    private static void WriteControlesSheet(XLWorkbook workbook, ExcelWorkbookReportModel model)
    {
        var ws = workbook.Worksheets.Add("CONTROLES");
        string[] headers = { "Sévérité", "Code Anomalie", "N° Ligne", "Message", "Valeur Attendue", "Valeur Constatée" };
        WriteHeaderRow(ws, headers);

        int row = 2;
        foreach (var a in model.ControlesSheet)
        {
            int c = 1;
            var cellSeverity = ws.Cell(row, c++);
            cellSeverity.Value = a.Severity;
            cellSeverity.Style.Fill.BackgroundColor = a.Severity switch
            {
                "BLOCAGE" => XLColor.FromArgb(0xF8, 0xD7, 0xDA),
                "ERREUR" => XLColor.FromArgb(0xFC, 0xE8, 0xCB),
                "AVERTISSEMENT" => XLColor.FromArgb(0xFF, 0xF3, 0xCD),
                _ => XLColor.White
            };
            ws.Cell(row, c++).Value = a.AnomalyCode;
            ws.Cell(row, c++).Value = a.LineNumber?.ToString() ?? "-";
            ws.Cell(row, c++).Value = a.MessageFr;
            ws.Cell(row, c++).Value = a.ExpectedRegulatoryValue ?? "-";
            ws.Cell(row, c++).Value = a.ActualUserOrExcelValue ?? "-";
            row++;
        }

        if (model.ControlesSheet.Count == 0)
        {
            ws.Cell(2, 1).Value = "Aucune anomalie détectée.";
        }

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);
    }

    private static void WriteHeaderRow(IXLWorksheet ws, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xD9, 0xE1, 0xF2);
        }
    }
}
