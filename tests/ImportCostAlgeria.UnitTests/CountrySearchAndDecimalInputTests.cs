using System;
using System.Linq;
using Xunit;
using ImportCostAlgeria.Core.Domain;
using ImportCostAlgeria.Core.Services;

namespace ImportCostAlgeria.UnitTests;

/// <summary>
/// Revue du 2026-10-01 : tests des points 2 (recherche de pays d'origine / d'expédition) et 3 (saisie
/// des taux de change avec virgule ou point décimal) de la revue CIMP. Les contrôles WPF eux-mêmes
/// (CountrySearchBox, FlexibleDecimalConverter) ne sont pas testables depuis ce projet de tests
/// multiplateforme (net8.0, sans référence WPF — voir ImportCostAlgeria.Presentation, net8.0-windows) ;
/// ces tests couvrent donc directement la logique métier centralisée et réellement partagée
/// (ImportCostAlgeria.Core.Domain.CountryCatalog et ImportCostAlgeria.Core.Services.FlexibleDecimalParser)
/// dont dépendent ces contrôles.
/// </summary>
public class CountrySearchAndDecimalInputTests
{
    // ------------------------------------------------------------------
    // A. Pays (point 2 / point 9.A de la revue)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("alg", "DZ", "Algérie")]
    [InlineData("chi", "CN", "Chine")]
    [InlineData("tur", "TR", "Turquie")]
    [InlineData("fra", "FR", "France")]
    public void Search_ShouldFindExpectedCountry_ForPartialFrenchNamePrefix(string query, string expectedIso2, string expectedNameFr)
    {
        var results = CountryCatalog.Search(query);

        Assert.Contains(results, c => c.Iso2 == expectedIso2 && c.NameFr == expectedNameFr);
    }

    [Fact]
    public void Search_ShouldNotBeLimitedToTwoCharacters_AndShouldFilterInstantly()
    {
        // La recherche doit fonctionner aussi bien avec 2 lettres qu'avec une saisie plus longue
        // (contrairement à l'ancien TextBox MaxLength="2") : "algerie" doit toujours trouver l'Algérie.
        var shortQuery = CountryCatalog.Search("al");
        var longQuery = CountryCatalog.Search("algerie");

        Assert.Contains(shortQuery, c => c.Iso2 == "DZ");
        Assert.Contains(longQuery, c => c.Iso2 == "DZ");
    }

    [Fact]
    public void Search_ShouldBeAccentInsensitive()
    {
        // "Algérie" contient un accent ; l'utilisateur doit pouvoir le retrouver sans le taper.
        var results = CountryCatalog.Search("alger");
        Assert.Contains(results, c => c.Iso2 == "DZ");
    }

    [Fact]
    public void Search_ShouldAlsoMatchByIso2Code()
    {
        var results = CountryCatalog.Search("DZ");
        Assert.Contains(results, c => c.Iso2 == "DZ");
    }

    [Fact]
    public void FindByIso2_ShouldReturnDisplayLabel_WithNameAndIsoCode()
    {
        var country = CountryCatalog.FindByIso2("dz"); // insensible à la casse
        Assert.NotNull(country);
        Assert.Equal("Algérie (DZ)", country!.DisplayLabel);
    }

    [Fact]
    public void Catalog_ShouldNotContainDuplicateIso2Codes()
    {
        var duplicates = CountryCatalog.All
            .GroupBy(c => c.Iso2, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void OriginAndShippingCountryFields_ShouldRemainIndependent()
    {
        // Les champs "Pays d'origine" et "Pays d'expédition" sont deux propriétés indépendantes de
        // ImportOperation : modifier l'une ne doit jamais affecter l'autre (section 9.A de la revue).
        var operation = new ImportOperation
        {
            CompanyId = Guid.NewGuid(),
            ImportNumber = "IMP-TEST-0001",
            ReferenceDate = DateOnly.FromDateTime(DateTime.Today),
            SupplierName = "Fournisseur Test",
            ExportShippingCountryIso2 = "CN",
            DefaultOriginCountryIso2 = "FR",
            MainCurrencyCode = "EUR",
            Incoterm = IncotermCode.FOB,
            ArrivalPortOrBorder = "Port d'Alger",
            TransportMode = "Maritime"
        };

        Assert.Equal("CN", operation.ExportShippingCountryIso2);
        Assert.Equal("FR", operation.DefaultOriginCountryIso2);

        operation.DefaultOriginCountryIso2 = "DZ";

        Assert.Equal("DZ", operation.DefaultOriginCountryIso2);
        Assert.Equal("CN", operation.ExportShippingCountryIso2); // inchangé : indépendance confirmée.
    }

    // ------------------------------------------------------------------
    // B. Taux de change — virgule / point décimal (point 3 / point 9.B de la revue)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("1,17", 1.17)]
    [InlineData("1.17", 1.17)]
    [InlineData("152,1552", 152.1552)]
    [InlineData("152.1552", 152.1552)]
    [InlineData(" 1,17 ", 1.17)] // espaces superflus tolérés
    public void TryParse_ShouldAcceptBothCommaAndDotAsDecimalSeparator(string input, double expectedAsDouble)
    {
        bool success = FlexibleDecimalParser.TryParse(input, out decimal result);

        Assert.True(success, $"La saisie '{input}' aurait dû être acceptée comme nombre decimal.");
        Assert.Equal((decimal)expectedAsDouble, result);
    }

    [Fact]
    public void TryParse_ShouldPreserveExactDecimalPrecision_ForFourDecimalPlaces()
    {
        // Vérifie explicitement qu'aucune conversion via double/float n'introduit d'imprécision binaire :
        // 152,1552 doit rester EXACTEMENT 152.1552m (type decimal), jamais 152.15519999...
        Assert.True(FlexibleDecimalParser.TryParse("152,1552", out decimal commaResult));
        Assert.True(FlexibleDecimalParser.TryParse("152.1552", out decimal dotResult));

        Assert.Equal(152.1552m, commaResult);
        Assert.Equal(152.1552m, dotResult);
        Assert.Equal(commaResult, dotResult);
        Assert.IsType<decimal>(commaResult);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1,2,3")]
    public void TryParse_ShouldRejectInvalidInput_WithoutInventingAValue(string? input)
    {
        bool success = FlexibleDecimalParser.TryParse(input, out decimal result);

        Assert.False(success);
        Assert.Equal(0m, result); // valeur de sortie neutre, jamais utilisée côté appelant quand success=false.
    }
}
