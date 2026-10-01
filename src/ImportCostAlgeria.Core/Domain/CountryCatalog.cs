using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ImportCostAlgeria.Core.Domain;

/// <summary>
/// Revue du 2026-10-01 (point 2 — pays d'origine / pays d'expédition) : représentation structurée et
/// unique d'un pays (code ISO 3166-1 alpha-2 + désignation en français). Le code ISO2 reste
/// l'identifiant stocké en base (voir <see cref="ImportOperation.ExportShippingCountryIso2"/>,
/// <see cref="ImportOperation.DefaultOriginCountryIso2"/>, <see cref="ImportOperationLine.OriginCountryIso2"/>)
/// mais il ne doit JAMAIS limiter la saisie utilisateur (ni en longueur, ni en recherche) : il sert
/// uniquement de clé technique une fois le pays choisi dans la liste complète via <see cref="CountryCatalog"/>.
/// </summary>
public sealed record CountryInfo(string Iso2, string NameFr)
{
    /// <summary>Libellé d'affichage recommandé par la revue : "Nom du pays (CODE)", ex. "Algérie (DZ)".</summary>
    public string DisplayLabel => $"{NameFr} ({Iso2})";
}

/// <summary>
/// Source structurée UNIQUE de la liste des pays (ISO 3166-1 alpha-2 + désignations officielles en
/// français, issues des données de référence Unicode CLDR) : évite tout doublon ou liste divergente
/// entre les différents écrans (Importation, Réglementation, assistants Excel, etc.).
/// Volontairement un module Core (aucune dépendance WPF) afin de rester réutilisable par n'importe quel
/// projet de la solution (Presentation, ExcelEngine, RegulatoryEngine, tests...).
/// </summary>
public static class CountryCatalog
{
    /// <summary>Liste complète, triée par désignation française, des pays/territoires ISO 3166-1 alpha-2.</summary>
    public static IReadOnlyList<CountryInfo> All { get; } = new List<CountryInfo>
    {
        new("AF", "Afghanistan"),
        new("ZA", "Afrique du Sud"),
        new("AL", "Albanie"),
        new("DZ", "Algérie"),
        new("DE", "Allemagne"),
        new("AD", "Andorre"),
        new("AO", "Angola"),
        new("AI", "Anguilla"),
        new("AQ", "Antarctique"),
        new("AG", "Antigua-et-Barbuda"),
        new("SA", "Arabie saoudite"),
        new("AR", "Argentine"),
        new("AM", "Arménie"),
        new("AW", "Aruba"),
        new("AU", "Australie"),
        new("AT", "Autriche"),
        new("AZ", "Azerbaïdjan"),
        new("BS", "Bahamas"),
        new("BH", "Bahreïn"),
        new("BD", "Bangladesh"),
        new("BB", "Barbade"),
        new("BE", "Belgique"),
        new("BZ", "Belize"),
        new("BM", "Bermudes"),
        new("BT", "Bhoutan"),
        new("BY", "Biélorussie"),
        new("BO", "Bolivie"),
        new("BA", "Bosnie-Herzégovine"),
        new("BW", "Botswana"),
        new("BN", "Brunei"),
        new("BR", "Brésil"),
        new("BG", "Bulgarie"),
        new("BF", "Burkina Faso"),
        new("BI", "Burundi"),
        new("BJ", "Bénin"),
        new("KH", "Cambodge"),
        new("CM", "Cameroun"),
        new("CA", "Canada"),
        new("CV", "Cap-Vert"),
        new("EA", "Ceuta et Melilla"),
        new("CL", "Chili"),
        new("CN", "Chine"),
        new("CY", "Chypre"),
        new("CO", "Colombie"),
        new("KM", "Comores"),
        new("CG", "Congo-Brazzaville"),
        new("CD", "Congo-Kinshasa"),
        new("KP", "Corée du Nord"),
        new("KR", "Corée du Sud"),
        new("CR", "Costa Rica"),
        new("HR", "Croatie"),
        new("CU", "Cuba"),
        new("CW", "Curaçao"),
        new("CI", "Côte d’Ivoire"),
        new("DK", "Danemark"),
        new("DG", "Diego Garcia"),
        new("DJ", "Djibouti"),
        new("DM", "Dominique"),
        new("ES", "Espagne"),
        new("EE", "Estonie"),
        new("SZ", "Eswatini"),
        new("FJ", "Fidji"),
        new("FI", "Finlande"),
        new("FR", "France"),
        new("GA", "Gabon"),
        new("GM", "Gambie"),
        new("GH", "Ghana"),
        new("GI", "Gibraltar"),
        new("GD", "Grenade"),
        new("GL", "Groenland"),
        new("GR", "Grèce"),
        new("GP", "Guadeloupe"),
        new("GU", "Guam"),
        new("GT", "Guatemala"),
        new("GG", "Guernesey"),
        new("GN", "Guinée"),
        new("GQ", "Guinée équatoriale"),
        new("GW", "Guinée-Bissau"),
        new("GY", "Guyana"),
        new("GF", "Guyane française"),
        new("GE", "Géorgie"),
        new("GS", "Géorgie du Sud-et-les Îles Sandwich du Sud"),
        new("HT", "Haïti"),
        new("HN", "Honduras"),
        new("HU", "Hongrie"),
        new("IN", "Inde"),
        new("ID", "Indonésie"),
        new("IQ", "Irak"),
        new("IR", "Iran"),
        new("IE", "Irlande"),
        new("IS", "Islande"),
        new("IL", "Israël"),
        new("IT", "Italie"),
        new("JM", "Jamaïque"),
        new("JP", "Japon"),
        new("JE", "Jersey"),
        new("JO", "Jordanie"),
        new("KZ", "Kazakhstan"),
        new("KE", "Kenya"),
        new("KG", "Kirghizstan"),
        new("KI", "Kiribati"),
        new("XK", "Kosovo"),
        new("KW", "Koweït"),
        new("RE", "La Réunion"),
        new("LA", "Laos"),
        new("LS", "Lesotho"),
        new("LV", "Lettonie"),
        new("LB", "Liban"),
        new("LR", "Liberia"),
        new("LY", "Libye"),
        new("LI", "Liechtenstein"),
        new("LT", "Lituanie"),
        new("LU", "Luxembourg"),
        new("MK", "Macédoine du Nord"),
        new("MG", "Madagascar"),
        new("MY", "Malaisie"),
        new("MW", "Malawi"),
        new("MV", "Maldives"),
        new("ML", "Mali"),
        new("MT", "Malte"),
        new("MA", "Maroc"),
        new("MQ", "Martinique"),
        new("MU", "Maurice"),
        new("MR", "Mauritanie"),
        new("YT", "Mayotte"),
        new("MX", "Mexique"),
        new("FM", "Micronésie"),
        new("MD", "Moldavie"),
        new("MC", "Monaco"),
        new("MN", "Mongolie"),
        new("MS", "Montserrat"),
        new("ME", "Monténégro"),
        new("MZ", "Mozambique"),
        new("MM", "Myanmar (Birmanie)"),
        new("NA", "Namibie"),
        new("NR", "Nauru"),
        new("NI", "Nicaragua"),
        new("NE", "Niger"),
        new("NG", "Nigeria"),
        new("NU", "Niue"),
        new("NO", "Norvège"),
        new("NC", "Nouvelle-Calédonie"),
        new("NZ", "Nouvelle-Zélande"),
        new("NP", "Népal"),
        new("OM", "Oman"),
        new("UG", "Ouganda"),
        new("UZ", "Ouzbékistan"),
        new("PK", "Pakistan"),
        new("PW", "Palaos"),
        new("PA", "Panama"),
        new("PG", "Papouasie-Nouvelle-Guinée"),
        new("PY", "Paraguay"),
        new("NL", "Pays-Bas"),
        new("BQ", "Pays-Bas caribéens"),
        new("PH", "Philippines"),
        new("PL", "Pologne"),
        new("PF", "Polynésie française"),
        new("PR", "Porto Rico"),
        new("PT", "Portugal"),
        new("PE", "Pérou"),
        new("QA", "Qatar"),
        new("HK", "R.A.S. chinoise de Hong Kong"),
        new("MO", "R.A.S. chinoise de Macao"),
        new("RO", "Roumanie"),
        new("GB", "Royaume-Uni"),
        new("RU", "Russie"),
        new("RW", "Rwanda"),
        new("CF", "République centrafricaine"),
        new("DO", "République dominicaine"),
        new("EH", "Sahara occidental"),
        new("BL", "Saint-Barthélemy"),
        new("KN", "Saint-Christophe-et-Niévès"),
        new("SM", "Saint-Marin"),
        new("MF", "Saint-Martin"),
        new("SX", "Saint-Martin (partie néerlandaise)"),
        new("PM", "Saint-Pierre-et-Miquelon"),
        new("VC", "Saint-Vincent-et-les Grenadines"),
        new("SH", "Sainte-Hélène"),
        new("LC", "Sainte-Lucie"),
        new("SV", "Salvador"),
        new("WS", "Samoa"),
        new("AS", "Samoa américaines"),
        new("ST", "Sao Tomé-et-Principe"),
        new("RS", "Serbie"),
        new("SC", "Seychelles"),
        new("SL", "Sierra Leone"),
        new("SG", "Singapour"),
        new("SK", "Slovaquie"),
        new("SI", "Slovénie"),
        new("SO", "Somalie"),
        new("SD", "Soudan"),
        new("SS", "Soudan du Sud"),
        new("LK", "Sri Lanka"),
        new("CH", "Suisse"),
        new("SR", "Suriname"),
        new("SE", "Suède"),
        new("SJ", "Svalbard et Jan Mayen"),
        new("SY", "Syrie"),
        new("SN", "Sénégal"),
        new("TJ", "Tadjikistan"),
        new("TZ", "Tanzanie"),
        new("TW", "Taïwan"),
        new("TD", "Tchad"),
        new("CZ", "Tchéquie"),
        new("TF", "Terres australes françaises"),
        new("IO", "Territoire britannique de l’océan Indien"),
        new("PS", "Territoires palestiniens"),
        new("TH", "Thaïlande"),
        new("TL", "Timor oriental"),
        new("TG", "Togo"),
        new("TK", "Tokelau"),
        new("TO", "Tonga"),
        new("TT", "Trinité-et-Tobago"),
        new("TA", "Tristan da Cunha"),
        new("TN", "Tunisie"),
        new("TM", "Turkménistan"),
        new("TR", "Turquie"),
        new("TV", "Tuvalu"),
        new("UA", "Ukraine"),
        new("UY", "Uruguay"),
        new("VU", "Vanuatu"),
        new("VE", "Venezuela"),
        new("VN", "Viêt Nam"),
        new("WF", "Wallis-et-Futuna"),
        new("YE", "Yémen"),
        new("ZM", "Zambie"),
        new("ZW", "Zimbabwe"),
        new("EG", "Égypte"),
        new("AE", "Émirats arabes unis"),
        new("EC", "Équateur"),
        new("ER", "Érythrée"),
        new("VA", "État de la Cité du Vatican"),
        new("US", "États-Unis"),
        new("ET", "Éthiopie"),
        new("BV", "Île Bouvet"),
        new("CX", "Île Christmas"),
        new("CP", "Île Clipperton"),
        new("NF", "Île Norfolk"),
        new("IM", "Île de Man"),
        new("AC", "Île de l’Ascension"),
        new("IC", "Îles Canaries"),
        new("KY", "Îles Caïmans"),
        new("CC", "Îles Cocos"),
        new("CK", "Îles Cook"),
        new("FO", "Îles Féroé"),
        new("HM", "Îles Heard-et-MacDonald"),
        new("FK", "Îles Malouines"),
        new("MP", "Îles Mariannes du Nord"),
        new("MH", "Îles Marshall"),
        new("PN", "Îles Pitcairn"),
        new("SB", "Îles Salomon"),
        new("TC", "Îles Turques-et-Caïques"),
        new("VG", "Îles Vierges britanniques"),
        new("VI", "Îles Vierges des États-Unis"),
        new("UM", "Îles mineures éloignées des États-Unis"),
        new("AX", "Îles Åland"),
    };

    private static readonly Dictionary<string, CountryInfo> ByIso2 =
        All.ToDictionary(c => c.Iso2, StringComparer.OrdinalIgnoreCase);

    /// <summary>Retrouve un pays à partir de son code ISO2 (insensible à la casse), ou null si inconnu/absent.</summary>
    public static CountryInfo? FindByIso2(string? iso2)
    {
        if (string.IsNullOrWhiteSpace(iso2)) return null;
        return ByIso2.TryGetValue(iso2.Trim(), out var country) ? country : null;
    }

    /// <summary>
    /// Recherche dynamique NON limitée en nombre de caractères : filtre par sous-chaîne (insensible à la
    /// casse ET aux accents) sur la désignation française ou sur le code ISO2. Ex. "alg" -> Algérie (DZ),
    /// "chi" -> Chine (CN), "tur" -> Turquie (TR), "fra" -> France (FR).
    /// </summary>
    public static IReadOnlyList<CountryInfo> Search(string? filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText)) return All;

        string needle = RemoveDiacritics(filterText.Trim());
        return All.Where(c =>
                RemoveDiacritics(c.NameFr).Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                c.Iso2.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string RemoveDiacritics(string text)
    {
        string normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
