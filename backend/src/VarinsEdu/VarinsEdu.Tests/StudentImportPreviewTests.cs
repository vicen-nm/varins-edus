using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Imports;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class StudentImportPreviewTests
{
    private sealed class FakeTenant(Guid? institutionId) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope => false;
    }

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static StudentImportService ServiceFor(DbContextOptions<AppDbContext> options, Guid? institutionId)
    {
        var tenant = new FakeTenant(institutionId);
        return new StudentImportService(new AppDbContext(options, tenant), tenant);
    }

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
    public async Task Preview_suggests_the_mapping_and_summarizes_the_import()
    {
        var options = NewOptions();
        using var file = SampleFile();

        var preview = await ServiceFor(options, Guid.NewGuid()).PreviewAsync(file, null);

        Assert.Empty(preview.MissingFields);
        Assert.Equal(6, preview.TotalRows);
        Assert.Equal(5, preview.Sample.Count);
        Assert.Equal("Cédula", preview.SuggestedMapping[StudentImportFields.IdentificationNumber]);
        Assert.Equal("Correo", preview.SuggestedMapping[StudentImportFields.Email]);

        var summary = Assert.IsType<PreviewSummary>(preview.Summary);
        Assert.Equal(3, summary.NewStudents);
        Assert.Equal(0, summary.ExistingStudents);
        Assert.Equal(3, summary.ErrorRows);
        Assert.Equal(new[] { "10-1", "10-3" }, summary.GroupsToCreate);
        Assert.Equal(
            new[]
            {
                new RowError(4, "invalid_email"),
                new RowError(5, "duplicate_in_file"),
                new RowError(6, "missing_first_name")
            },
            summary.Errors);
    }

    [Fact]
    public async Task Preview_does_not_save_anything()
    {
        var options = NewOptions();
        using var file = SampleFile();

        await ServiceFor(options, Guid.NewGuid()).PreviewAsync(file, null);

        using var db = new AppDbContext(options, new PlatformTenant());
        Assert.Empty(await db.People.ToListAsync());
        Assert.Empty(await db.Groups.ToListAsync());
        Assert.Empty(await db.ImportBatches.ToListAsync());
    }

    [Fact]
    public async Task Preview_marks_the_people_who_already_exist()
    {
        var options = NewOptions();
        var institutionId = Guid.NewGuid();

        using (var db = new AppDbContext(options, new PlatformTenant()))
        {
            db.Institutions.Add(new Institution { Id = institutionId, Name = "Test", Slug = "test" });
            db.People.Add(new Person
            {
                Id = Guid.NewGuid(),
                InstitutionId = institutionId,
                IdentificationType = IdentificationTypes.Cedula,
                IdentificationNumber = "111111111",
                FirstName = "Ana",
                LastName = "Ya Existe"
            });
            await db.SaveChangesAsync();
        }

        using var file = SampleFile();
        var preview = await ServiceFor(options, institutionId).PreviewAsync(file, null);

        var summary = Assert.IsType<PreviewSummary>(preview.Summary);
        Assert.Equal(2, summary.NewStudents);
        Assert.Equal(1, summary.ExistingStudents);
    }

    [Fact]
    public async Task Preview_reports_the_required_fields_that_are_not_mapped()
    {
        var mapping = new Dictionary<string, string>
        {
            [StudentImportFields.FirstName] = "Nombre",
            [StudentImportFields.LastName] = "Apellidos"
        };

        using var file = SampleFile();
        var preview = await ServiceFor(NewOptions(), Guid.NewGuid()).PreviewAsync(file, mapping);

        Assert.Equal(new[] { StudentImportFields.IdentificationNumber }, preview.MissingFields);
        Assert.Null(preview.Summary);
    }

    [Fact]
    public async Task Platform_users_cannot_import_students()
    {
        using var file = SampleFile();

        await Assert.ThrowsAsync<TenantViolationException>(() =>
            ServiceFor(NewOptions(), null).PreviewAsync(file, null));
    }
}
