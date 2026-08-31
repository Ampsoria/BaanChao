using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace RentalManager.Api.Services;

public sealed class PublicLinkSigner(IConfiguration configuration)
{
    public string CreateInvoiceQrToken(int invoiceId, DateTime expiresUtc)
    {
        var payload = $"{invoiceId}|{new DateTimeOffset(expiresUtc).ToUnixTimeSeconds()}";
        var signature = Sign(payload);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload)) + "." + WebEncoders.Base64UrlEncode(signature);
    }

    public bool ValidateInvoiceQrToken(int invoiceId, string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) return false;
        try
        {
            var payload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(parts[0]));
            var supplied = WebEncoders.Base64UrlDecode(parts[1]);
            var values = payload.Split('|');
            return values.Length == 2 && int.TryParse(values[0], out var id) && id == invoiceId &&
                   long.TryParse(values[1], out var expiry) && DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= expiry &&
                   CryptographicOperations.FixedTimeEquals(Sign(payload), supplied);
        }
        catch (FormatException) { return false; }
    }

    private byte[] Sign(string payload)
    {
        var key = configuration["PublicLinks:SigningKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException("PublicLinks:SigningKey ต้องยาวอย่างน้อย 32 ตัวอักษร");
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(payload));
    }
}
