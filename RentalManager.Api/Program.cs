using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using RentalManager.Infrastructure;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("RentalDb");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Missing ConnectionStrings:RentalDb. Configure it with User Secrets or an environment variable.");

builder.Services.AddRentalInfrastructure(connectionString, builder.Configuration);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "rental_admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddControllersWithViews().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<RentalManager.Api.Services.PublicLinkSigner>();
builder.Services.AddHostedService<RentalManager.Api.Services.BillingAutomationWorker>();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseExceptionHandler(exceptionHandlerApp => exceptionHandlerApp.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = error is RentalOperationException or RentalManager.Core.Services.BillingRuleException or InvalidDataException
        ? StatusCodes.Status400BadRequest
        : StatusCodes.Status500InternalServerError;
    await Results.Problem(
        title: context.Response.StatusCode == 400 ? "ข้อมูลไม่ผ่านกฎของระบบ" : "เกิดข้อผิดพลาดในระบบ",
        detail: error is RentalOperationException or RentalManager.Core.Services.BillingRuleException or InvalidDataException
            ? error.Message
            : "กรุณาลองใหม่หรือตรวจสอบ log ของเซิร์ฟเวอร์",
        statusCode: context.Response.StatusCode).ExecuteAsync(context);
}));
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var request = context.Request;
    if (request.Path.StartsWithSegments("/api/admin") &&
        !HttpMethods.IsGet(request.Method) &&
        request.Headers["X-Requested-With"] != "RentalAdmin")
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await Results.BadRequest(new { message = "คำขอเขียนข้อมูลไม่มี security header ที่กำหนด" })
            .ExecuteAsync(context);
        return;
    }
    await next();
});

if (app.Configuration.GetValue("Database:InitializeOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<RentalDbContext>();
    await db.Database.MigrateAsync();
}

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program;
