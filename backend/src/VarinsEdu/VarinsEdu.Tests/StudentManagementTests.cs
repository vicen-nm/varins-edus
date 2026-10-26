using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Students;
using Xunit;

namespace VarinsEdu.Tests;

public class StudentManagementTests
{
    private static readonly ActivationCodeHasher Hasher = new(new string('k', 48));

    private sealed class FakeTenant(Guid? institutionId) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope => false;
    }

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static StudentManagementService ServiceFor(DbContextOptions<AppDbContext> options, Guid? institutionId)
    {
        var tenant = new FakeTenant(institutionId);
        return new StudentManagementService(new AppDbContext(options, tenant), tenant, Hasher, TimeProvider.System);
    }

    private static AppDbContext Reader(DbContextOptions<AppDbContext> options) =>
        new(options, new PlatformTenant());

    private static async Task<Guid> AddStudentAsync(
        DbContextOptions<AppDbContext> options,
        Guid institutionId,
        string cedula,
        string first,
        string last,
        string? code,
        DateTimeOffset? expiresAt = null,
        int failedAttempts = 0)
    {
        using var db = Reader(options);
        var id = Guid.NewGuid();

        db.People.Add(new Person
        {
            Id = id,
            InstitutionId = institutionId,
            IdentificationNumber = cedula,
            FirstName = first,
            LastName = last,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.StudentProfiles.Add(new StudentProfile { PersonId = id, InstitutionId = institutionId });

        if (code is not null)
        {
            db.ActivationCodes.Add(new ActivationCode
            {
                Id = Guid.NewGuid(),
                InstitutionId = institutionId,
                PersonId = id,
                CodeHash = Hasher.Hash(code),
                ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(30),
                FailedAttempts = failedAttempts,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return id;
    }

    private static string StatusOf(StudentPage page, string cedula) =>
        page.Items.Single(i => i.IdentificationNumber == cedula).CodeStatus;

    [Fact]
    public async Task The_list_shows_the_status_of_each_code()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();

        await AddStudentAsync(options, inst, "111111111", "Ana", "Prueba Uno", "AAAA-2222");
        await AddStudentAsync(options, inst, "222222222", "Beto", "Prueba Dos", null);
        await AddStudentAsync(options, inst, "333333333", "Carla", "Prueba Tres", "CCCC-2222", DateTimeOffset.UtcNow.AddDays(-1));
        await AddStudentAsync(options, inst, "444444444", "Dina", "Prueba Cuatro", "DDDD-2222", failedAttempts: 5);

        var page = await ServiceFor(options, inst).ListAsync(null, null, null, 0, 50);

        Assert.Equal(4, page.Total);
        Assert.Equal("pending", StatusOf(page, "111111111"));
        Assert.Equal("none", StatusOf(page, "222222222"));
        Assert.Equal("expired", StatusOf(page, "333333333"));
        Assert.Equal("locked", StatusOf(page, "444444444"));
    }

    [Fact]
    public async Task Search_works_by_cedula_with_hyphens_and_by_name()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();

        await AddStudentAsync(options, inst, "111111111", "Ana", "Prueba Uno", null);
        await AddStudentAsync(options, inst, "444444444", "Dina", "Prueba Cuatro", null);

        var service = ServiceFor(options, inst);

        var byCedula = await service.ListAsync("1-1111-1111", null, null, 0, 50);
        var byName = await service.ListAsync("dina prueba", null, null, 0, 50);

        Assert.Equal("111111111", Assert.Single(byCedula.Items).IdentificationNumber);
        Assert.Equal("444444444", Assert.Single(byName.Items).IdentificationNumber);
    }

    [Fact]
    public async Task The_account_filter_separates_students_with_and_without_account()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();

        var ana = await AddStudentAsync(options, inst, "111111111", "Ana", "Prueba Uno", "AAAA-2222");
        await AddStudentAsync(options, inst, "222222222", "Beto", "Prueba Dos", "BBBB-2222");

        using (var db = Reader(options))
        {
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                InstitutionId = inst,
                PersonId = ana,
                Username = "111111111@demo-a",
                PasswordHash = "x"
            });
            await db.SaveChangesAsync();
        }

        var service = ServiceFor(options, inst);

        var with = await service.ListAsync(null, null, true, 0, 50);
        var without = await service.ListAsync(null, null, false, 0, 50);

        Assert.Equal("111111111", Assert.Single(with.Items).IdentificationNumber);
        Assert.Equal("222222222", Assert.Single(without.Items).IdentificationNumber);
    }

    [Fact]
    public async Task Regenerating_replaces_the_old_code_and_resets_attempts()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();
        var ana = await AddStudentAsync(options, inst, "111111111", "Ana", "Prueba Uno", "AAAA-2222", failedAttempts: 5);
        var service = ServiceFor(options, inst);

        var result = await service.RegenerateCodeAsync(ana);

        Assert.Null(result.Error);

        using var db = Reader(options);
        var stored = await db.ActivationCodes.SingleAsync();

        Assert.True(Hasher.Verify(stored.CodeHash, result.Code!));
        Assert.False(Hasher.Verify(stored.CodeHash, "AAAA-2222"));
        Assert.Equal(0, stored.FailedAttempts);
        Assert.Null(stored.UsedAt);

        var page = await ServiceFor(options, inst).ListAsync(null, null, null, 0, 50);
        Assert.Equal("pending", StatusOf(page, "111111111"));
    }

    [Fact]
    public async Task A_student_without_a_code_gets_one()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();
        var beto = await AddStudentAsync(options, inst, "222222222", "Beto", "Prueba Dos", null);

        var result = await ServiceFor(options, inst).RegenerateCodeAsync(beto);

        Assert.Null(result.Error);

        using var db = Reader(options);
        Assert.Single(await db.ActivationCodes.ToListAsync());
    }

    [Fact]
    public async Task A_student_with_an_account_cannot_get_a_new_code()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();
        var ana = await AddStudentAsync(options, inst, "111111111", "Ana", "Prueba Uno", "AAAA-2222");

        using (var db = Reader(options))
        {
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                InstitutionId = inst,
                PersonId = ana,
                Username = "111111111@demo-a",
                PasswordHash = "x"
            });
            await db.SaveChangesAsync();
        }

        var result = await ServiceFor(options, inst).RegenerateCodeAsync(ana);

        Assert.Equal(CodeRegenerationError.HasAccount, result.Error);
    }

    [Fact]
    public async Task Students_of_other_institutions_are_not_found()
    {
        var options = NewOptions();
        var other = await AddStudentAsync(options, Guid.NewGuid(), "111111111", "Ana", "Prueba Uno", "AAAA-2222");
        var service = ServiceFor(options, Guid.NewGuid());

        Assert.Equal(CodeRegenerationError.StudentNotFound, (await service.RegenerateCodeAsync(other)).Error);
        Assert.Equal(CodeRegenerationError.StudentNotFound, (await service.RegenerateCodeAsync(Guid.NewGuid())).Error);
        Assert.Empty((await service.ListAsync(null, null, null, 0, 50)).Items);
    }

    [Fact]
    public async Task Platform_users_cannot_manage_students()
    {
        var service = ServiceFor(NewOptions(), null);

        await Assert.ThrowsAsync<TenantViolationException>(() => service.ListAsync(null, null, null, 0, 50));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.RegenerateCodeAsync(Guid.NewGuid()));
    }
}
