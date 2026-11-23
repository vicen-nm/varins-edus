using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Auditing;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class AuditQueryServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly AuditFilter All = new(null, null, null, null);

    private sealed class FakeTenant(Guid? institutionId) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope => false;
    }

    private sealed record World(DbContextOptions<AppDbContext> Options, Guid InstA, Guid InstB, Guid StaffA);

    // Institution A (Costa Rica time, UTC-6) with a staff member, and institution B.
    private static async Task<World> SeedAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var staff = Guid.NewGuid();

        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.AddRange(
            new Institution { Id = a, Name = "A", Slug = "demo-a", TimeZone = "America/Costa_Rica" },
            new Institution { Id = b, Name = "B", Slug = "demo-b", TimeZone = "America/Costa_Rica" });
        db.Users.Add(new User { Id = staff, InstitutionId = a, Username = "staff@demo-a", PasswordHash = "x" });

        await db.SaveChangesAsync();

        return new World(options, a, b, staff);
    }

    private static AuditLog Log(Guid? institution, Guid? user, string action, string entity, DateTimeOffset at, string? metadata = null) =>
        new()
        {
            InstitutionId = institution,
            UserId = user,
            Action = action,
            EntityType = entity,
            EntityId = "x",
            OccurredAt = at,
            IpAddress = "203.0.113.7",
            Metadata = metadata
        };

    private static async Task AddAsync(World w, params AuditLog[] logs)
    {
        using var db = new AppDbContext(w.Options, new PlatformTenant());
        db.AuditLogs.AddRange(logs);
        await db.SaveChangesAsync();
    }

    private static AuditQueryService ServiceFor(World w, Guid? institutionId)
    {
        var tenant = new FakeTenant(institutionId);

        return new AuditQueryService(new AppDbContext(w.Options, tenant), tenant);
    }

    [Fact]
    public async Task The_newest_come_first_with_the_username_the_metadata_and_pages()
    {
        var w = await SeedAsync();

        await AddAsync(
            w,
            Log(w.InstA, w.StaffA, "Vehicle.updated", "Vehicle", T0, """{"reason":"x"}"""),
            Log(w.InstA, null, "auth.login.failed", "User", T0.AddMinutes(1)),
            Log(w.InstA, w.StaffA, "Visit.created", "Visit", T0.AddMinutes(2)));

        var service = ServiceFor(w, w.InstA);
        var first = await service.SearchAsync(All, 0, 2);

        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal("Visit.created", first.Items[0].Action);
        Assert.Equal("staff@demo-a", first.Items[0].Username);
        Assert.Equal("auth.login.failed", first.Items[1].Action);
        Assert.Null(first.Items[1].Username);
        Assert.Null(first.Items[1].Metadata);

        var last = Assert.Single((await service.SearchAsync(All, 2, 2)).Items);

        Assert.Equal("Vehicle.updated", last.Action);
        Assert.Equal("x", last.Metadata!.Value.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task The_text_search_and_the_entity_type_filter_narrow_the_list()
    {
        var w = await SeedAsync();

        await AddAsync(
            w,
            Log(w.InstA, null, "auth.login.failed", "User", T0),
            Log(w.InstA, null, "Vehicle.updated", "Vehicle", T0.AddMinutes(1)),
            Log(w.InstA, null, "ExitPermit.created", "ExitPermit", T0.AddMinutes(2)));

        var service = ServiceFor(w, w.InstA);

        Assert.Equal("auth.login.failed", Assert.Single((await service.SearchAsync(new(null, "LOGIN", null, null), 0, 50)).Items).Action);
        Assert.Equal("Vehicle.updated", Assert.Single((await service.SearchAsync(new(null, "vehicle", null, null), 0, 50)).Items).Action);
        Assert.Equal("ExitPermit.created", Assert.Single((await service.SearchAsync(new("ExitPermit", null, null, null), 0, 50)).Items).Action);
        Assert.Equal(0, (await service.SearchAsync(new(null, "zzz", null, null), 0, 50)).Total);
        Assert.Equal(0, (await service.SearchAsync(new("Vehicle", "created", null, null), 0, 50)).Total);
    }

    [Fact]
    public async Task The_date_range_follows_the_time_zone_of_the_institution_and_includes_both_days()
    {
        var w = await SeedAsync();

        // In Costa Rica: 23:59 of the 17th, 00:00 and 23:59 of the 18th, and 00:00 of the 19th.
        await AddAsync(
            w,
            Log(w.InstA, null, "a.one", "User", new DateTimeOffset(2026, 11, 18, 5, 59, 0, TimeSpan.Zero)),
            Log(w.InstA, null, "a.two", "User", new DateTimeOffset(2026, 11, 18, 6, 0, 0, TimeSpan.Zero)),
            Log(w.InstA, null, "a.three", "User", new DateTimeOffset(2026, 11, 19, 5, 59, 0, TimeSpan.Zero)),
            Log(w.InstA, null, "a.four", "User", new DateTimeOffset(2026, 11, 19, 6, 0, 0, TimeSpan.Zero)));

        var service = ServiceFor(w, w.InstA);

        var middle = await service.SearchAsync(new(null, null, new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 18)), 0, 50);

        Assert.Equal(new[] { "a.three", "a.two" }, middle.Items.Select(i => i.Action).ToArray());
        Assert.Equal("a.four", Assert.Single((await service.SearchAsync(new(null, null, new DateOnly(2026, 11, 19), null), 0, 50)).Items).Action);
        Assert.Equal("a.one", Assert.Single((await service.SearchAsync(new(null, null, null, new DateOnly(2026, 11, 17)), 0, 50)).Items).Action);
    }

    [Fact]
    public async Task Each_institution_sees_only_its_own_history_and_the_platform_sees_all()
    {
        var w = await SeedAsync();

        await AddAsync(
            w,
            Log(w.InstA, null, "a.event", "User", T0),
            Log(w.InstB, null, "b.event", "User", T0.AddMinutes(1)),
            Log(null, null, "nobody.event", "User", T0.AddMinutes(2)));

        Assert.Equal("a.event", Assert.Single((await ServiceFor(w, w.InstA).SearchAsync(All, 0, 50)).Items).Action);
        Assert.Equal("b.event", Assert.Single((await ServiceFor(w, w.InstB).SearchAsync(All, 0, 50)).Items).Action);

        var platform = new PlatformTenant();
        var all = await new AuditQueryService(new AppDbContext(w.Options, platform), platform).SearchAsync(All, 0, 50);

        Assert.Equal(3, all.Total);
    }

    [Fact]
    public async Task The_page_size_is_limited_and_an_entry_without_metadata_is_fine()
    {
        var w = await SeedAsync();

        await AddAsync(
            w,
            Log(w.InstA, null, "a.one", "User", T0),
            Log(w.InstA, null, "a.two", "User", T0.AddMinutes(1)),
            Log(w.InstA, null, "a.three", "User", T0.AddMinutes(2)));

        var service = ServiceFor(w, w.InstA);

        // Zero becomes one, and a huge number is capped (only 3 exist).
        Assert.Single((await service.SearchAsync(All, 0, 0)).Items);
        Assert.Equal(3, (await service.SearchAsync(All, 0, 1000)).Items.Count);
        Assert.Empty((await service.SearchAsync(All, 10, 50)).Items);
        Assert.All((await service.SearchAsync(All, 0, 50)).Items, i => Assert.Null(i.Metadata));
    }
}
