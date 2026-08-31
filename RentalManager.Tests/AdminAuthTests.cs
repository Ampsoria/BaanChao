using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using RentalManager.Api.Services;
using Xunit;

namespace RentalManager.Tests;

public sealed class AdminPasswordHasherTests
{
    [Fact]
    public void Hash_ProducesAVerifiableValueThatDiffersEveryTime()
    {
        var first = AdminPasswordHasher.Hash("correct horse battery staple");
        var second = AdminPasswordHasher.Hash("correct horse battery staple");

        Assert.NotEqual(first, second); // salt ต่างกันทุกครั้ง
        Assert.True(AdminPasswordHasher.IsHash(first));
        Assert.True(AdminPasswordHasher.Verify("correct horse battery staple", first));
        Assert.True(AdminPasswordHasher.Verify("correct horse battery staple", second));
        Assert.False(AdminPasswordHasher.Verify("wrong password", first));
        // รหัสผ่านต้องไม่ปรากฏในค่าที่เก็บ
        Assert.DoesNotContain("correct horse", first, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext-password")]
    [InlineData("pbkdf2$notanumber$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2$210000$!!!notbase64!!!$aGFzaA==")]
    [InlineData("pbkdf2$210000$c2FsdA==$c2hvcnQ=")] // hash สั้นเกินไป
    [InlineData("pbkdf2$210000$c2FsdA==")]          // ส่วนประกอบไม่ครบ
    public void Verify_RejectsMalformedValuesInsteadOfThrowing(string encoded)
    {
        Assert.False(AdminPasswordHasher.Verify("anything", encoded));
        if (encoded.Length > 0 && !encoded.StartsWith("pbkdf2$", StringComparison.Ordinal))
            Assert.False(AdminPasswordHasher.IsHash(encoded));
    }
}

public sealed class AdminLoginTests : IAsyncLifetime
{
    private const string ConnectionVariable = "ConnectionStrings__RentalDb";
    private const string ConnectionString =
        "Server=localhost;Database=NotUsed;User Id=sa;Password=NotUsed_123;TrustServerCertificate=True";
    private const string Password = "Test_Admin_2026";

    private string? previousConnectionString;
    private WebApplicationFactory<Program> factory = null!;

    public ValueTask InitializeAsync()
    {
        previousConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        Environment.SetEnvironmentVariable(ConnectionVariable, ConnectionString);
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:RentalDb"] = ConnectionString,
                    ["Database:InitializeOnStartup"] = "false",
                    ["Automation:Enabled"] = "false",
                    ["Admin:Username"] = "amp",
                    // เก็บเป็น hash ไม่ใช่ plaintext
                    ["Admin:PasswordHash"] = AdminPasswordHasher.Hash(Password),
                    ["Admin:Password"] = "",
                    ["Admin:LoginAttemptsPerWindow"] = "3",
                    ["Admin:LoginWindowMinutes"] = "5"
                })));
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Login_AcceptsHashedPassword_AndRejectsWrongOne()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { username = "amp", password = "nope" }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var correct = await client.PostAsJsonAsync("/api/auth/login", new { username = "amp", password = Password }, ct);
        Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me", ct)).StatusCode);
    }

    [Fact]
    public async Task Login_StopsAnsweringAfterTooManyAttemptsFromTheSameCaller()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // ตั้งเพดานไว้ 3 ครั้งต่อหน้าต่าง คำขอที่ 4 ต้องโดนปฏิเสธก่อนถึงการตรวจรหัสผ่าน
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login", new { username = "amp", password = $"guess-{attempt}" }, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var blocked = await client.PostAsJsonAsync(
            "/api/auth/login", new { username = "amp", password = Password }, ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        Environment.SetEnvironmentVariable(ConnectionVariable, previousConnectionString);
    }
}
