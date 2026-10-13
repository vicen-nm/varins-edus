using VarinsEdu.Domain.Constants;
using VarinsEdu.Infrastructure.Imports;
using Xunit;

namespace VarinsEdu.Tests;

public class StudentImportAnalyzerTests
{
    private static readonly string[] Headers = ["Cédula", "Nombre", "Apellidos", "Sección", "Correo"];

    private static readonly Dictionary<string, string> Mapping = new()
    {
        [StudentImportFields.IdentificationNumber] = "Cédula",
        [StudentImportFields.FirstName] = "Nombre",
        [StudentImportFields.LastName] = "Apellidos",
        [StudentImportFields.Group] = "Sección",
        [StudentImportFields.Email] = "Correo"
    };

    // Rows use Excel numbering: the header is row 1, so the first student is row 2.
    private static ExcelSheet Sheet(params string[][] rows) =>
        new(Headers, rows.Select((cells, index) => new ExcelRow(index + 2, cells)).ToList());

    private static ImportAnalysis Analyze(
        ExcelSheet sheet,
        HashSet<string>? existingKeys = null,
        params string[] existingGroups) =>
        StudentImportAnalyzer.Analyze(sheet, Mapping, existingKeys ?? new HashSet<string>(), existingGroups);

    [Fact]
    public void Valid_rows_become_new_students_with_clean_values()
    {
        var analysis = Analyze(Sheet(
            new[] { "1-1111-1111", "  Ana   María ", "Prueba  Uno", " 10-1 ", "ANA@Example.com" }));

        var student = Assert.Single(analysis.NewStudents);

        Assert.Equal(IdentificationTypes.Cedula, student.IdentificationType);
        Assert.Equal("111111111", student.IdentificationNumber);
        Assert.Equal("Ana María", student.FirstName);
        Assert.Equal("Prueba Uno", student.LastName);
        Assert.Equal("10-1", student.GroupName);
        Assert.Equal("ana@example.com", student.Email);
        Assert.Empty(analysis.Errors);
    }

    [Fact]
    public void People_who_already_exist_are_reported_and_not_imported_again()
    {
        var existing = new HashSet<string> { StudentImportAnalyzer.Key(IdentificationTypes.Cedula, "222222222") };

        var analysis = Analyze(
            Sheet(
                new[] { "1-1111-1111", "Ana", "Uno", "10-1", "" },
                new[] { "2-2222-2222", "Beto", "Dos", "10-1", "" }),
            existing);

        Assert.Equal("111111111", Assert.Single(analysis.NewStudents).IdentificationNumber);
        Assert.Equal("222222222", Assert.Single(analysis.ExistingStudents).IdentificationNumber);
    }

    [Fact]
    public void A_repeated_identification_inside_the_file_is_an_error()
    {
        var analysis = Analyze(Sheet(
            new[] { "1-1111-1111", "Ana", "Uno", "", "" },
            new[] { "1 1111 1111", "Ana", "Repetida", "", "" }));

        Assert.Single(analysis.NewStudents);

        var error = Assert.Single(analysis.Errors);
        Assert.Equal(3, error.RowNumber);
        Assert.Equal("duplicate_in_file", error.Code);
    }

    [Fact]
    public void Missing_required_values_are_errors_with_their_row_number()
    {
        var analysis = Analyze(Sheet(
            new[] { "", "Ana", "Uno", "", "" },
            new[] { "1", "", "Uno", "", "" },
            new[] { "2", "Beto", "", "", "" }));

        Assert.Empty(analysis.NewStudents);
        Assert.Equal(
            new[]
            {
                new RowError(2, "missing_identification_number"),
                new RowError(3, "missing_first_name"),
                new RowError(4, "missing_last_name")
            },
            analysis.Errors);
    }

    [Fact]
    public void An_invalid_email_is_an_error()
    {
        var analysis = Analyze(Sheet(new[] { "1", "Ana", "Uno", "", "no-es-un-correo" }));

        Assert.Empty(analysis.NewStudents);
        Assert.Equal("invalid_email", Assert.Single(analysis.Errors).Code);
    }

    [Fact]
    public void The_identification_type_is_read_from_the_file_when_it_is_mapped()
    {
        var sheet = new ExcelSheet(
            ["Cédula", "Tipo", "Nombre", "Apellidos"],
            [
                new ExcelRow(2, ["A123", "Pasaporte", "Ana", "Uno"]),
                new ExcelRow(3, ["B456", "Foo", "Beto", "Dos"])
            ]);

        var mapping = new Dictionary<string, string>(Mapping)
        {
            [StudentImportFields.IdentificationType] = "Tipo"
        };

        var analysis = StudentImportAnalyzer.Analyze(sheet, mapping, new HashSet<string>(), []);

        Assert.Equal(IdentificationTypes.Passport, Assert.Single(analysis.NewStudents).IdentificationType);
        Assert.Equal("invalid_identification_type", Assert.Single(analysis.Errors).Code);
    }

    [Fact]
    public void Groups_to_create_skip_the_existing_ones_ignoring_case()
    {
        var analysis = Analyze(
            Sheet(
                new[] { "1", "Ana", "Uno", "10-A", "" },
                new[] { "2", "Beto", "Dos", "10-B", "" },
                new[] { "3", "Carla", "Tres", "10-b", "" },
                new[] { "4", "Dario", "Cuatro", "", "" }),
            null,
            "10-a");

        Assert.Equal(new[] { "10-B" }, analysis.GroupsToCreate);
    }

    [Fact]
    public void A_mapping_without_a_required_field_is_rejected()
    {
        var incomplete = Mapping
            .Where(pair => pair.Key != StudentImportFields.FirstName)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        var error = Assert.Throws<ImportFileException>(() =>
            StudentImportAnalyzer.Analyze(
                Sheet(new[] { "1", "Ana", "Uno", "", "" }),
                incomplete,
                new HashSet<string>(),
                []));

        Assert.Equal("missing_required_mapping", error.Code);
    }

    [Fact]
    public void A_mapping_to_a_header_that_does_not_exist_is_rejected()
    {
        var wrong = new Dictionary<string, string>(Mapping)
        {
            [StudentImportFields.FirstName] = "Columna inexistente"
        };

        var error = Assert.Throws<ImportFileException>(() =>
            StudentImportAnalyzer.Analyze(
                Sheet(new[] { "1", "Ana", "Uno", "", "" }),
                wrong,
                new HashSet<string>(),
                []));

        Assert.Equal("invalid_mapping", error.Code);
    }
}
