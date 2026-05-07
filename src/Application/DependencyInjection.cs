using BH_DataIngestionService.Application.Services;
using BH_DataIngestionService.Application.Validation;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<TransactionValidator>();
        services.AddScoped<TransactionIngestionService>();
        services.AddScoped<TransactionQueryService>();
        services.AddScoped<StatsService>();

        return services;
    }
}
