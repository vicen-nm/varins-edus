using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;

namespace VarinsEdu.Infrastructure.Imports;

// Saves an import: students, groups and activation codes, all or nothing.
public class StudentImportCommitter(
    AppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    ActivationCodeHasher codeHasher,
    AuditSuppression auditSuppression,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromDays(30);

    private const int MaxListedErrors = 100;

    // The server re-reads and re-validates the file: it never trusts a previous preview.
    // Rows with errors are reported and skipped; people who already exist are not touched.
    public async Task<ImportResult> CommitAsync(
        Stream file,
        string fileName,
        IReadOnlyDictionary<string, string>? mapping,
        CancellationToken ct = default)
    {
        if (tenant.InstitutionId is not { } institutionId)
        {
            throw new TenantViolationException("Only institution users can import students.");
        }

        var userId = currentUser.UserId
            ?? throw new InvalidOperationException("An authenticated user is required to import students.");

        var sheet = ExcelReader.Read(file);
        var usedMapping = mapping ?? ColumnMapper.Suggest(sheet.Headers);

        // The tenant filter limits both queries to this institution.
        var existingKeys = (await db.People
                .Select(p => p.IdentificationType + "|" + p.IdentificationNumber)
                .ToListAsync(ct))
            .ToHashSet();

        var existingGroups = await db.Groups
            .AsNoTracking()
            .Select(g => new { g.Id, g.Name })
            .ToListAsync(ct);

        var analysis = StudentImportAnalyzer.Analyze(
            sheet,
            usedMapping,
            existingKeys,
            existingGroups.Select(g => g.Name).ToList());

        var now = timeProvider.GetUtcNow();

        // The in-memory test database does not support transactions, so only relational providers get one.
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational())
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
        }

        try
        {
            var batch = new ImportBatch
            {
                Id = Guid.NewGuid(),
                InstitutionId = institutionId,
                UserId = userId,
                FileName = SafeFileName(fileName),
                Status = ImportStatuses.Completed,
                ColumnMapping = JsonSerializer.Serialize(usedMapping),
                TotalRows = sheet.Rows.Count,
                OkRows = analysis.NewStudents.Count,
                ErrorRows = analysis.Errors.Count,
                CreatedAt = now
            };

            // The batch is audited as one entry, with its counts.
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync(ct);

            var issuedCodes = new List<IssuedCode>();

            // The rows below are not audited one by one: the batch above already tells the story.
            using (auditSuppression.Begin())
            {
                var groupIds = existingGroups.ToDictionary(g => g.Name, g => g.Id, StringComparer.OrdinalIgnoreCase);

                foreach (var name in analysis.GroupsToCreate)
                {
                    var group = new Group { Id = Guid.NewGuid(), InstitutionId = institutionId, Name = name };
                    db.Groups.Add(group);
                    groupIds[name] = group.Id;
                }

                foreach (var student in analysis.NewStudents)
                {
                    var person = new Person
                    {
                        Id = Guid.NewGuid(),
                        InstitutionId = institutionId,
                        IdentificationType = student.IdentificationType,
                        IdentificationNumber = student.IdentificationNumber,
                        FirstName = student.FirstName,
                        LastName = student.LastName,
                        Phone = student.Phone,
                        Email = student.Email,
                        IsActive = true,
                        CreatedAt = now
                    };
                    db.People.Add(person);

                    db.StudentProfiles.Add(new StudentProfile
                    {
                        PersonId = person.Id,
                        InstitutionId = institutionId,
                        GroupId = student.GroupName is null ? null : groupIds[student.GroupName],
                        Status = StudentStatuses.Active
                    });

                    var code = ActivationCodeGenerator.Generate();

                    db.ActivationCodes.Add(new ActivationCode
                    {
                        Id = Guid.NewGuid(),
                        InstitutionId = institutionId,
                        PersonId = person.Id,
                        CodeHash = codeHasher.Hash(code),
                        ExpiresAt = now.Add(CodeLifetime),
                        ImportBatchId = batch.Id,
                        CreatedAt = now
                    });

                    issuedCodes.Add(new IssuedCode(
                        student.IdentificationNumber,
                        student.FirstName,
                        student.LastName,
                        student.GroupName,
                        code));
                }

                await db.SaveChangesAsync(ct);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }

            return new ImportResult(
                batch.Id,
                analysis.NewStudents.Count,
                analysis.ExistingStudents.Count,
                analysis.Errors.Count,
                analysis.Errors.Take(MaxListedErrors).ToList(),
                issuedCodes);
        }
        finally
        {
            if (transaction is not null)
            {
                // Without a commit, disposing the transaction rolls everything back.
                await transaction.DisposeAsync();
            }
        }
    }

    // The name comes from the user: keep only the file name, never a path, and limit its length.
    private static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);

        if (string.IsNullOrWhiteSpace(name))
        {
            return "import.xlsx";
        }

        return name.Length > 255 ? name[..255] : name;
    }
}
