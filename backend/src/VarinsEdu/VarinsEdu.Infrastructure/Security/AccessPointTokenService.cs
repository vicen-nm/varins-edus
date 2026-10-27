using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace VarinsEdu.Infrastructure.Security;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public enum AccessTokenStatus
{
    Valid,
    Invalid,
    Expired
}

public sealed record AccessTokenVerification(AccessTokenStatus Status, Guid? PointId, Guid? InstitutionId);

// The rotating QR shown on a point's screen: "body.signature", valid for a few seconds.
// It uses its own derived key, so a card token or any other token can never pass as one of these.
public sealed class AccessPointTokenService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(30);

    private const string Version = "a1";
    private const int MaxLength = 512;

    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public AccessPointTokenService(string secret, TimeProvider time)
    {
        _time = time;

        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(secret),
            outputLength: 32,
            salt: null,
            info: Encoding.UTF8.GetBytes("varinsedu-access-point-token-v1"));
    }

    public AccessToken Issue(Guid pointId, Guid institutionId)
    {
        var expires = _time.GetUtcNow().Add(Lifetime);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));

        var payload = $"{Version}|{pointId:N}|{institutionId:N}|{expires.ToUnixTimeSeconds()}|{nonce}";
        var body = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
        var signature = Base64Url.EncodeToString(Sign(body));

        return new AccessToken($"{body}.{signature}", expires);
    }

    // The signature is checked first: nothing inside an unsigned token is trusted or parsed.
    public AccessTokenVerification Verify(string? token)
    {
        var invalid = new AccessTokenVerification(AccessTokenStatus.Invalid, null, null);

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
            || !Guid.TryParseExact(fields[1], "N", out var pointId)
            || !Guid.TryParseExact(fields[2], "N", out var institutionId)
            || !long.TryParse(fields[3], out var expiresUnix))
        {
            return invalid;
        }

        if (expiresUnix <= _time.GetUtcNow().ToUnixTimeSeconds())
        {
            return new AccessTokenVerification(AccessTokenStatus.Expired, null, null);
        }

        return new AccessTokenVerification(AccessTokenStatus.Valid, pointId, institutionId);
    }

    private byte[] Sign(string body) => HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body));
}
