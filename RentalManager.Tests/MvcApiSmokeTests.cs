using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace RentalManager.Tests;

public sealed class MvcApiSmokeTests : IAsyncLifetime
{
    private const string ConnectionVariable = "ConnectionStrings__RentalDb";
    private string? previousConnectionString;
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;

    public ValueTask InitializeAsync()
    {
        // Program intentionally validates the connection string before the test
        // server's late configuration callbacks run, so supply this bootstrap
        // setting exactly as production does. Database initialization stays off.
        previousConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        Environment.SetEnvironmentVariable(
            ConnectionVariable,
            "Server=localhost;Database=NotUsed;User Id=sa;Password=NotUsed_123;TrustServerCertificate=True");
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:RentalDb"] = "Server=localhost;Database=NotUsed;User Id=sa;Password=NotUsed_123;TrustServerCertificate=True",
                    ["Database:InitializeOnStartup"] = "false",
                    ["Automation:Enabled"] = "false",
                    ["Admin:Username"] = "amp",
                    ["Admin:Password"] = "Test_Admin_2026"
                })));
        client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task MvcHomeLoginAuthorizationAndWriteHeader_WorkTogether()
    {
        var ct = TestContext.Current.CancellationToken;
        var home = await client.GetAsync("/", ct);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Amp Rental", await home.Content.ReadAsStringAsync(ct));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/rooms", ct)).StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "amp", password = "Test_Admin_2026" }, ct);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var noHeader = await client.PostAsJsonAsync("/api/admin/invoices/generate", new { billingPeriod = "2026-09" }, ct);
        Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/not-a-route", ct)).StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
        Environment.SetEnvironmentVariable(ConnectionVariable, previousConnectionString);
    }
}
