using System.Text.RegularExpressions;
using VarinsEdu.Infrastructure.Security;
using Xunit;

namespace VarinsEdu.Tests;

public class ActivationCodeTests
{
    private static readonly ActivationCodeHasher Hasher = new(new string('k', 48));

    [Fact]
    public void Generated_codes_have_the_expected_shape()
    {
        for (var i = 0; i < 200; i++)
        {
            // 4 + 4 characters from the alphabet (no I, L, O, 0 or 1), separated by a hyphen.
            Assert.Matches(@"^[A-HJKMNP-Z2-9]{4}-[A-HJKMNP-Z2-9]{4}$", ActivationCodeGenerator.Generate());
        }
    }

    [Fact]
    public void Generated_codes_do_not_repeat_in_a_large_sample()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => ActivationCodeGenerator.Generate()).ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Normalize_ignores_case_spaces_and_hyphens()
    {
        Assert.Equal("ABCD2345", ActivationCodeGenerator.Normalize("abcd-2345"));
        Assert.Equal("ABCD2345", ActivationCodeGenerator.Normalize("  ABCD 2345 "));
        Assert.Equal("ABCD2345", ActivationCodeGenerator.Normalize("abcd2345"));
        Assert.Equal(string.Empty, ActivationCodeGenerator.Normalize(null));
    }

    [Fact]
    public void Hash_ignores_the_formatting_of_the_code()
    {
        Assert.Equal(Hasher.Hash("ABCD-2345"), Hasher.Hash("abcd 2345"));
    }

    [Fact]
    public void Hash_depends_on_the_secret()
    {
        var other = new ActivationCodeHasher(new string('x', 48));

        Assert.NotEqual(Hasher.Hash("ABCD-2345"), other.Hash("ABCD-2345"));
    }

    [Fact]
    public void Verify_accepts_only_the_right_code()
    {
        var hash = Hasher.Hash("ABCD-2345");

        Assert.True(Hasher.Verify(hash, "abcd-2345"));
        Assert.False(Hasher.Verify(hash, "ABCD-2346"));
    }

    [Fact]
    public void Hash_is_a_hex_string_that_does_not_reveal_the_code()
    {
        var hash = Hasher.Hash("ABCD-2345");

        Assert.Matches("^[0-9A-F]{64}$", hash);
        Assert.DoesNotContain("ABCD", hash);
    }
}
