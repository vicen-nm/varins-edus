using Microsoft.AspNetCore.Identity;
using VarinsEdu.Domain.Common;

namespace VarinsEdu.Infrastructure.Security;

public class PasswordHasherService : IPasswordHasher
{
    // PasswordHasher<T> asks for a user object, but it does not use it for hashing.
    private static readonly object NoUser = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(NoUser, password);

    public bool Verify(string passwordHash, string password) =>
        _hasher.VerifyHashedPassword(NoUser, passwordHash, password) != PasswordVerificationResult.Failed;
}
