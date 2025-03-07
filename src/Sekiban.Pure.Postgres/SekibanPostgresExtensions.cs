using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Postgres;

public static class SekibanPostgresExtensions
{
    public static IHostApplicationBuilder AddSekibanPostgresDb(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSekibanPostgresDb(builder.Configuration);
        return builder;
    }
    public static IServiceCollection AddSekibanPostgresDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTransient<IEventWriter, PostgresDbEventWriter>();
        services.AddTransient<PostgresDbFactory>();
        services.AddTransient<IPostgresMemoryCacheAccessor, PostgresMemoryCacheAccessor>();
        services.AddTransient<IEventReader, PostgresDbEventReader>();
        var dbOption = SekibanPostgresDbOption.FromConfiguration(
            configuration.GetSection("Sekiban"),
            (configuration as IConfigurationRoot)!);
        services.AddSingleton(dbOption);
        services.AddMemoryCache();
        return services;
    }

}
