using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Persistence;
using Xunit;

namespace VarinsEdu.Tests;

public class TenantWriteTests
{
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

    [Fact]
    public void New_row_without_institution_is_stamped_with_the_current_one()
    {
        var dbName = Guid.NewGuid().ToString();
        var a = Guid.NewGuid();

        using (var db = CreateContext(dbName, new FakeTenant(a)))
        {
            db.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "10-1" });
            db.SaveChanges();
        }

        using var check = CreateContext(dbName, new FakeTenant(null, isPlatformScope: true));
        var group = Assert.Single(check.Groups.ToList());
        Assert.Equal(a, group.InstitutionId);
    }

    [Fact]
    public void Writing_a_row_of_another_institution_is_blocked()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using var db = CreateContext(Guid.NewGuid().ToString(), new FakeTenant(a));
        db.Groups.Add(new Group { Id = Guid.NewGuid(), InstitutionId = b, Name = "10-1" });

        Assert.Throws<TenantViolationException>(() => db.SaveChanges());
    }

    [Fact]
    public void The_institution_of_an_existing_row_cannot_be_changed()
    {
        var dbName = Guid.NewGuid().ToString();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using (var seed = CreateContext(dbName, new FakeTenant(null, isPlatformScope: true)))
        {
            seed.Groups.Add(new Group { Id = Guid.NewGuid(), InstitutionId = a, Name = "10-1" });
            seed.SaveChanges();
        }

        using var db = CreateContext(dbName, new FakeTenant(a));
        var group = db.Groups.Single();
        group.InstitutionId = b;

        Assert.Throws<TenantViolationException>(() => db.SaveChanges());
    }

    [Fact]
    public void Caller_without_institution_cannot_write()
    {
        using var db = CreateContext(Guid.NewGuid().ToString(), new FakeTenant(null));
        db.Groups.Add(new Group { Id = Guid.NewGuid(), InstitutionId = Guid.NewGuid(), Name = "10-1" });

        Assert.Throws<TenantViolationException>(() => db.SaveChanges());
    }

    [Fact]
    public void Platform_scope_can_write_for_any_institution()
    {
        var b = Guid.NewGuid();

        using var db = CreateContext(Guid.NewGuid().ToString(), new FakeTenant(null, isPlatformScope: true));
        db.Groups.Add(new Group { Id = Guid.NewGuid(), InstitutionId = b, Name = "10-1" });
        db.SaveChanges();

        Assert.Equal(1, db.Groups.Count());
    }

    [Fact]
    public void Platform_scope_must_state_the_institution()
    {
        using var db = CreateContext(Guid.NewGuid().ToString(), new FakeTenant(null, isPlatformScope: true));
        db.Groups.Add(new Group { Id = Guid.NewGuid(), Name = "10-1" });

        Assert.Throws<TenantViolationException>(() => db.SaveChanges());
    }

    [Fact]
    public void User_created_by_a_tenant_belongs_to_that_tenant()
    {
        var dbName = Guid.NewGuid().ToString();
        var a = Guid.NewGuid();

        using (var db = CreateContext(dbName, new FakeTenant(a)))
        {
            db.Users.Add(new User { Id = Guid.NewGuid(), Username = "ana" });
            db.SaveChanges();
        }

        using var check = CreateContext(dbName, new FakeTenant(null, isPlatformScope: true));
        var user = Assert.Single(check.Users.ToList());
        Assert.Equal(a, user.InstitutionId);
    }

    [Fact]
    public void Tenant_cannot_create_users_for_another_institution()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        using var db = CreateContext(Guid.NewGuid().ToString(), new FakeTenant(a));
        db.Users.Add(new User { Id = Guid.NewGuid(), InstitutionId = b, Username = "intruder" });

        Assert.Throws<TenantViolationException>(() => db.SaveChanges());
    }

    [Fact]
    public void Tenant_cannot_create_institutions()
    {
        var a = Guid.NewGuid();

        using var db = CreateContext(Guid.NewGuid().ToString(), new FakeTenant(a));
        db.Institutions.Add(new Institution { Id = Guid.NewGuid(), Name = "X", Slug = "x" });

        Assert.Throws<TenantViolationException>(() => db.SaveChanges());
    }
}
