using Microsoft.Extensions.Configuration;
using RentalManager.Api.Services;
using Xunit;

namespace RentalManager.Tests;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void ValidManualConfiguration_HasNoIssuesWithoutOptionalIntegrations()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Admin:Username"] = "amp",
            ["Admin:PasswordHash"] = AdminPasswordHasher.Hash("strong-password"),
            ["PromptPay:Target"] = "0812345678",
            ["Line:Enabled"] = "false",
            ["SlipVerification:External:Enabled"] = "false"
        });

        Assert.Empty(ProductionConfigurationValidator.FindIssues(configuration));
    }

    [Fact]
    public void EnabledIntegrations_RequireHttpsAndCredentials()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Admin:Username"] = "amp",
            ["Admin:PasswordHash"] = "plaintext",
            ["PromptPay:Target"] = "not-a-number",
            ["Line:Enabled"] = "true",
            ["PublicLinks:BaseUrl"] = "http://rental.example",
            ["SlipVerification:External:Enabled"] = "true",
            ["SlipVerification:External:Endpoint"] = "http://slip.example"
        });

        var issues = ProductionConfigurationValidator.FindIssues(configuration);

        Assert.Contains(issues, issue => issue.Contains("PasswordHash", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("PromptPay", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("ChannelSecret", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("ChannelAccessToken", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("SigningKey", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("BaseUrl", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("Endpoint", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Contains("ApiKey", StringComparison.Ordinal));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
