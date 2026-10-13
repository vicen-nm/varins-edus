using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Imports;
using Xunit;

namespace VarinsEdu.Tests;

public class ColumnMapperTests
{
    [Fact]
    public void Suggests_columns_ignoring_accents_case_and_spaces()
    {
        var headers = new[] { "Cédula", "NOMBRE", " Apellidos ", "Sección", "Teléfono", "Correo electrónico", "Observaciones" };

        var suggestion = ColumnMapper.Suggest(headers);

        Assert.Equal("Cédula", suggestion[StudentImportFields.IdentificationNumber]);
        Assert.Equal("NOMBRE", suggestion[StudentImportFields.FirstName]);
        Assert.Equal(" Apellidos ", suggestion[StudentImportFields.LastName]);
        Assert.Equal("Sección", suggestion[StudentImportFields.Group]);
        Assert.Equal("Teléfono", suggestion[StudentImportFields.Phone]);
        Assert.Equal("Correo electrónico", suggestion[StudentImportFields.Email]);
        Assert.DoesNotContain("Observaciones", suggestion.Values);
    }

    [Fact]
    public void Maps_each_field_to_the_first_matching_column_only()
    {
        var suggestion = ColumnMapper.Suggest(new[] { "Nombre", "Nombres" });

        Assert.Equal("Nombre", suggestion[StudentImportFields.FirstName]);
        Assert.Single(suggestion);
    }

    [Fact]
    public void Suggests_nothing_when_no_header_matches()
    {
        var suggestion = ColumnMapper.Suggest(new[] { "Foo", "Bar", "" });

        Assert.Empty(suggestion);
    }
}
