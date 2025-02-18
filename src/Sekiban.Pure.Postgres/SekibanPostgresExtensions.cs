using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Postgres;

public static class SekibanPostgresExtensions
{
    public static IHostApplicationBuilder AddSekibanPostgresDb(
        this IHostApplicationBuilder builder)
    {
        builder.Services.AddTransient<IEventWriter, PostgresDbEventWriter>();
        builder.Services.AddTransient<PostgresDbFactory>();
        builder.Services.AddTransient<IPostgresMemoryCacheAccessor, PostgresMemoryCacheAccessor>();
        builder.Services.AddTransient<IEventReader, PostgresDbEventReader>();
        var dbOption =
            SekibanPostgresDbOption.FromConfiguration(
                builder.Configuration.GetSection("Sekiban"),
                (builder.Configuration as IConfigurationRoot)!);
        builder.Services.AddSingleton(dbOption);
        builder.Services.AddMemoryCache();
        return builder;
    }
}