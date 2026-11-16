using System.Security.Cryptography;
using System.Text;

namespace VarinsEdu.Infrastructure.Security;

// Turns a refresh token into a fingerprint that is safe to store.
// It has its own derived key, so it can never be mixed up with the other secrets.
public sealed class RefreshTokenHasher
{
    private readonly byte[] _key;

    public RefreshTokenHasher(string secret)
    {
        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(secret),
            outputLength: 32,
            salt: null,
            info: Encoding.UTF8.GetBytes("varinsedu-refresh-token-v1"));
    }

    public string Hash(string token) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(token)));
}
