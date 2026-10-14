using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Imports;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class StudentImportCommitTests
{
    private static readonly ActivationCodeHasher Hasher = new(new string('k', 48));

    private sealed class FakeTenant(Guid? institutionId) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope => false;
    }

    private sealed class FakeUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public string? IpAddress => "203.0.113.7";
    }

    private static DbContextOptions<AppDbContext> PlainOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static StudentImportCommitter CommitterFor(
        DbContextOptions<AppDbContext> options,
        Guid? institutionId,
        Guid? userId = null,
        AuditSuppression? suppression = null)
    {
        var tenant = new FakeTenant(institutionId);

        return new StudentImportCommitter(
            new AppDbContext(options, tenant),
            tenant,
            new FakeUser(userId ?? Guid.NewGuid()),
            Hasher,
            suppression ?? new AuditSuppression(),
            TimeProvider.System);
    }

    // Reads everything, so tests can inspect what was saved.
    private static AppDbContext Reader(DbContextOptions<AppDbContext> options) =>
        new(options, new PlatformTenant());

    // 6 data rows: 3 valid new students, an invalid email, a repeated cédula and a missing first name.
    private static MemoryStream SampleFile() => TestWorkbooks.Create(
        new[] { "Cédula", "Nombre", "Apellidos", "Sección", "Teléfono", "Correo" },
        new[] { "1-1111-1111", "Ana", "Prueba Uno", "10-1", "8888-0001", "ana.prueba@example.com" },
        new[] { "2-2222-2222", "Beto", "Prueba Dos", "10-1", "", "" },
        new[] { "3-3333-3333", "Carla", "Prueba Tres", "10-2", "", "carla@example" },
        new[] { "1-1111-1111", "Ana", "Repetida", "10-1", "", "" },
        new[] { "4-4444-4444", "", "Prueba Cuatro", "10-2", "", "" },
        new[] { "5-5555-5555", "Elena", "Prueba Cinco", "10-3", "", "" });

    [Fact]
    public async Task Commit_creates_students_profiles_groups_and_codes()
    {
        var options = PlainOptions();
        var institutionId = Guid.NewGuid();
        using var file = SampleFile();

        var result = await CommitterFor(options, institutionId).CommitAsync(file, "estudiantes.xlsx", null);

        Assert.Equal(3, result.Created);
        Assert.Equal(0, result.AlreadyExisted);
        Assert.Equal(3, result.ErrorRows);
        Assert.Equal(3, result.Codes.Count);

        using var db = Reader(options);

        var people = await db.People.ToListAsync();
        Assert.Equal(3, people.Count);
        Assert.All(people, p => Assert.Equal(institutionId, p.InstitutionId));

        Assert.Equal(3, await db.StudentProfiles.CountAsync());
        Assert.Equal(3, await db.ActivationCodes.CountAsync());
        Assert.Equal(new[] { "10-1", "10-3" }, (await db.Groups.Select(g => g.Name).ToListAsync()).Order());

        // Ana is placed in the group created for "10-1".
        var ana = people.Single(p => p.IdentificationNumber == "111111111");
        var profile = await db.StudentProfiles.SingleAsync(s => s.PersonId == ana.Id);
        var group = await db.Groups.SingleAsync(g => g.Id == profile.GroupId);

        Assert.Equal("10-1", group.Name);
        Assert.Equal(StudentStatuses.Active, profile.Status);
    }

    [Fact]
    public async Task Only_fingerprints_of_the_codes_are_stored()
    {
        var options = PlainOptions();
        using var file = SampleFile();

        var result = await CommitterFor(options, Guid.NewGuid()).CommitAsync(file, "estudiantes.xlsx", null);

        using var db = Reader(options);
        var people = await db.People.ToListAsync();
        var storedCodes = await db.ActivationCodes.ToListAsync();

        foreach (var issued in result.Codes)
        {
            var person = people.Single(p => p.IdentificationNumber == issued.IdentificationNumber);
            var stored = storedCodes.Single(c => c.PersonId == person.Id);

            Assert.NotEqual(issued.Code, stored.CodeHash);
            Assert.True(Hasher.Verify(stored.CodeHash, issued.Code));
        }

        Assert.All(storedCodes, c => Assert.Null(c.UsedAt));
        Assert.All(storedCodes, c => Assert.Equal(0, c.FailedAttempts));
        Assert.All(storedCodes, c => Assert.True(c.ExpiresAt > DateTimeOffset.UtcNow.AddDays(29)));
    }

    [Fact]
    public async Task Importing_the_same_file_twice_creates_no_duplicates_and_no_new_codes()
    {
        var options = PlainOptions();
        var institutionId = Guid.NewGuid();

        using (var first = SampleFile())
        {
            await CommitterFor(options, institutionId).CommitAsync(first, "estudiantes.xlsx", null);
        }

        using var second = SampleFile();
        var result = await CommitterFor(options, institutionId).CommitAsync(second, "estudiantes.xlsx", null);

        Assert.Equal(0, result.Created);
        Assert.Equal(3, result.AlreadyExisted);
        Assert.Empty(result.Codes);

        using var db = Reader(options);
        Assert.Equal(3, await db.People.CountAsync());
        Assert.Equal(3, await db.ActivationCodes.CountAsync());
        Assert.Equal(2, await db.ImportBatches.CountAsync());
    }

    [Fact]
    public async Task The_batch_records_the_file_the_mapping_and_the_counts()
    {
        var options = PlainOptions();
        var userId = Guid.NewGuid();
        using var file = SampleFile();

        var result = await CommitterFor(options, Guid.NewGuid(), userId).CommitAsync(file, "estudiantes.xlsx", null);

        using var db = Reader(options);
        var batch = await db.ImportBatches.SingleAsync();

        Assert.Equal(result.BatchId, batch.Id);
        Assert.Equal(userId, batch.UserId);
        Assert.Equal("estudiantes.xlsx", batch.FileName);
        Assert.Equal(ImportStatuses.Completed, batch.Status);
        Assert.Equal(6, batch.TotalRows);
        Assert.Equal(3, batch.OkRows);
        Assert.Equal(3, batch.ErrorRows);
        Assert.Contains("identificationNumber", batch.ColumnMapping);
    }

    [Fact]
    public async Task Bulk_rows_are_not_audited_one_by_one()
    {
        var institutionId = Guid.NewGuid();
        var tenant = new FakeTenant(institutionId);
        var user = new FakeUser(Guid.NewGuid());
        var suppression = new AuditSuppression();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(tenant, user, TimeProvider.System, suppression))
            .Options;

        var committer = new StudentImportCommitter(
            new AppDbContext(options, tenant), tenant, user, Hasher, suppression, TimeProvider.System);

        using var file = SampleFile();
        await committer.CommitAsync(file, "estudiantes.xlsx", null);

        using var db = Reader(options);
        var audit = Assert.Single(await db.AuditLogs.ToListAsync());

        Assert.Equal("ImportBatch.created", audit.Action);
        Assert.DoesNotContain("Prueba", audit.Metadata);

        // Suppression must always end with the import.
        Assert.False(suppression.IsSuppressed);

        // The students themselves were saved.
        Assert.Equal(3, await db.People.CountAsync());
    }

    [Fact]
    public async Task Each_institution_imports_independently()
    {
        var options = PlainOptions();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using (var forA = SampleFile())
        {
            await CommitterFor(options, a).CommitAsync(forA, "a.xlsx", null);
        }

        using var forB = SampleFile();
        var resultB = await CommitterFor(options, b).CommitAsync(forB, "b.xlsx", null);

        // The same cédulas are new for B: people are unique per institution.
        Assert.Equal(3, resultB.Created);

        using var db = Reader(options);
        var people = await db.People.ToListAsync();

        Assert.Equal(3, people.Count(p => p.InstitutionId == a));
        Assert.Equal(3, people.Count(p => p.InstitutionId == b));
    }

    [Fact]
    public async Task Platform_users_cannot_commit_imports()
    {
        using var file = SampleFile();

        await Assert.ThrowsAsync<TenantViolationException>(() =>
            CommitterFor(PlainOptions(), null).CommitAsync(file, "estudiantes.xlsx", null));
    }

    [Fact]
    public async Task An_invalid_mapping_saves_nothing()
    {
        var options = PlainOptions();
        var mapping = new Dictionary<string, string>
        {
            [StudentImportFields.FirstName] = "Nombre",
            [StudentImportFields.LastName] = "Apellidos"
        };

        using var file = SampleFile();

        var error = await Assert.ThrowsAsync<ImportFileException>(() =>
            CommitterFor(options, Guid.NewGuid()).CommitAsync(file, "estudiantes.xlsx", mapping));

        Assert.Equal("missing_required_mapping", error.Code);

        using var db = Reader(options);
        Assert.Empty(await db.People.ToListAsync());
        Assert.Empty(await db.ImportBatches.ToListAsync());
    }
}
