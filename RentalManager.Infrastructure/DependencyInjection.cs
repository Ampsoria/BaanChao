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
    public static IServiceCollection AddRentalInfrastructure(
        this IServiceCollection services, string connectionString, IConfiguration configuration)
    {
        services.AddDbContext<RentalDbContext>(options =>
            options.UseSqlServer(connectionString));
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
            options.SlipRoot = configuration["Storage:SlipRoot"] ?? "slips";
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
}
