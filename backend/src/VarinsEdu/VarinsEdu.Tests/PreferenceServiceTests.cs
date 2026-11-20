using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Preferences;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class PreferenceServiceTests
{
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

    private sealed record World(
        DbContextOptions<AppDbContext> Options,
        Guid InstitutionId,
        Guid AnaUserId,
        Guid AnaPersonId,
        Guid BetoUserId,
        Guid CarlaPersonId,
        Guid DinoPersonId,
        Guid StaffId);

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    // One institution with Ana and Beto (students with an account), Carla (a student without an account),
    // Dino (a student whose account is deactivated) and a staff member.
    private static async Task<World> SeedAsync(
        DbContextOptions<AppDbContext>? options = null,
        string slug = "demo-a",
        Guid? institutionId = null)
    {
        options ??= NewOptions();

        var inst = institutionId ?? Guid.NewGuid();
        var anaPerson = Guid.NewGuid();
        var betoPerson = Guid.NewGuid();
        var carlaPerson = Guid.NewGuid();
        var dinoPerson = Guid.NewGuid();
        var anaUser = Guid.NewGuid();
        var betoUser = Guid.NewGuid();
        var staff = Guid.NewGuid();

        Person NewPerson(Guid id, string cedula, string first) => new()
        {
            Id = id,
            InstitutionId = inst,
            IdentificationNumber = cedula,
            FirstName = first,
            LastName = "Prueba"
        };

        User NewUser(Guid id, string name, Guid? person, bool active = true) => new()
        {
            Id = id,
            InstitutionId = inst,
            PersonId = person,
            Username = $"{name}@{slug}",
            PasswordHash = "x",
            IsActive = active
        };

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.Add(new Institution { Id = inst, Name = "Colegio " + slug, Slug = slug });
        db.People.AddRange(
            NewPerson(anaPerson, "111111111", "Ana"),
            NewPerson(betoPerson, "222222222", "Beto"),
            NewPerson(carlaPerson, "333333333", "Carla"),
            NewPerson(dinoPerson, "444444444", "Dino"));
        db.Users.AddRange(
            NewUser(anaUser, "ana", anaPerson),
            NewUser(betoUser, "beto", betoPerson),
            NewUser(Guid.NewGuid(), "dino", dinoPerson, active: false),
            NewUser(staff, "staff", null));

        await db.SaveChangesAsync();

        return new World(options, inst, anaUser, anaPerson, betoUser, carlaPerson, dinoPerson, staff);
    }

    // A new service every time: each one has its own database context, like each request does.
    private static PreferenceService As(World w, Guid userId) =>
        new(new AppDbContext(w.Options, new FakeTenant(w.InstitutionId)), new FakeTenant(w.InstitutionId), new FakeUser(userId));

    private static AppDbContext Reader(DbContextOptions<AppDbContext> options) =>
        new(options, new PlatformTenant());

    [Fact]
    public async Task Without_saved_preferences_the_defaults_are_returned()
    {
        var w = await SeedAsync();

        var preferences = await As(w, w.AnaUserId).GetMineAsync();

        Assert.Equal(new DisplayPreferences("standard", 1.0, false, false, false), preferences);
    }

    [Fact]
    public async Task Saving_creates_the_row_then_updates_it_and_rounds_the_font_scale()
    {
        var w = await SeedAsync();

        var first = await As(w, w.AnaUserId).SetMineAsync(new("simple", 1.25, true, true, false));

        Assert.Null(first.Error);
        Assert.Equal(new DisplayPreferences("simple", 1.25, true, true, false), first.Value);
        Assert.Equal(first.Value, await As(w, w.AnaUserId).GetMineAsync());

        var second = await As(w, w.AnaUserId).SetMineAsync(new("standard", 1.2549, false, false, true));

        Assert.Equal(new DisplayPreferences("standard", 1.25, false, false, true), second.Value);

        // The limits themselves are allowed.
        Assert.Null((await As(w, w.AnaUserId).SetMineAsync(new("standard", 1.0, false, false, false))).Error);
        Assert.Null((await As(w, w.AnaUserId).SetMineAsync(new("standard", 2.0, false, false, false))).Error);

        using var db = Reader(w.Options);
        var row = await db.UserPreferences.SingleAsync();

        Assert.Equal(w.AnaUserId, row.UserId);
        Assert.Equal(2.0, row.FontScale);
    }

    [Fact]
    public async Task Invalid_preferences_are_rejected_and_nothing_is_saved()
    {
        var w = await SeedAsync();
        var service = As(w, w.AnaUserId);

        Assert.Equal("invalid_preferences", (await service.SetMineAsync(new("otro", 1.0, false, false, false))).Error);
        Assert.Equal("invalid_preferences", (await service.SetMineAsync(new("", 1.0, false, false, false))).Error);
        Assert.Equal("invalid_preferences", (await service.SetMineAsync(new("simple", 0.99, false, false, false))).Error);
        Assert.Equal("invalid_preferences", (await service.SetMineAsync(new("simple", 2.01, false, false, false))).Error);
        Assert.Equal("invalid_preferences", (await service.SetMineAsync(new("simple", double.NaN, false, false, false))).Error);
        Assert.Equal("invalid_preferences", (await service.SetMineAsync(new("simple", double.PositiveInfinity, false, false, false))).Error);

        using var db = Reader(w.Options);
        Assert.Empty(await db.UserPreferences.ToListAsync());
    }

    [Fact]
    public async Task Each_person_only_has_their_own_preferences()
    {
        var w = await SeedAsync();

        await As(w, w.AnaUserId).SetMineAsync(new("simple", 1.5, true, true, true));

        Assert.Equal(DisplayDefaults.Default, await As(w, w.BetoUserId).GetMineAsync());

        await As(w, w.BetoUserId).SetMineAsync(new("standard", 1.1, false, false, false));

        Assert.Equal("simple", (await As(w, w.AnaUserId).GetMineAsync()).UiMode);
    }

    [Fact]
    public async Task Staff_set_the_preferences_of_a_student_with_an_account_in_their_own_institution()
    {
        var options = NewOptions();
        var w = await SeedAsync(options);
        var other = await SeedAsync(options, "demo-b");
        var staff = As(w, w.StaffId);

        var set = await staff.SetForStudentAsync(w.AnaPersonId, new("simple", 1.5, false, true, true));

        Assert.Null(set.Error);
        Assert.Equal(new DisplayPreferences("simple", 1.5, false, true, true), await As(w, w.AnaUserId).GetMineAsync());
        Assert.Equal(1.5, (await As(w, w.StaffId).GetForStudentAsync(w.AnaPersonId)).Value!.FontScale);

        // No account, a deactivated account, nobody, or a student of another institution: all look the same.
        Assert.Equal("student_account_not_found", (await staff.SetForStudentAsync(w.CarlaPersonId, DisplayDefaults.Default)).Error);
        Assert.Equal("student_account_not_found", (await staff.SetForStudentAsync(w.DinoPersonId, DisplayDefaults.Default)).Error);
        Assert.Equal("student_account_not_found", (await staff.SetForStudentAsync(Guid.NewGuid(), DisplayDefaults.Default)).Error);
        Assert.Equal("student_account_not_found", (await staff.SetForStudentAsync(other.AnaPersonId, DisplayDefaults.Default)).Error);
        Assert.Equal("student_account_not_found", (await staff.GetForStudentAsync(other.AnaPersonId)).Error);

        Assert.Equal("invalid_preferences", (await staff.SetForStudentAsync(w.AnaPersonId, new("x", 1.0, false, false, false))).Error);

        // The other institution's student was not touched.
        using var db = Reader(options);
        Assert.Single(await db.UserPreferences.ToListAsync());
    }

    [Fact]
    public async Task Platform_users_have_their_own_preferences_but_cannot_configure_students()
    {
        var w = await SeedAsync();
        var platformUser = Guid.NewGuid();

        using (var db = Reader(w.Options))
        {
            db.Users.Add(new User { Id = platformUser, InstitutionId = null, Username = "root", PasswordHash = "x" });
            await db.SaveChangesAsync();
        }

        var tenant = new PlatformTenant();
        var service = new PreferenceService(new AppDbContext(w.Options, tenant), tenant, new FakeUser(platformUser));

        Assert.Null((await service.SetMineAsync(new("simple", 1.5, false, false, false))).Error);
        Assert.Equal("simple", (await service.GetMineAsync()).UiMode);

        await Assert.ThrowsAsync<TenantViolationException>(() => service.GetForStudentAsync(w.AnaPersonId));
        await Assert.ThrowsAsync<TenantViolationException>(() => service.SetForStudentAsync(w.AnaPersonId, DisplayDefaults.Default));
    }

    [Fact]
    public async Task Preference_changes_are_audited_without_their_values()
    {
        var institutionId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(
                new FakeTenant(institutionId),
                new FakeUser(null),
                TimeProvider.System,
                new AuditSuppression()))
            .Options;

        var w = await SeedAsync(options, "demo-a", institutionId);

        Assert.Null((await As(w, w.StaffId).SetForStudentAsync(w.AnaPersonId, new("simple", 1.5, true, true, true))).Error);

        using var db = Reader(options);
        var logs = await db.AuditLogs.Where(a => a.EntityType == "UserPreference").ToListAsync();

        var log = Assert.Single(logs);

        Assert.Equal("UserPreference.created", log.Action);
        Assert.Equal(w.AnaUserId.ToString(), log.EntityId);
        Assert.DoesNotContain("simple", log.Metadata);
        Assert.DoesNotContain("1.5", log.Metadata);
        Assert.Contains("[redacted]", log.Metadata);
    }
}
