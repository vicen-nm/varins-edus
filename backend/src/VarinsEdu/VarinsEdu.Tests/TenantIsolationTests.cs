using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Persistence;
using Xunit;

namespace VarinsEdu.Tests;

public class TenantIsolationTests
{
    // A fake tenant lets each test decide "who is calling" without HTTP or login.
    private sealed class FakeTenant(Guid? institutionId, bool isPlatformScope = false) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope { get; } = isPlatformScope;
    }

    private static AppDbContext CreateContext(string databaseName, ICurrentTenant tenant)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AppDbContext(options, tenant);
    }

    // Institution A: 1 group and 1 user. Institution B: 2 groups and 1 user. Plus 1 platform user.
    private static (Guid A, Guid B) Seed(string databaseName)
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        // Seed with platform scope so the filter does not hide anything while inserting.
        using var db = CreateContext(databaseName, new FakeTenant(null, isPlatformScope: true));

        db.Institutions.AddRange(
            new Institution { Id = a, Name = "A", Slug = "a" },
            new Institution { Id = b, Name = "B", Slug = "b" });

        db.Groups.AddRange(
            new Group { Id = Guid.NewGuid(), InstitutionId = a, Name = "10-1" },
            new Group { Id = Guid.NewGuid(), InstitutionId = b, Name = "10-1" },
            new Group { Id = Guid.NewGuid(), InstitutionId = b, Name = "10-2" });

        db.Users.AddRange(
            new User { Id = Guid.NewGuid(), InstitutionId = a, Username = "user-a" },
            new User { Id = Guid.NewGuid(), InstitutionId = b, Username = "user-b" },
            new User { Id = Guid.NewGuid(), InstitutionId = null, Username = "platform-admin" });

        db.SaveChanges();
        return (a, b);
    }

    [Fact]
    public void Tenant_only_sees_its_own_groups()
    {
        var dbName = Guid.NewGuid().ToString();
        var (a, b) = Seed(dbName);

        using var dbA = CreateContext(dbName, new FakeTenant(a));
        using var dbB = CreateContext(dbName, new FakeTenant(b));

        var groupsA = dbA.Groups.ToList();

        Assert.Single(groupsA);
        Assert.All(groupsA, g => Assert.Equal(a, g.InstitutionId));
        Assert.Equal(2, dbB.Groups.Count());
    }

    [Fact]
    public void Tenant_only_sees_its_own_institution()
    {
        var dbName = Guid.NewGuid().ToString();
        var (a, _) = Seed(dbName);

        using var db = CreateContext(dbName, new FakeTenant(a));

        var institution = Assert.Single(db.Institutions.ToList());
        Assert.Equal(a, institution.Id);
    }

    [Fact]
    public void Tenant_never_sees_platform_users()
    {
        var dbName = Guid.NewGuid().ToString();
        var (a, _) = Seed(dbName);

        using var db = CreateContext(dbName, new FakeTenant(a));

        var user = Assert.Single(db.Users.ToList());
        Assert.Equal("user-a", user.Username);
    }

    [Fact]
    public void Platform_scope_sees_everything()
    {
        var dbName = Guid.NewGuid().ToString();
        Seed(dbName);

        using var db = CreateContext(dbName, new FakeTenant(null, isPlatformScope: true));

        Assert.Equal(2, db.Institutions.Count());
        Assert.Equal(3, db.Groups.Count());
        Assert.Equal(3, db.Users.Count());
    }

    [Fact]
    public void Unknown_caller_sees_nothing()
    {
        var dbName = Guid.NewGuid().ToString();
        Seed(dbName);

        // No institution and no platform scope: the filter must fail closed.
        using var db = CreateContext(dbName, new FakeTenant(null));

        Assert.Empty(db.Institutions.ToList());
        Assert.Empty(db.Groups.ToList());
        Assert.Empty(db.Users.ToList());
    }

    [Fact]
    public void IgnoreQueryFilters_bypasses_the_filter()
    {
        var dbName = Guid.NewGuid().ToString();
        var (a, _) = Seed(dbName);

        using var db = CreateContext(dbName, new FakeTenant(a));

        Assert.Equal(1, db.Groups.Count());
        Assert.Equal(3, db.Groups.IgnoreQueryFilters().Count());
    }
}
