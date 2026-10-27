using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace VarinsEdu.Infrastructure.Security;

public sealed record CardToken(string Token, DateTimeOffset ExpiresAt);

public enum CardTokenStatus
{
    Valid,
    Invalid,
    Expired
}

public sealed record CardTokenVerification(CardTokenStatus Status, Guid? PersonId, Guid? InstitutionId);

// Short-lived signed token shown as a QR on the student's card: "body.signature".
// The body carries no secrets, only who it is for and when it expires.
public sealed class CardTokenService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(45);

    private const string Version = "v1";
    private const int MaxLength = 512;

    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public CardTokenService(string secret, TimeProvider time)
    {
        _time = time;

        // A key derived for this one purpose: it cannot be confused with the other uses of the secret.
        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(secret),
            outputLength: 32,
            salt: null,
            info: Encoding.UTF8.GetBytes("varinsedu-card-token-v1"));
    }

    public CardToken Issue(Guid personId, Guid institutionId)
    {
        var expires = _time.GetUtcNow().Add(Lifetime);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));

        var payload = $"{Version}|{personId:N}|{institutionId:N}|{expires.ToUnixTimeSeconds()}|{nonce}";
        var body = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
        var signature = Base64Url.EncodeToString(Sign(body));

        return new CardToken($"{body}.{signature}", expires);
    }

    // The signature is checked first: nothing inside an unsigned token is trusted or even parsed.
    public CardTokenVerification Verify(string? token)
    {
        var invalid = new CardTokenVerification(CardTokenStatus.Invalid, null, null);

        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxLength)
        {
            return invalid;
        }

        var parts = token.Split('.');

        if (parts.Length != 2)
        {
            return invalid;
        }

        string payload;

        try
        {
            var given = Base64Url.DecodeFromChars(parts[1]);

            if (!CryptographicOperations.FixedTimeEquals(Sign(parts[0]), given))
            {
                return invalid;
            }

            payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[0]));
        }
        catch (FormatException)
        {
            return invalid;
        }

        var fields = payload.Split('|');

        if (fields.Length != 5
            || fields[0] != Version
            || !Guid.TryParseExact(fields[1], "N", out var personId)
            || !Guid.TryParseExact(fields[2], "N", out var institutionId)
            || !long.TryParse(fields[3], out var expiresUnix))
        {
            return invalid;
        }

        if (expiresUnix <= _time.GetUtcNow().ToUnixTimeSeconds())
        {
            return new CardTokenVerification(CardTokenStatus.Expired, null, null);
        }

        return new CardTokenVerification(CardTokenStatus.Valid, personId, institutionId);
    }

    private byte[] Sign(string body) => HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body));
}
