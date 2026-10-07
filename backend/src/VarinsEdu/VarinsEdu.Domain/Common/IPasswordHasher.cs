namespace VarinsEdu.Domain.Common;

// Hashes and verifies passwords. Passwords themselves are never stored.
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
