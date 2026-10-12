using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Infrastructure.Institutions;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Seeding;
using Xunit;

namespace VarinsEdu.Tests;

public class BrandingServiceTests
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

    private static async Task SeedAsync(
        DbContextOptions<AppDbContext> options,
        Guid institutionId,
        bool withSettings = true)
    {
        using var db = new AppDbContext(options, new PlatformTenant());

        db.Institutions.Add(new Institution
        {
            Id = institutionId,
            Name = "Test",
            Slug = institutionId.ToString("N")
        });

        if (withSettings)
        {
            db.InstitutionSettings.Add(new InstitutionSettings { InstitutionId = institutionId });
        }

        await db.SaveChangesAsync();
    }

    private static BrandingService ServiceFor(DbContextOptions<AppDbContext> options, Guid? institutionId)
    {
        var tenant = new FakeTenant(institutionId);
        return new BrandingService(new AppDbContext(options, tenant), tenant);
    }

    private static async Task<InstitutionSettings> ReadAsync(
        DbContextOptions<AppDbContext> options,
        Guid institutionId)
    {
        using var db = new AppDbContext(options, new PlatformTenant());
        return await db.InstitutionSettings.SingleAsync(s => s.InstitutionId == institutionId);
    }

    [Fact]
    public async Task Updates_name_and_colors_trimmed_and_in_lowercase()
    {
        var options = NewOptions();
        var a = Guid.NewGuid();
        await SeedAsync(options, a);

        var result = await ServiceFor(options, a).UpdateAsync(new("  Colegio Demo A  ", "#7A1FA2", "#00FF00"));

        Assert.True(result.Succeeded);

        var settings = await ReadAsync(options, a);
        Assert.Equal("Colegio Demo A", settings.DisplayName);
        Assert.Equal("#7a1fa2", settings.PrimaryColor);
        Assert.Equal("#00ff00", settings.AccentColor);
    }

    [Fact]
    public async Task Blank_values_go_back_to_the_default()
    {
        var options = NewOptions();
        var a = Guid.NewGuid();
        await SeedAsync(options, a);

        await ServiceFor(options, a).UpdateAsync(new("Colegio Demo A", "#7a1fa2", "#00ff00"));
        var result = await ServiceFor(options, a).UpdateAsync(new("   ", null, ""));

        Assert.True(result.Succeeded);

        var settings = await ReadAsync(options, a);
        Assert.Null(settings.DisplayName);
        Assert.Null(settings.PrimaryColor);
        Assert.Null(settings.AccentColor);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff")]
    [InlineData("#GGGGGG")]
    [InlineData("007c99")]
    public async Task Invalid_colors_are_rejected_and_nothing_changes(string color)
    {
        var options = NewOptions();
        var a = Guid.NewGuid();
        await SeedAsync(options, a);

        var result = await ServiceFor(options, a).UpdateAsync(new("Colegio Demo A", color, null));

        Assert.Equal(BrandingError.InvalidColor, result.Error);

        var settings = await ReadAsync(options, a);
        Assert.Null(settings.PrimaryColor);
        Assert.Null(settings.DisplayName);
    }

    [Fact]
    public async Task Too_short_display_name_is_rejected()
    {
        var options = NewOptions();
        var a = Guid.NewGuid();
        await SeedAsync(options, a);

        var result = await ServiceFor(options, a).UpdateAsync(new("ab", null, null));

        Assert.Equal(BrandingError.InvalidDisplayName, result.Error);
    }

    [Fact]
    public async Task Too_long_display_name_is_rejected()
    {
        var options = NewOptions();
        var a = Guid.NewGuid();
        await SeedAsync(options, a);

        var result = await ServiceFor(options, a).UpdateAsync(new(new string('a', 201), null, null));

        Assert.Equal(BrandingError.InvalidDisplayName, result.Error);
    }

    [Fact]
    public async Task Platform_users_have_no_institution_to_update()
    {
        var result = await ServiceFor(NewOptions(), null).UpdateAsync(new("Colegio Demo A", null, null));

        Assert.Equal(BrandingError.NotAnInstitutionUser, result.Error);
    }

    [Fact]
    public async Task An_institution_can_only_change_its_own_identity()
    {
        var options = NewOptions();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await SeedAsync(options, a);
        await SeedAsync(options, b);

        await ServiceFor(options, a).UpdateAsync(new("Solo para A", "#7a1fa2", null));

        var settingsA = await ReadAsync(options, a);
        var settingsB = await ReadAsync(options, b);

        Assert.Equal("Solo para A", settingsA.DisplayName);
        Assert.Null(settingsB.DisplayName);
        Assert.Null(settingsB.PrimaryColor);
    }

    [Fact]
    public async Task Missing_settings_row_is_created()
    {
        var options = NewOptions();
        var a = Guid.NewGuid();
        await SeedAsync(options, a, withSettings: false);

        var result = await ServiceFor(options, a).UpdateAsync(new("Colegio Demo A", "#7a1fa2", null));

        Assert.True(result.Succeeded);
        Assert.Equal("#7a1fa2", (await ReadAsync(options, a)).PrimaryColor);
    }
}
