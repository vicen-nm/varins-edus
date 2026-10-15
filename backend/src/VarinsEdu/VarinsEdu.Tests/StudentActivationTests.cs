using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class StudentActivationTests
{
    private const string Code = "ABCD-2345";
    private const string Password = "mi-clave-segura-1";

    private static readonly ActivationCodeHasher CodeHasher = new(new string('k', 48));

    private sealed class FakeUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public string? IpAddress => "203.0.113.7";
    }

    private sealed record Scenario(DbContextOptions<AppDbContext> Options, Guid InstitutionId, Guid PersonId);

    // One institution with its student role, one student (cédula 111111111) and one activation code.
    private static async Task<Scenario> SeedAsync(
        string slug = "demo-a",
        bool institutionActive = true,
        DateTimeOffset? expiresAt = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var institutionId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.Add(new Institution
        {
            Id = institutionId,
            Name = "Colegio Demo A",
            Slug = slug,
            IsActive = institutionActive
        });

        var role = new Role
        {
            Id = roleId,
            InstitutionId = institutionId,
            Key = RoleKeys.Student,
            Name = "Student",
            IsSystem = true
        };
        role.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionKey = PermissionKeys.StudentsView });
        db.Roles.Add(role);

        db.People.Add(new Person
        {
            Id = personId,
            InstitutionId = institutionId,
            IdentificationType = IdentificationTypes.Cedula,
            IdentificationNumber = "111111111",
            FirstName = "Ana",
            LastName = "Prueba",
            IsActive = true
        });

        db.ActivationCodes.Add(new ActivationCode
        {
            Id = Guid.NewGuid(),
            InstitutionId = institutionId,
            PersonId = personId,
            CodeHash = CodeHasher.Hash(Code),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddDays(30),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();

        return new Scenario(options, institutionId, personId);
    }

    private static StudentActivationService ServiceFor(DbContextOptions<AppDbContext> options) =>
        new(options, new PasswordHasherService(), CodeHasher, new FakeUser(null), TimeProvider.System);

    private static ActivationCommand Command(
        string slug = "demo-a",
        string identification = "1-1111-1111",
        string code = Code,
        string password = Password) =>
        new(slug, identification, code, password);

    private static AppDbContext Reader(DbContextOptions<AppDbContext> options) =>
        new(options, new PlatformTenant());

    [Fact]
    public async Task A_valid_activation_creates_the_account_and_returns_the_user()
    {
        var scenario = await SeedAsync();

        var result = await ServiceFor(scenario.Options).ActivateAsync(Command());

        Assert.True(result.Succeeded);

        var user = result.User!;
        Assert.Equal("111111111@demo-a", user.Username);
        Assert.Equal(scenario.InstitutionId, user.InstitutionId);
        Assert.False(user.IsPlatform);
        Assert.Equal(new[] { PermissionKeys.StudentsView }, user.Permissions);

        using var db = Reader(scenario.Options);
        var stored = await db.Users.SingleAsync();

        Assert.Equal(scenario.PersonId, stored.PersonId);
        Assert.Equal(scenario.InstitutionId, stored.InstitutionId);
        Assert.NotEqual(Password, stored.PasswordHash);
        Assert.True(new PasswordHasherService().Verify(stored.PasswordHash, Password));

        var link = await db.UserRoles.SingleAsync();
        var role = await db.Roles.SingleAsync(r => r.Id == link.RoleId);
        Assert.Equal(RoleKeys.Student, role.Key);
    }

    [Fact]
    public async Task The_code_cannot_be_used_twice()
    {
        var scenario = await SeedAsync();
        var service = ServiceFor(scenario.Options);

        Assert.True((await service.ActivateAsync(Command())).Succeeded);

        var second = await service.ActivateAsync(Command(password: "otra-clave-segura-2"));

        Assert.Equal(ActivationError.ActivationFailed, second.Error);

        using var db = Reader(scenario.Options);
        Assert.NotNull((await db.ActivationCodes.SingleAsync()).UsedAt);
        Assert.Single(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task The_code_is_accepted_with_other_formatting()
    {
        var scenario = await SeedAsync();

        var result = await ServiceFor(scenario.Options).ActivateAsync(Command(code: " abcd 2345 "));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_wrong_code_is_rejected_and_counts_as_an_attempt()
    {
        var scenario = await SeedAsync();

        var result = await ServiceFor(scenario.Options).ActivateAsync(Command(code: "WXYZ-9999"));

        Assert.Equal(ActivationError.ActivationFailed, result.Error);

        using var db = Reader(scenario.Options);
        Assert.Equal(1, (await db.ActivationCodes.SingleAsync()).FailedAttempts);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task The_code_is_locked_after_too_many_attempts()
    {
        var scenario = await SeedAsync();
        var service = ServiceFor(scenario.Options);

        for (var i = 0; i < StudentActivationService.MaxFailedAttempts; i++)
        {
            await service.ActivateAsync(Command(code: "WXYZ-9999"));
        }

        // Even the right code is refused once the code is locked.
        var result = await service.ActivateAsync(Command());

        Assert.Equal(ActivationError.ActivationFailed, result.Error);

        using var db = Reader(scenario.Options);
        Assert.Equal(StudentActivationService.MaxFailedAttempts, (await db.ActivationCodes.SingleAsync()).FailedAttempts);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task An_expired_code_is_rejected()
    {
        var scenario = await SeedAsync(expiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        var result = await ServiceFor(scenario.Options).ActivateAsync(Command());

        Assert.Equal(ActivationError.ActivationFailed, result.Error);
    }

    [Fact]
    public async Task Unknown_people_are_rejected_with_the_same_error()
    {
        var scenario = await SeedAsync();

        var result = await ServiceFor(scenario.Options).ActivateAsync(Command(identification: "9-9999-9999"));

        Assert.Equal(ActivationError.ActivationFailed, result.Error);

        // Nobody else's code was touched.
        using var db = Reader(scenario.Options);
        Assert.Equal(0, (await db.ActivationCodes.SingleAsync()).FailedAttempts);
    }

    [Fact]
    public async Task Unknown_or_inactive_institutions_are_rejected()
    {
        var unknown = await SeedAsync();
        var inactive = await SeedAsync(institutionActive: false);

        var forUnknown = await ServiceFor(unknown.Options).ActivateAsync(Command(slug: "otro-colegio"));
        var forInactive = await ServiceFor(inactive.Options).ActivateAsync(Command());

        Assert.Equal(ActivationError.ActivationFailed, forUnknown.Error);
        Assert.Equal(ActivationError.ActivationFailed, forInactive.Error);
    }

    [Fact]
    public async Task A_person_who_already_has_an_account_cannot_activate_again()
    {
        var scenario = await SeedAsync();

        using (var db = Reader(scenario.Options))
        {
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                InstitutionId = scenario.InstitutionId,
                PersonId = scenario.PersonId,
                Username = "alguien@demo-a",
                PasswordHash = "x"
            });
            await db.SaveChangesAsync();
        }

        var result = await ServiceFor(scenario.Options).ActivateAsync(Command());

        Assert.Equal(ActivationError.ActivationFailed, result.Error);

        using var check = Reader(scenario.Options);
        Assert.Null((await check.ActivationCodes.SingleAsync()).UsedAt);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("111111111abc")]
    [InlineData("12345678")]
    public async Task Weak_passwords_are_rejected_before_the_code_is_checked(string password)
    {
        var scenario = await SeedAsync();

        var result = await ServiceFor(scenario.Options).ActivateAsync(Command(password: password));

        Assert.Equal(ActivationError.WeakPassword, result.Error);

        // A weak password must not use up an attempt, and nothing is created.
        using var db = Reader(scenario.Options);
        Assert.Equal(0, (await db.ActivationCodes.SingleAsync()).FailedAttempts);
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task The_code_only_works_for_the_institution_it_belongs_to()
    {
        var scenario = await SeedAsync();

        using (var db = Reader(scenario.Options))
        {
            db.Institutions.Add(new Institution { Id = Guid.NewGuid(), Name = "Colegio B", Slug = "demo-b" });
            await db.SaveChangesAsync();
        }

        // Same cédula and the right code, but through another school's link.
        var result = await ServiceFor(scenario.Options).ActivateAsync(Command(slug: "demo-b"));

        Assert.Equal(ActivationError.ActivationFailed, result.Error);

        using var check = Reader(scenario.Options);
        Assert.Empty(await check.Users.ToListAsync());
        Assert.Equal(0, (await check.ActivationCodes.SingleAsync()).FailedAttempts);
    }

    [Fact]
    public async Task Failures_are_audited_without_the_identification_number()
    {
        var scenario = await SeedAsync();
        var service = ServiceFor(scenario.Options);

        await service.ActivateAsync(Command(code: "WXYZ-9999"));
        await service.ActivateAsync(Command(identification: "9-9999-9999"));

        using var db = Reader(scenario.Options);
        var failures = await db.AuditLogs.Where(a => a.Action == "auth.activation.failed").ToListAsync();

        Assert.Equal(2, failures.Count);
        Assert.Contains(failures, a => JsonDocument.Parse(a.Metadata!).RootElement.GetProperty("reason").GetString() == "wrong_code");
        Assert.Contains(failures, a => JsonDocument.Parse(a.Metadata!).RootElement.GetProperty("reason").GetString() == "unknown_person");

        // Cédulas are personal data of minors: never written to the audit log.
        Assert.All(failures, a => Assert.DoesNotContain("1111", a.Metadata));
        Assert.All(failures, a => Assert.DoesNotContain("9999999", a.Metadata));
    }

    [Fact]
    public async Task A_successful_activation_is_audited()
    {
        var scenario = await SeedAsync();

        var result = await ServiceFor(scenario.Options).ActivateAsync(Command());

        using var db = Reader(scenario.Options);
        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "auth.activation.succeeded");

        Assert.Equal(result.User!.UserId, audit.UserId);
        Assert.Equal(scenario.InstitutionId, audit.InstitutionId);
        Assert.Equal("203.0.113.7", audit.IpAddress);
    }
}
