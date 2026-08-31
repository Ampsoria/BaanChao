using System.Security.Cryptography;
using System.Text;

namespace RentalManager.Infrastructure.Line;

public static class LineSignatureVerifier
{
    public static bool Verify(string body, string suppliedSignature, string channelSecret)
    {
        if (string.IsNullOrWhiteSpace(suppliedSignature) || string.IsNullOrWhiteSpace(channelSecret)) return false;
        try
        {
            var expected = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(channelSecret), Encoding.UTF8.GetBytes(body));
            var supplied = Convert.FromBase64String(suppliedSignature);
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
