using System.Text.RegularExpressions;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;

namespace VarinsEdu.Infrastructure.Imports;

// Validates every row of an Excel sheet. It touches no database and no HTTP, so it is easy to test.
public static class StudentImportAnalyzer
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> IdentificationTypeNames = new()
    {
        ["cedula"] = IdentificationTypes.Cedula,
        ["dimex"] = IdentificationTypes.Dimex,
        ["pasaporte"] = IdentificationTypes.Passport,
        ["passport"] = IdentificationTypes.Passport,
        ["otro"] = IdentificationTypes.Other,
        ["other"] = IdentificationTypes.Other
    };

    public static string Key(string type, string number) => $"{type}|{number}";

    // existingKeys holds "type|number" for every person already in the institution.
    public static ImportAnalysis Analyze(
        ExcelSheet sheet,
        IReadOnlyDictionary<string, string> mapping,
        IReadOnlySet<string> existingKeys,
        IReadOnlyCollection<string> existingGroupNames)
    {
        var columns = ResolveColumns(sheet.Headers, mapping);

        var newStudents = new List<StudentRow>();
        var existingStudents = new List<StudentRow>();
        var errors = new List<RowError>();
        var seen = new HashSet<string>();

        foreach (var row in sheet.Rows)
        {
            var (student, error) = ParseRow(row, columns);

            if (student is null)
            {
                errors.Add(new RowError(row.Number, error!));
                continue;
            }

            var key = Key(student.IdentificationType, student.IdentificationNumber);

            if (!seen.Add(key))
            {
                errors.Add(new RowError(row.Number, "duplicate_in_file"));
                continue;
            }

            if (existingKeys.Contains(key))
            {
                existingStudents.Add(student);
            }
            else
            {
                newStudents.Add(student);
            }
        }

        var groupsToCreate = newStudents
            .Where(s => s.GroupName is not null)
            .Select(s => s.GroupName!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !existingGroupNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new ImportAnalysis(newStudents, existingStudents, errors, groupsToCreate);
    }

    private static Dictionary<string, int> ResolveColumns(
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<string, string> mapping)
    {
        var columns = new Dictionary<string, int>();

        foreach (var (field, header) in mapping)
        {
            if (!StudentImportFields.All.Contains(field) || string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var index = IndexOfHeader(headers, header);

            if (index < 0)
            {
                throw new ImportFileException("invalid_mapping");
            }

            columns[field] = index;
        }

        if (StudentImportFields.Required.Any(field => !columns.ContainsKey(field)))
        {
            throw new ImportFileException("missing_required_mapping");
        }

        return columns;
    }

    private static int IndexOfHeader(IReadOnlyList<string> headers, string header)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i], header.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    // Returns the first problem found in the row, or the clean student.
    private static (StudentRow? Student, string? Error) ParseRow(ExcelRow row, IReadOnlyDictionary<string, int> columns)
    {
        string Cell(string field) => columns.TryGetValue(field, out var index) ? row.Cells[index] : string.Empty;

        var number = IdentificationNumbers.Normalize(Cell(StudentImportFields.IdentificationNumber));

        if (number.Length == 0)
        {
            return (null, "missing_identification_number");
        }

        if (number.Length > 50)
        {
            return (null, "identification_number_too_long");
        }

        var type = IdentificationTypes.Cedula;
        var typeText = Cell(StudentImportFields.IdentificationType);

        if (!string.IsNullOrWhiteSpace(typeText))
        {
            if (!IdentificationTypeNames.TryGetValue(ColumnMapper.Normalize(typeText), out var parsedType))
            {
                return (null, "invalid_identification_type");
            }

            type = parsedType;
        }

        var firstName = Clean(Cell(StudentImportFields.FirstName));

        if (firstName.Length == 0)
        {
            return (null, "missing_first_name");
        }

        if (firstName.Length > 100)
        {
            return (null, "first_name_too_long");
        }

        var lastName = Clean(Cell(StudentImportFields.LastName));

        if (lastName.Length == 0)
        {
            return (null, "missing_last_name");
        }

        if (lastName.Length > 150)
        {
            return (null, "last_name_too_long");
        }

        var group = Clean(Cell(StudentImportFields.Group));

        if (group.Length > 50)
        {
            return (null, "group_name_too_long");
        }

        var phone = Cell(StudentImportFields.Phone).Trim();

        if (phone.Length > 30)
        {
            return (null, "phone_too_long");
        }

        var email = Cell(StudentImportFields.Email).Trim().ToLowerInvariant();

        if (email.Length > 0 && (email.Length > 200 || !EmailPattern.IsMatch(email)))
        {
            return (null, "invalid_email");
        }

        var student = new StudentRow(
            row.Number,
            type,
            number,
            firstName,
            lastName,
            NullIfEmpty(group),
            NullIfEmpty(phone),
            NullIfEmpty(email));

        return (student, null);
    }

    // Trims and collapses repeated spaces: "  Ana   María " becomes "Ana María".
    private static string Clean(string text) => Spaces.Replace(text.Trim(), " ");

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;
}
