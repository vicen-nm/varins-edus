using VarinsEdu.Infrastructure.Security;
using Xunit;

namespace VarinsEdu.Tests;

public class PasswordHasherTests
{
    private readonly PasswordHasherService _hasher = new();

    [Fact]
    public void Hash_is_not_the_password()
    {
        var hash = _hasher.Hash("my-secret-password");

        Assert.NotEqual("my-secret-password", hash);
        Assert.DoesNotContain("my-secret-password", hash);
    }

    [Fact]
    public void Same_password_gives_different_hashes()
    {
        var first = _hasher.Hash("my-secret-password");
        var second = _hasher.Hash("my-secret-password");

        // A random salt makes every hash unique, even for the same password.
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Verify_accepts_the_right_password_and_rejects_a_wrong_one()
    {
        var hash = _hasher.Hash("my-secret-password");

        Assert.True(_hasher.Verify(hash, "my-secret-password"));
        Assert.False(_hasher.Verify(hash, "another-password"));
    }
}
