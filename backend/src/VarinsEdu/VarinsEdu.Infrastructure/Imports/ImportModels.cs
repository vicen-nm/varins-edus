namespace VarinsEdu.Infrastructure.Imports;

public sealed record ExcelRow(int Number, IReadOnlyList<string> Cells);

public sealed record ExcelSheet(IReadOnlyList<string> Headers, IReadOnlyList<ExcelRow> Rows);

public sealed record StudentRow(
    int RowNumber,
    string IdentificationType,
    string IdentificationNumber,
    string FirstName,
    string LastName,
    string? GroupName,
    string? Phone,
    string? Email);

public sealed record RowError(int RowNumber, string Code);

public sealed record ImportAnalysis(
    IReadOnlyList<StudentRow> NewStudents,
    IReadOnlyList<StudentRow> ExistingStudents,
    IReadOnlyList<RowError> Errors,
    IReadOnlyList<string> GroupsToCreate);

public sealed record PreviewSummary(
    int NewStudents,
    int ExistingStudents,
    int ErrorRows,
    IReadOnlyList<RowError> Errors,
    IReadOnlyList<string> GroupsToCreate);

public sealed record ImportPreview(
    IReadOnlyList<string> Headers,
    IReadOnlyDictionary<string, string> SuggestedMapping,
    IReadOnlyDictionary<string, string> Mapping,
    IReadOnlyList<string> MissingFields,
    int TotalRows,
    IReadOnlyList<IReadOnlyList<string>> Sample,
    PreviewSummary? Summary);
