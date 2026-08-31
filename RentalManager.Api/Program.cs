using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using RentalManager.Api.Services;
using RentalManager.Infrastructure;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Services;

// โหมดช่วยตั้งรหัสผ่าน: พิมพ์ค่าที่จะนำไปใส่ Admin:PasswordHash
// อ่านจาก stdin ไม่ใช่ argument เพื่อไม่ให้รหัสผ่านตกค้างใน shell history
if (args is ["hash-password", ..])
{
    Console.Error.Write("Password: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input))
    {
        Console.Error.WriteLine("ยกเลิก: ไม่ได้กรอกรหัสผ่าน");
        return 1;
    }
    Console.WriteLine(AdminPasswordHasher.Hash(input));
    return 0;
}

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("RentalDb");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Missing ConnectionStrings:RentalDb. Configure it with User Secrets or an environment variable.");

builder.Services.AddRentalInfrastructure(
    connectionString, builder.Configuration, builder.Environment.ContentRootPath);
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
// จำกัดจำนวนครั้งที่ลองล็อกอินต่อ IP กัน brute force เพราะมีผู้ใช้คนเดียวและรหัสผ่านเดียว
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Login, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("Admin:LoginAttemptsPerWindow", 5),
                Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("Admin:LoginWindowMinutes", 5)),
                QueueLimit = 0
            }));
});
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
app.UseRateLimiter();
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
    // migration ทั้งหมดเขียนเป็น T-SQL จึงใช้ได้เฉพาะ SQL Server
    // โหมด SQLite สร้าง schema จากโมเดลตรงๆ เพราะมีไว้ดูหน้าจอเท่านั้น ไม่ได้เก็บข้อมูลจริง
    if (string.Equals(app.Configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase))
        await db.Database.EnsureCreatedAsync();
    else
        await db.Database.MigrateAsync();
}

// สลิปคือหลักฐานการชำระเงิน ถ้าเขียนโฟลเดอร์ไม่ได้ต้องรู้ตั้งแต่ตอนสตาร์ต
// ไม่ใช่ไปรู้ตอนลูกบ้านส่งสลิปมาแล้วหาย — ไม่ crash เพราะบน shared hosting
// จะทำให้หน้า admin เข้าไม่ได้เลยจนวินิจฉัยอะไรไม่ได้
{
    var slipRoot = RentalManager.Infrastructure.DependencyInjection.ResolveSlipRoot(
        app.Configuration["Storage:SlipRoot"], app.Environment.ContentRootPath);
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        Directory.CreateDirectory(slipRoot);
        var probe = Path.Combine(slipRoot, $".write-test-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(probe, "ok");
        File.Delete(probe);
        startupLogger.LogInformation("Slip storage ready at {SlipRoot}", slipRoot);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
        startupLogger.LogError(exception,
            "เขียนโฟลเดอร์เก็บสลิปไม่ได้: {SlipRoot} — ตรวจสิทธิ์ของ Storage:SlipRoot ก่อนรับสลิปจากลูกบ้าน", slipRoot);
    }
}

// เตือนดังๆ ถ้ายังใช้รหัสผ่าน plaintext บนเครื่องจริง
if (!app.Environment.IsDevelopment() &&
    !AdminPasswordHasher.IsHash(app.Configuration["Admin:PasswordHash"]))
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup").LogWarning(
        "Admin:PasswordHash ยังไม่ได้ตั้ง ระบบกำลังใช้รหัสผ่านแบบ plaintext จาก Admin:Password "
        + "สร้างค่าที่ปลอดภัยด้วย: dotnet run --project RentalManager.Api -- hash-password");

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();
return 0;

public partial class Program;
