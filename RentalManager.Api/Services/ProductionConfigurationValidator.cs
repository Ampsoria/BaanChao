namespace RentalManager.Api.Services;

public static class ProductionConfigurationValidator
{
    public static IReadOnlyList<string> FindIssues(IConfiguration configuration)
    {
        var issues = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration["Admin:Username"]))
            issues.Add("Admin:Username ยังไม่ได้ตั้ง");
        if (!AdminPasswordHasher.IsHash(configuration["Admin:PasswordHash"]))
            issues.Add("Admin:PasswordHash ต้องเป็น PBKDF2 hash");

        var promptPay = DigitsOnly(configuration["PromptPay:Target"]);
        if (!((promptPay.Length == 10 && promptPay.StartsWith('0')) || promptPay.Length == 13))
            issues.Add("PromptPay:Target ต้องเป็นเบอร์มือถือ 10 หลักหรือเลขประจำตัว 13 หลัก");

        if (configuration.GetValue("Line:Enabled", false))
        {
            if (string.IsNullOrWhiteSpace(configuration["Line:ChannelSecret"]))
                issues.Add("Line:ChannelSecret ยังไม่ได้ตั้ง");
            if (string.IsNullOrWhiteSpace(configuration["Line:ChannelAccessToken"]))
                issues.Add("Line:ChannelAccessToken ยังไม่ได้ตั้ง");
            if ((configuration["PublicLinks:SigningKey"]?.Length ?? 0) < 32)
                issues.Add("PublicLinks:SigningKey ต้องยาวอย่างน้อย 32 ตัวอักษร");
            if (!IsHttpsUrl(configuration["PublicLinks:BaseUrl"]))
                issues.Add("PublicLinks:BaseUrl ต้องเป็น HTTPS URL");
        }

        if (configuration.GetValue("SlipVerification:External:Enabled", false))
        {
            if (!IsHttpsUrl(configuration["SlipVerification:External:Endpoint"]))
                issues.Add("SlipVerification:External:Endpoint ต้องเป็น HTTPS URL");
            if (string.IsNullOrWhiteSpace(configuration["SlipVerification:External:ApiKey"]))
                issues.Add("SlipVerification:External:ApiKey ยังไม่ได้ตั้ง");
        }

        return issues;
    }

    private static string DigitsOnly(string? value) =>
        new((value ?? "").Where(char.IsDigit).ToArray());

    private static bool IsHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
