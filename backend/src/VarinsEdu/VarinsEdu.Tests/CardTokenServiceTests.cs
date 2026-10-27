using VarinsEdu.Infrastructure.Security;
using Xunit;

namespace VarinsEdu.Tests;

public class CardTokenServiceTests
{
    private static readonly string Secret = new('k', 48);
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void An_issued_token_verifies_and_carries_the_ids()
    {
        var service = new CardTokenService(Secret, new FakeClock(Start));
        var person = Guid.NewGuid();
        var institution = Guid.NewGuid();

        var result = service.Verify(service.Issue(person, institution).Token);

        Assert.Equal(CardTokenStatus.Valid, result.Status);
        Assert.Equal(person, result.PersonId);
        Assert.Equal(institution, result.InstitutionId);
    }

    [Fact]
    public void A_changed_body_is_invalid()
    {
        var service = new CardTokenService(Secret, new FakeClock(Start));
        var parts = service.Issue(Guid.NewGuid(), Guid.NewGuid()).Token.Split('.');

        var tampered = (parts[0][0] == 'A' ? 'B' : 'A') + parts[0][1..] + "." + parts[1];

        Assert.Equal(CardTokenStatus.Invalid, service.Verify(tampered).Status);
    }

    [Fact]
    public void A_changed_signature_is_invalid()
    {
        var service = new CardTokenService(Secret, new FakeClock(Start));
        var parts = service.Issue(Guid.NewGuid(), Guid.NewGuid()).Token.Split('.');

        var tampered = parts[0] + "." + (parts[1][0] == 'A' ? 'B' : 'A') + parts[1][1..];

        Assert.Equal(CardTokenStatus.Invalid, service.Verify(tampered).Status);
    }

    [Fact]
    public void The_token_expires_after_its_lifetime()
    {
        var clock = new FakeClock(Start);
        var service = new CardTokenService(Secret, clock);
        var token = service.Issue(Guid.NewGuid(), Guid.NewGuid()).Token;

        clock.Now = Start + CardTokenService.Lifetime - TimeSpan.FromSeconds(1);
        Assert.Equal(CardTokenStatus.Valid, service.Verify(token).Status);

        clock.Now = Start + CardTokenService.Lifetime + TimeSpan.FromSeconds(1);
        Assert.Equal(CardTokenStatus.Expired, service.Verify(token).Status);
    }

    [Fact]
    public void A_token_signed_with_another_key_is_invalid()
    {
        var clock = new FakeClock(Start);
        var token = new CardTokenService(new string('a', 48), clock).Issue(Guid.NewGuid(), Guid.NewGuid()).Token;

        Assert.Equal(CardTokenStatus.Invalid, new CardTokenService(new string('b', 48), clock).Verify(token).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("a.b")]
    [InlineData("a.b.c")]
    public void Garbage_is_invalid(string? token)
    {
        var service = new CardTokenService(Secret, new FakeClock(Start));

        Assert.Equal(CardTokenStatus.Invalid, service.Verify(token).Status);
    }
}
