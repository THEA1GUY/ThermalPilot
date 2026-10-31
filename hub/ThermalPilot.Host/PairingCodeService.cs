using System.Security.Cryptography;
using System.Text;

namespace ThermalPilot.Host;

public sealed class PairingCodeService(TimeProvider clock)
{
    private readonly object gate = new();
    private byte[]? codeHash;
    private DateTimeOffset expiresAtUtc;

    public (string Code, DateTimeOffset ExpiresAtUtc) Create()
    {
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        lock (gate)
        {
            codeHash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
            expiresAtUtc = clock.GetUtcNow().AddMinutes(5);
            return (code, expiresAtUtc);
        }
    }

    public bool TryRedeem(string? code)
    {
        if (code is null || code.Length is < 30 or > 64 || code.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            return false;
        var candidate = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        lock (gate)
        {
            if (codeHash is null || clock.GetUtcNow() > expiresAtUtc || !CryptographicOperations.FixedTimeEquals(codeHash, candidate))
                return false;
            CryptographicOperations.ZeroMemory(codeHash);
            codeHash = null;
            expiresAtUtc = default;
            return true;
        }
    }
}
