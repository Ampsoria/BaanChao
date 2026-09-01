using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RentalManager.Infrastructure.Data;
using RentalManager.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using RentalManager.Core.Interfaces;
using RentalManager.Core.Services;
using RentalManager.Infrastructure.Documents;
using RentalManager.Infrastructure.PromptPay;
using RentalManager.Infrastructure.Slip;
using RentalManager.Infrastructure.Storage;
using RentalManager.Infrastructure.Line;

namespace RentalManager.Infrastructure;

public static class DependencyInjection
{
    /// <param name="contentRootPath">
    /// ใช้เป็นฐานของ Storage:SlipRoot เมื่อค่าที่ตั้งไว้เป็น relative path
    /// ห้ามพึ่ง current directory เพราะบน IIS ไม่ได้ชี้ไปที่โฟลเดอร์แอปเสมอไป
    /// </param>
    public static IServiceCollection AddRentalInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration,
        string? contentRootPath = null)
    {
        // ของจริงเป็น SQL Server เสมอ SQLite มีไว้เฉพาะให้ลองเปิดหน้าจอบนเครื่อง dev
        // ที่ยังไม่มี SQL Server โดยไม่ต้องติดตั้งอะไร (ดู README)
        var useSqlite = string.Equals(configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase);
        services.AddDbContext<RentalDbContext>(options =>
        {
            if (useSqlite) options.UseSqlite(connectionString);
            else options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
        });
        services.AddScoped<RentalOperationsService>();
        services.Configure<BillingOptions>(options =>
        {
            if (byte.TryParse(configuration["Billing:DueDay"], out var dueDay) && dueDay > 0)
                options.DueDay = dueDay;
            if (byte.TryParse(configuration["Billing:MinimumStayMonths"], out var minimumStay))
                options.MinimumStayMonths = minimumStay;
        });
        services.Configure<FileStorageOptions>(options =>
        {
            options.SlipRoot = ResolveSlipRoot(configuration["Storage:SlipRoot"], contentRootPath);
            if (int.TryParse(configuration["Storage:MaxUploadMegabytes"], out var maxUpload))
                options.MaxUploadMegabytes = maxUpload;
        });
        services.Configure<ExternalSlipVerifierOptions>(options =>
        {
            options.Enabled = bool.TryParse(configuration["SlipVerification:External:Enabled"], out var enabled) && enabled;
            options.Endpoint = configuration["SlipVerification:External:Endpoint"] ?? "";
            options.ApiKey = configuration["SlipVerification:External:ApiKey"] ?? "";
        });
        services.Configure<LineOptions>(options =>
        {
            options.Enabled = bool.TryParse(configuration["Line:Enabled"], out var enabled) && enabled;
            options.ChannelSecret = configuration["Line:ChannelSecret"] ?? "";
            options.ChannelAccessToken = configuration["Line:ChannelAccessToken"] ?? "";
        });
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IPromptPayService, PromptPayService>();
        services.AddSingleton<IReceiptService, ReceiptService>();
        services.AddTransient<LocalSlipVerifier>();
        services.AddHttpClient<ExternalSlipVerifier>();
        services.AddHttpClient<ILineMessenger, LineMessenger>(client =>
            client.BaseAddress = new Uri("https://api.line.me/"));
        return services;
    }

    /// <summary>
    /// สลิปคือหลักฐานการชำระเงิน หายแล้วหายเลย ตำแหน่งที่เก็บจึงต้องคาดเดาได้แน่นอน
    /// relative path จะอิงโฟลเดอร์ของแอป ไม่ใช่ current directory ซึ่งบน IIS อาจเป็นที่อื่น
    /// </summary>
    public static string ResolveSlipRoot(string? configured, string? contentRootPath)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? "slips" : configured.Trim();
        if (Path.IsPathRooted(value)) return Path.GetFullPath(value);
        var basePath = string.IsNullOrWhiteSpace(contentRootPath) ? AppContext.BaseDirectory : contentRootPath;
        return Path.GetFullPath(Path.Combine(basePath, value));
    }
}
