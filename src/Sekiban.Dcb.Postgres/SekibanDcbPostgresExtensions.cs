using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.Postgres;

public static class SekibanDcbPostgresExtensions
{
    public static IServiceCollection AddSekibanDcbPostgres(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "SekibanDcbConnection")
    {
        var connectionString = configuration.GetConnectionString(connectionStringName) ??
            throw new InvalidOperationException($"Connection string '{connectionStringName}' not found.");

        return services.AddSekibanDcbPostgres(connectionString);
    }

    public static IServiceCollection AddSekibanDcbPostgres(this IServiceCollection services, string connectionString)
    {
        // Add DbContext factory
        services.AddDbContextFactory<SekibanDcbDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Add DbContext for migrations
        services.AddDbContext<SekibanDcbDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
        });

        // Register IEventStore implementation
        services.AddSingleton<IEventStore, PostgresEventStore>();

        return services;
    }
}
