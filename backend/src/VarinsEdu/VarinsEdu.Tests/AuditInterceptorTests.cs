using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using Xunit;

namespace VarinsEdu.Tests;

public class AuditInterceptorTests
{
    private sealed class FakeTenant(Guid? institutionId, bool isPlatformScope = false) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope { get; } = isPlatformScope;
    }

    private sealed class FakeUser(Guid? userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public string? IpAddress => "203.0.113.7";
    }

    private static DbContextOptions<AppDbContext> Options(ICurrentTenant tenant, ICurrentUser user) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(tenant, user, TimeProvider.System))
            .Options;

    // Reads everything, so tests can inspect the audit log.
    private static AppDbContext Reader(DbContextOptions<AppDbContext> options) =>
        new(options, new FakeTenant(null, isPlatformScope: true));

    [Fact]
    public void Created_rows_are_audited_with_user_institution_and_values()
    {
        var institutionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var tenant = new FakeTenant(institutionId);
        var options = Options(tenant, new FakeUser(userId));

        using (var db = new AppDbContext(options, tenant))
        {
            db.Groups.Add(new Group { Id = groupId, Name = "10-1", Level = "10" });
            db.SaveChanges();
        }

        using var reader = Reader(options);
        var log = Assert.Single(reader.AuditLogs.ToList());

        Assert.Equal("Group.created", log.Action);
        Assert.Equal("Group", log.EntityType);
        Assert.Equal(groupId.ToString(), log.EntityId);
        Assert.Equal(institutionId, log.InstitutionId);
        Assert.Equal(userId, log.UserId);
        Assert.Equal("203.0.113.7", log.IpAddress);

        var after = JsonDocument.Parse(log.Metadata!).RootElement.GetProperty("after");
        Assert.Equal("10-1", after.GetProperty("Name").GetString());
    }

    [Fact]
    public void Updated_rows_record_only_the_changed_properties()
    {
        var tenant = new FakeTenant(Guid.NewGuid());
        var options = Options(tenant, new FakeUser(Guid.NewGuid()));

        using (var db = new AppDbContext(options, tenant))
        {
            db.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "10-1", Level = "10" });
            db.SaveChanges();
        }

        using (var db = new AppDbContext(options, tenant))
        {
            var group = db.Groups.Single();
            group.Name = "10-2";
            db.SaveChanges();
        }

        using var reader = Reader(options);
        var log = reader.AuditLogs.Single(l => l.Action == "Group.updated");
        var changes = JsonDocument.Parse(log.Metadata!).RootElement.GetProperty("changes");

        Assert.Equal("10-1", changes.GetProperty("Name").GetProperty("from").GetString());
        Assert.Equal("10-2", changes.GetProperty("Name").GetProperty("to").GetString());
        Assert.False(changes.TryGetProperty("Level", out _));
    }

    [Fact]
    public void Deleted_rows_are_audited_with_their_previous_values()
    {
        var tenant = new FakeTenant(Guid.NewGuid());
        var options = Options(tenant, new FakeUser(Guid.NewGuid()));

        using (var db = new AppDbContext(options, tenant))
        {
            db.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "10-1" });
            db.SaveChanges();
        }

        using (var db = new AppDbContext(options, tenant))
        {
            db.Groups.Remove(db.Groups.Single());
            db.SaveChanges();
        }

        using var reader = Reader(options);
        var log = reader.AuditLogs.Single(l => l.Action == "Group.deleted");
        var before = JsonDocument.Parse(log.Metadata!).RootElement.GetProperty("before");

        Assert.Equal("10-1", before.GetProperty("Name").GetString());
    }

    [Fact]
    public void Passwords_and_personal_data_are_redacted()
    {
        var tenant = new FakeTenant(Guid.NewGuid());
        var options = Options(tenant, new FakeUser(Guid.NewGuid()));

        using (var db = new AppDbContext(options, tenant))
        {
            db.Users.Add(new User { Id = Guid.NewGuid(), Username = "ana", PasswordHash = "secret-hash-value" });
            db.People.Add(new Person
            {
                Id = Guid.NewGuid(),
                IdentificationNumber = "1-1111-1111",
                FirstName = "Ana",
                LastName = "Prueba"
            });
            db.SaveChanges();
        }

        using var reader = Reader(options);
        var everything = string.Join(" ", reader.AuditLogs.Select(l => l.Metadata).ToList());

        Assert.DoesNotContain("secret-hash-value", everything);
        Assert.DoesNotContain("1-1111-1111", everything);
        Assert.DoesNotContain("Prueba", everything);
        Assert.Contains("[redacted]", everything);
    }

    [Fact]
    public void Saving_without_changes_writes_no_audit_rows()
    {
        var tenant = new FakeTenant(Guid.NewGuid());
        var options = Options(tenant, new FakeUser(Guid.NewGuid()));

        using (var db = new AppDbContext(options, tenant))
        {
            db.SaveChanges();
        }

        using var reader = Reader(options);
        Assert.Empty(reader.AuditLogs.ToList());
    }

    [Fact]
    public void Audit_rows_are_not_audited_themselves()
    {
        var tenant = new FakeTenant(Guid.NewGuid());
        var options = Options(tenant, new FakeUser(Guid.NewGuid()));

        using (var db = new AppDbContext(options, tenant))
        {
            db.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "10-1" });
            db.SaveChanges();
        }

        using var reader = Reader(options);
        var logs = reader.AuditLogs.ToList();

        Assert.Single(logs);
        Assert.DoesNotContain(logs, l => l.EntityType == nameof(AuditLog));
    }

    [Fact]
    public void Institution_rows_use_their_own_id_as_institution()
    {
        var platform = new FakeTenant(null, isPlatformScope: true);
        var options = Options(platform, new FakeUser(null));
        var institutionId = Guid.NewGuid();

        using (var db = new AppDbContext(options, platform))
        {
            db.Institutions.Add(new Institution { Id = institutionId, Name = "Demo", Slug = "demo" });
            db.SaveChanges();
        }

        using var reader = Reader(options);
        var log = Assert.Single(reader.AuditLogs.ToList());

        Assert.Equal("Institution.created", log.Action);
        Assert.Equal(institutionId, log.InstitutionId);
        Assert.Null(log.UserId);
    }
}
