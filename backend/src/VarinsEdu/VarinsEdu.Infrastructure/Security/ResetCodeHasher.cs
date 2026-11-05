using System.Security.Cryptography;
using System.Text;

namespace VarinsEdu.Infrastructure.Security;

// Turns a password reset code into a fingerprint that is safe to store.
// It has its own derived key, so it can never be mixed up with the activation codes.
public sealed class ResetCodeHasher
{
    private readonly byte[] _key;

    public ResetCodeHasher(string secret)
    {
        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(secret),
            outputLength: 32,
            salt: null,
            info: Encoding.UTF8.GetBytes("varinsedu-password-reset-code-v1"));
    }

    public string Hash(string code) =>
        Convert.ToHexString(
            HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ActivationCodeGenerator.Normalize(code))));

    // Compares in constant time, so the response time reveals nothing about the stored value.
    public bool Verify(string expectedHash, string code) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHash),
            Encoding.ASCII.GetBytes(Hash(code)));
}
