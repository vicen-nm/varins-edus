using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Persistence;

namespace VarinsEdu.Infrastructure.Imports;

public class StudentImportService(AppDbContext db, ICurrentTenant tenant)
{
    private const int SampleRows = 5;
    private const int MaxListedErrors = 100;

    // Shows what an import would do. It saves nothing.
    public async Task<ImportPreview> PreviewAsync(
        Stream file,
        IReadOnlyDictionary<string, string>? mapping,
        CancellationToken ct = default)
    {
        if (tenant.InstitutionId is null)
        {
            throw new TenantViolationException("Only institution users can import students.");
        }

        var sheet = ExcelReader.Read(file);

        var suggested = ColumnMapper.Suggest(sheet.Headers);
        var used = mapping ?? suggested;

        var sample = sheet.Rows.Take(SampleRows).Select(row => row.Cells).ToList();
        var missing = StudentImportFields.Required.Where(field => !used.ContainsKey(field)).ToList();

        if (missing.Count > 0)
        {
            return new ImportPreview(sheet.Headers, suggested, used, missing, sheet.Rows.Count, sample, null);
        }

        // The tenant filter limits both queries to the caller's institution.
        var existingKeys = (await db.People
                .Select(p => p.IdentificationType + "|" + p.IdentificationNumber)
                .ToListAsync(ct))
            .ToHashSet();

        var existingGroups = await db.Groups.Select(g => g.Name).ToListAsync(ct);

        var analysis = StudentImportAnalyzer.Analyze(sheet, used, existingKeys, existingGroups);

        var summary = new PreviewSummary(
            analysis.NewStudents.Count,
            analysis.ExistingStudents.Count,
            analysis.Errors.Count,
            analysis.Errors.Take(MaxListedErrors).ToList(),
            analysis.GroupsToCreate);

        return new ImportPreview(sheet.Headers, suggested, used, missing, sheet.Rows.Count, sample, summary);
    }
}
