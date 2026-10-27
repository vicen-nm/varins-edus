using Microsoft.EntityFrameworkCore;
using VarinsEdu.Domain.Common;
using VarinsEdu.Domain.Constants;
using VarinsEdu.Domain.Entities;
using VarinsEdu.Domain.Exceptions;
using VarinsEdu.Infrastructure.Persistence;
using VarinsEdu.Infrastructure.Security;
using VarinsEdu.Infrastructure.Seeding;
using VarinsEdu.Infrastructure.Students;
using Xunit;

namespace VarinsEdu.Tests;

public class StudentCardServiceTests
{
    private static readonly CardTokenService Tokens = new(new string('k', 48), TimeProvider.System);

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

    private static DbContextOptions<AppDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static StudentCardService ServiceFor(DbContextOptions<AppDbContext> options, Guid? institutionId, Guid? userId)
    {
        var tenant = new FakeTenant(institutionId);
        return new StudentCardService(new AppDbContext(options, tenant), tenant, new FakeUser(userId), Tokens);
    }

    // One student with an account. Returns the user id and the person id.
    private static async Task<(Guid UserId, Guid PersonId)> SeedAsync(
        DbContextOptions<AppDbContext> options,
        Guid institutionId,
        string status = StudentStatuses.Active,
        bool withProfile = true)
    {
        using var db = new AppDbContext(options, new PlatformTenant());
        var personId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        db.Groups.Add(new Group { Id = groupId, InstitutionId = institutionId, Name = "10-1" });
        db.People.Add(new Person
        {
            Id = personId,
            InstitutionId = institutionId,
            IdentificationNumber = "111111111",
            FirstName = "Ana",
            LastName = "Prueba Uno",
            CreatedAt = DateTimeOffset.UtcNow
        });

        if (withProfile)
        {
            db.StudentProfiles.Add(new StudentProfile
            {
                PersonId = personId,
                InstitutionId = institutionId,
                GroupId = groupId,
                Status = status
            });
        }

        db.Users.Add(new User
        {
            Id = userId,
            InstitutionId = institutionId,
            PersonId = personId,
            Username = "111111111@demo-a",
            PasswordHash = "x"
        });

        await db.SaveChangesAsync();
        return (userId, personId);
    }

    [Fact]
    public async Task A_student_gets_her_own_card_with_a_valid_token()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();
        var (userId, personId) = await SeedAsync(options, inst);

        var result = await ServiceFor(options, inst, userId).GetAsync();

        Assert.Null(result.Error);

        var card = result.Card!;
        Assert.Equal("Ana", card.FirstName);
        Assert.Equal("Prueba Uno", card.LastName);
        Assert.Equal("10-1", card.Group);

        var check = Tokens.Verify(card.Token.Token);
        Assert.Equal(CardTokenStatus.Valid, check.Status);
        Assert.Equal(personId, check.PersonId);
        Assert.Equal(inst, check.InstitutionId);
    }

    [Fact]
    public async Task A_user_without_a_student_profile_has_no_card()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();
        var (userId, _) = await SeedAsync(options, inst, withProfile: false);

        var result = await ServiceFor(options, inst, userId).GetAsync();

        Assert.Equal(CardError.Unavailable, result.Error);
    }

    [Fact]
    public async Task A_student_who_is_not_active_has_no_card()
    {
        var options = NewOptions();
        var inst = Guid.NewGuid();
        var (userId, _) = await SeedAsync(options, inst, status: "inactive");

        var result = await ServiceFor(options, inst, userId).GetAsync();

        Assert.Equal(CardError.Unavailable, result.Error);
    }

    [Fact]
    public async Task A_user_of_another_institution_has_no_card()
    {
        var options = NewOptions();
        var (userId, _) = await SeedAsync(options, Guid.NewGuid());

        var result = await ServiceFor(options, Guid.NewGuid(), userId).GetAsync();

        Assert.Equal(CardError.Unavailable, result.Error);
    }

    [Fact]
    public async Task Platform_users_cannot_have_a_card()
    {
        var service = ServiceFor(NewOptions(), null, Guid.NewGuid());

        await Assert.ThrowsAsync<TenantViolationException>(() => service.GetAsync());
    }
}
