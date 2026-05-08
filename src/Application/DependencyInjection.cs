using BH_DataIngestionService.Application.Services.Ingestion;
using BH_DataIngestionService.Application.Services.Stats;
using BH_DataIngestionService.Application.Services.Transactions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BH_DataIngestionService.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<TransactionIngestionService>();
        services.AddScoped<TransactionIngestionService>();
        services.AddScoped<TransactionLoadGenerationService>();
        services.AddSingleton<TransactionTestDataGenerator>();
        services.AddScoped<TransactionQueryService>();
        services.AddScoped<StatsService>();

        return services;
    }
}
