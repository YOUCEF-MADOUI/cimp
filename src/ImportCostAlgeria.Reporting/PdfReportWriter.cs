using System;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ImportCostAlgeria.Reporting;

/// <summary>
/// Écriture RÉELLE du rapport PDF professionnel d'une importation (Section 20 du plan de finalisation
/// Windows) : informations entreprise, opération, fournisseur, Incoterm, articles, valeur en douane,
/// droits, taxes, frais, coût douanier, coût de revient, anomalies, sources réglementaires, date de
/// génération et mention légale obligatoire.
/// </summary>
public static class PdfReportWriter
{
    static PdfReportWriter()
    {
        // Licence Community QuestPDF (gratuite pour les petites/moyennes organisations — voir questpdf.com).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static void WriteToFile(PdfProfessionalReportModel model, string filePath)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Text($"RAPPORT D'IMPORTATION — {model.ImportNumber}")
                        .FontSize(16).Bold();
                    col.Item().Text($"Généré le {model.GeneratedAtUtc:dd/MM/yyyy HH:mm} UTC — Version réglementaire appliquée : {model.RegulatoryVersionUsed}")
                        .FontSize(8).Italic();
                    col.Item().PaddingTop(4).LineHorizontal(1);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Element(c => BuildIdentificationSection(c, model));
                    col.Item().Element(c => BuildArticlesSection(c, model));
                    col.Item().Element(c => BuildRecapSection(c, model));
                    col.Item().Element(c => BuildFeesSection(c, model));
                    col.Item().Element(c => BuildTaxesSection(c, model));
                    col.Item().Element(c => BuildControlsSection(c, model));
                    col.Item().Element(c => BuildLegalSection(c, model));
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("CIMP — Coût d'Importation Maîtrisé pour le Produit — Page ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }).GeneratePdf(filePath);
    }

    private static void BuildIdentificationSection(QuestPDF.Infrastructure.IContainer container, PdfProfessionalReportModel m)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(col =>
        {
            col.Item().Text("IDENTIFICATION").Bold().FontSize(11);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"Entreprise : {m.CompanyLegalName}");
                    c.Item().Text($"NIF : {m.CompanyNif ?? "Non renseigné"}");
                    c.Item().Text($"Fournisseur : {m.SupplierName}");
                    c.Item().Text($"Pays d'expédition : {m.ExportShippingCountryIso2}");
                    c.Item().Text($"Pays d'origine : {string.Join(", ", m.DistinctOriginCountries)}");
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"N° d'importation : {m.ImportNumber}");
                    c.Item().Text($"Date de référence : {m.ReferenceDate:dd/MM/yyyy}");
                    c.Item().Text($"Incoterm : {m.IncotermCode}");
                    c.Item().Text($"Devise de la facture : {m.CurrencyCode} — Taux réglementaire appliqué (vers DZD) : {m.AppliedExchangeRateToDzd:F4}");
                    c.Item().Text($"Port / Transport : {m.ArrivalPort} — {m.TransportMode}");
                    // Section 11 du plan multi-devises : affichage clair devise originale / devise de
                    // l'autorisation d'importation, strictement séparé de la conversion réglementaire DZD.
                    if (m.FinancialSummary.DeviseAutorisation != null && m.FinancialSummary.MontantAutorisation.HasValue)
                    {
                        c.Item().PaddingTop(3).Text($"Autorisation d'importation : {m.FinancialSummary.MontantAutorisation.Value:N2} {m.FinancialSummary.DeviseAutorisation} (Taux {m.FinancialSummary.DeviseOriginale}/{m.FinancialSummary.DeviseAutorisation} = {m.FinancialSummary.TauxChangeAutorisation:F4}, {m.FinancialSummary.TypeDeTauxAutorisationFr})").Bold();
                    }
                });
            });
        });
    }

    private static void BuildArticlesSection(QuestPDF.Infrastructure.IContainer container, PdfProfessionalReportModel m)
    {
        container.Column(col =>
        {
            col.Item().Text("ARTICLES — DÉTAIL DU CALCUL").Bold().FontSize(11);
            col.Item().Table(table =>
            {
                // Section 12 & 15 du plan multi-devises : colonne supplémentaire "Autorisation" affichée
                // uniquement si au moins une ligne porte une conversion commerciale (sinon colonnes vides,
                // jamais affichée comme si une conversion inutile avait été faite — Section 15).
                bool anyAuthorizationConversion = m.ArticleDetails.Any(a => a.DeviseAutorisation != null);

                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2.0f); // Référence / désignation
                    c.RelativeColumn(0.8f); // Qté
                    c.RelativeColumn(1.1f); // Code SH
                    c.RelativeColumn(1.3f); // Valeur douanière
                    c.RelativeColumn(1.1f); // Droit douane
                    c.RelativeColumn(1.1f); // TVA
                    c.RelativeColumn(1.3f); // Coût revient
                    c.RelativeColumn(1.3f); // Coût unitaire
                    if (anyAuthorizationConversion) c.RelativeColumn(1.5f); // Total autorisation (ex: USD)
                });

                table.Header(header =>
                {
                    var headerLabels = new System.Collections.Generic.List<string>
                        { "Référence / Désignation", "Qté", "Code SH", "Val. Douanière (DZD)", "Droit Douane (DZD)", "TVA (DZD)", "Coût Revient (DZD)", "Coût Unit. (DZD)" };
                    if (anyAuthorizationConversion) headerLabels.Add("Total Autorisation");
                    foreach (var h in headerLabels)
                    {
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(h).Bold().FontSize(7.5f);
                    }
                });

                foreach (var a in m.ArticleDetails)
                {
                    table.Cell().Padding(2).Text($"{a.Reference} — {a.Designation}").FontSize(7.5f);
                    table.Cell().Padding(2).Text(a.Quantite.ToString("N2")).FontSize(7.5f);
                    table.Cell().Padding(2).Text(a.CodeSh).FontSize(7.5f);
                    table.Cell().Padding(2).Text(a.ValeurDouaniereDzd.ToString("N2")).FontSize(7.5f);
                    table.Cell().Padding(2).Text(a.DroitDouaneDzd.ToString("N2")).FontSize(7.5f);
                    table.Cell().Padding(2).Text(a.TvaDzd.ToString("N2")).FontSize(7.5f);
                    table.Cell().Padding(2).Text(a.CoutTotalRevientDzd.ToString("N2")).FontSize(7.5f);
                    table.Cell().Padding(2).Text(a.CoutUnitaireRevientDzd.ToString("N2")).FontSize(7.5f);
                    if (anyAuthorizationConversion)
                    {
                        string authorizationText = a.DeviseAutorisation != null && a.TotalAutorisation.HasValue
                            ? $"{a.TotalAutorisation.Value:N2} {a.DeviseAutorisation}"
                            : "-";
                        table.Cell().Padding(2).Text(authorizationText).FontSize(7.5f);
                    }
                }
            });
        });
    }

    private static void BuildRecapSection(QuestPDF.Infrastructure.IContainer container, PdfProfessionalReportModel m)
    {
        var r = m.FinancialSummary;
        container.Border(1).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(col =>
        {
            col.Item().Text("RÉCAPITULATIF FINANCIER").Bold().FontSize(11);
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"Valeur fournisseur : {r.ValeurFournisseurDzd:N2} DZD");
                    c.Item().Text($"Transport international : {r.TransportInternationalDzd:N2} DZD");
                    c.Item().Text($"Assurance : {r.AssuranceDzd:N2} DZD");
                    c.Item().Text($"Autres frais : {r.AutresFraisDzd:N2} DZD");
                    c.Item().Text($"Valeur en douane : {r.ValeurDouaniereDzd:N2} DZD").Bold();
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text($"Droits de douane : {r.TotalDroitsDouaneDzd:N2} DZD");
                    c.Item().Text($"Autres taxes : {r.TotalAutresTaxesDzd:N2} DZD");
                    c.Item().Text($"TVA import : {r.TotalTvaDzd:N2} DZD");
                    c.Item().Text($"Total droits et taxes : {r.TotalDroitsEtTaxesDzd:N2} DZD").Bold();
                    c.Item().Text($"TVA non récupérable : {(r.IsVatNonRecoverable ? "OUI" : "NON")}");
                });
            });
            col.Item().PaddingTop(6).Text($"COÛT TOTAL DE REVIENT : {r.CoutTotalRevientDzd:N2} DZD").Bold().FontSize(12);
            col.Item().Text($"Coût moyen unitaire : {r.CoutMoyenUnitaireDzd:N2} DZD / unité").Bold();

            if (r.DeviseAutorisation != null && r.MontantAutorisation.HasValue)
            {
                col.Item().PaddingTop(6).Text("CONVERSION COMMERCIALE (AUTORISATION D'IMPORTATION)").Bold().FontSize(9);
                col.Item().Text($"Montant facture : {r.MontantOriginal:N2} {r.DeviseOriginale}  →  Montant équivalent : {r.MontantAutorisation.Value:N2} {r.DeviseAutorisation} (taux {r.TauxChangeAutorisation:F4}, {r.TypeDeTauxAutorisationFr})").FontSize(8);
            }
        });
    }

    private static void BuildFeesSection(QuestPDF.Infrastructure.IContainer container, PdfProfessionalReportModel m)
    {
        if (m.FeesBreakdown.Count == 0) return;
        container.Column(col =>
        {
            col.Item().Text("FRAIS D'APPROCHE").Bold().FontSize(11);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2f);
                    c.RelativeColumn(1.5f);
                    c.RelativeColumn(1.5f);
                    c.RelativeColumn(1f);
                    c.RelativeColumn(1f);
                });
                table.Header(h =>
                {
                    foreach (var t in new[] { "Frais", "Montant (DZD)", "Méthode de répartition", "Val. Douane", "Coût Revient" })
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(t).Bold().FontSize(7.5f);
                });
                foreach (var f in m.FeesBreakdown)
                {
                    table.Cell().Padding(2).Text(f.FeeName).FontSize(7.5f);
                    table.Cell().Padding(2).Text(f.AmountConvertedDzd.ToString("N2")).FontSize(7.5f);
                    table.Cell().Padding(2).Text(f.AllocationMethod).FontSize(7.5f);
                    table.Cell().Padding(2).Text(f.IncludedInCustomsValue ? "OUI" : "NON").FontSize(7.5f);
                    table.Cell().Padding(2).Text(f.IncludedInCostOfGoods ? "OUI" : "NON").FontSize(7.5f);
                }
            });
        });
    }

    private static void BuildTaxesSection(QuestPDF.Infrastructure.IContainer container, PdfProfessionalReportModel m)
    {
        if (m.TaxesBreakdown.Count == 0) return;
        container.Column(col =>
        {
            col.Item().Text("DROITS, TAXES ET SOURCES RÉGLEMENTAIRES APPLIQUÉES").Bold().FontSize(11);
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.6f);
                    c.RelativeColumn(1f);
                    c.RelativeColumn(1f);
                    c.RelativeColumn(3.4f);
                });
                table.Header(h =>
                {
                    foreach (var t in new[] { "Référence", "Taxe", "Taux / Montant", "Source légale" })
                        h.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(t).Bold().FontSize(7.5f);
                });
                foreach (var t in m.TaxesBreakdown)
                {
                    table.Cell().Padding(2).Text(t.ProductReference).FontSize(7.5f);
                    table.Cell().Padding(2).Text(t.TaxNameFr).FontSize(7.5f);
                    table.Cell().Padding(2).Text($"{t.RatePercent:F2} % = {t.TaxAmountDzd:N2} DZD").FontSize(7.5f);
                    table.Cell().Padding(2).Text($"{t.LegalSourceAndArticle} (v.{t.RegulatoryVersionCode})").FontSize(7.5f);
                }
            });
        });
    }

    private static void BuildControlsSection(QuestPDF.Infrastructure.IContainer container, PdfProfessionalReportModel m)
    {
        container.Column(col =>
        {
            col.Item().Text("CONTRÔLES ET ANOMALIES").Bold().FontSize(11);
            if (m.ControlsAndAlerts.Count == 0)
            {
                col.Item().Text("Aucune anomalie détectée lors du calcul.");
                return;
            }
            foreach (var a in m.ControlsAndAlerts)
            {
                col.Item().Text($"[{a.Severity}] {a.AnomalyCode} — {a.MessageFr}").FontSize(8);
            }
        });
    }

    private static void BuildLegalSection(QuestPDF.Infrastructure.IContainer container, PdfProfessionalReportModel m)
    {
        container.PaddingTop(8).BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(4).Column(col =>
        {
            col.Item().Text("MENTION LÉGALE OBLIGATOIRE").Bold().FontSize(9);
            col.Item().Text(m.MandatoryLegalDisclaimerFr).FontSize(7.5f).Italic();
        });
    }
}
