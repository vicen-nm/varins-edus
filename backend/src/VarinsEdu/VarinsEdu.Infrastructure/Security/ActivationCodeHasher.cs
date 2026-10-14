using System.Security.Cryptography;
using System.Text;

namespace VarinsEdu.Infrastructure.Security;

// Turns an activation code into a fingerprint that is safe to store.
// Unlike passwords, codes are random and high-entropy, so a fast keyed hash (HMAC) is the right tool:
// without the secret key, a stolen database cannot even be used to test guesses.
public sealed class ActivationCodeHasher
{
    private readonly byte[] _key;

    public ActivationCodeHasher(string secret)
    {
        // The key is derived for this one purpose, so the original secret is never used directly.
        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(secret),
            outputLength: 32,
            salt: null,
            info: Encoding.UTF8.GetBytes("varinsedu-activation-code-v1"));
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
