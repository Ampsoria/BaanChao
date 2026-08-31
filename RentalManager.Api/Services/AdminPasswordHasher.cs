using System.Security.Cryptography;

namespace RentalManager.Api.Services;

/// <summary>
/// แฮชรหัสผ่านผู้ดูแลด้วย PBKDF2-HMAC-SHA256
///
/// เก็บรหัสผ่านเป็น plaintext ใน config ไม่ปลอดภัยพอเมื่อหน้า login เปิดสู่อินเทอร์เน็ต
/// ใครอ่าน environment variable หรือไฟล์ config ได้ ก็เข้าระบบได้ทันที
/// สร้างค่าที่จะใส่ใน Admin:PasswordHash ด้วยคำสั่ง: dotnet run --project RentalManager.Api -- hash-password
/// </summary>
public static class AdminPasswordHasher
{
    private const string Prefix = "pbkdf2";
    private const int Iterations = 210_000; // ตามคำแนะนำ OWASP สำหรับ PBKDF2-HMAC-SHA256
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Join('$', Prefix, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix) return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations < 1) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }
        if (salt.Length == 0 || expected.Length < 16) return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static bool IsHash(string? value) =>
        value is not null && value.StartsWith(Prefix + "$", StringComparison.Ordinal);
}
