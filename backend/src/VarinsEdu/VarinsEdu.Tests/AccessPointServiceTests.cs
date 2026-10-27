using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Access;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class AccessPointServiceTests
{
    private static readonly AccessPointTokenService Tokens = new(new string('k', 48), TimeProvider.System);

    private sealed class FakeTenant(Guid? institutionId) : ICurrentTenant
    {
        public Guid? InstitutionId { get; } = institutionId;
        public bool IsPlatformScope => false;
    }

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static AccessPointService ServiceFor(DbContextOptions<AppDbContext> options, Guid? institutionId)
    {
        var tenant = new FakeTenant(institutionId);
        return new AccessPointService(new AppDbContext(options, tenant), tenant, Tokens, TimeProvider.System);
    }

    private static AccessPointInput Valid(string name = "Comedor") =>
        new(name, AccessPointKinds.Cafeteria, 9.9333, -84.0833);

    private static AccessServiceInput Lunch(string name = "Almuerzo") =>
        new(name, new TimeOnly(11, 0), new TimeOnly(13, 0));

    [Fact]
    public async Task Creating_a_point_saves_it_with_the_defaults()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();

        var result = await ServiceFor(options, inst).CreateAsync(Valid("  Comedor  "));

        Assert.Null(result.Error);
        Assert.Equal("Comedor", result.Value!.Name);
        Assert.Equal(150, result.Value.RadiusMeters);
        Assert.Equal(100, result.Value.MaxAccuracyMeters);

        using var db = new AppDbContext(options, new PlatformTenant());
        Assert.Equal(inst, (await db.AccessPoints.SingleAsync()).InstitutionId);
    }

    [Fact]
    public async Task Invalid_points_are_rejected()
    {
        var service = ServiceFor(NewOptions(), Guid.NewGuid());

        Assert.Equal("invalid_access_name", (await service.CreateAsync(Valid(""))).Error);
        Assert.Equal("invalid_access_kind", (await service.CreateAsync(Valid() with { Kind = "otro" })).Error);
        Assert.Equal("invalid_location", (await service.CreateAsync(Valid() with { Latitude = 100 })).Error);
        Assert.Equal("invalid_location", (await service.CreateAsync(Valid() with { Longitude = null })).Error);
        Assert.Equal("invalid_radius", (await service.CreateAsync(Valid() with { RadiusMeters = 5 })).Error);
        Assert.Equal("invalid_accuracy", (await service.CreateAsync(Valid() with { MaxAccuracyMeters = 5000 })).Error);
    }

    [Fact]
    public async Task Point_names_are_unique_per_institution()
    {
        var options = NewOptions();

        var a = ServiceFor(options, Guid.NewGuid());
        var b = ServiceFor(options, Guid.NewGuid());

        Assert.Null((await a.CreateAsync(Valid())).Error);
        Assert.Equal("access_point_name_taken", (await a.CreateAsync(Valid())).Error);
        Assert.Null((await b.CreateAsync(Valid())).Error);
    }

    [Fact]
    public async Task A_point_of_another_institution_cannot_be_changed()
    {
        var options = NewOptions();
        var created = await ServiceFor(options, Guid.NewGuid()).CreateAsync(Valid());

        var result = await ServiceFor(options, Guid.NewGuid()).UpdateAsync(created.Value!.Id, Valid("Otro nombre"));

        Assert.Equal("access_point_not_found", result.Error);
    }

    [Fact]
    public async Task A_service_needs_a_valid_schedule_and_a_unique_name()
    {
        var service = ServiceFor(NewOptions(), Guid.NewGuid());
        var point = (await service.CreateAsync(Valid())).Value!;

        var backwards = new AccessServiceInput("Almuerzo", new TimeOnly(13, 0), new TimeOnly(11, 0));

        Assert.Equal("invalid_schedule", (await service.CreateServiceAsync(point.Id, backwards)).Error);
        Assert.Null((await service.CreateServiceAsync(point.Id, Lunch())).Error);
        Assert.Equal("access_service_name_taken", (await service.CreateServiceAsync(point.Id, Lunch())).Error);
    }

    [Fact]
    public async Task Services_cannot_be_added_to_points_of_other_institutions()
    {
        var options = NewOptions();
        var point = (await ServiceFor(options, Guid.NewGuid()).CreateAsync(Valid())).Value!;
        var other = ServiceFor(options, Guid.NewGuid());

        Assert.Equal("access_point_not_found", (await other.CreateServiceAsync(point.Id, Lunch())).Error);
        Assert.Equal("access_point_not_found", (await other.ListServicesAsync(point.Id)).Error);
    }

    [Fact]
    public async Task A_qr_token_is_issued_for_an_active_point()
    {
        var inst = Guid.NewGuid();
        var service = ServiceFor(NewOptions(), inst);
        var point = (await service.CreateAsync(Valid())).Value!;

        var qr = await service.IssueQrAsync(point.Id);

        Assert.Null(qr.Error);

        var check = Tokens.Verify(qr.Value!.Token);
        Assert.Equal(AccessTokenStatus.Valid, check.Status);
        Assert.Equal(point.Id, check.PointId);
        Assert.Equal(inst, check.InstitutionId);
    }

    [Fact]
    public async Task An_inactive_point_has_no_qr()
    {
        var service = ServiceFor(NewOptions(), Guid.NewGuid());
        var point = (await service.CreateAsync(Valid())).Value!;

        await service.UpdateAsync(point.Id, Valid() with { IsActive = false });

        Assert.Equal("access_point_not_found", (await service.IssueQrAsync(point.Id)).Error);
    }

    [Fact]
    public async Task Platform_users_cannot_manage_access_points()
    {
        var service = ServiceFor(NewOptions(), null);

        await Assert.ThrowsAsync<TenantViolationException>(() => service.ListAsync());
        await Assert.ThrowsAsync<TenantViolationException>(() => service.CreateAsync(Valid()));
    }
}
