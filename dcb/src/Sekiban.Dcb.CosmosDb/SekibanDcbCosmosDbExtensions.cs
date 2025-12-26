using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Service collection extensions for CosmosDB-backed DCB.
/// </summary>
public static class SekibanDcbCosmosDbExtensions
{
    private static readonly Action<ILogger, string, Exception?> LogUsingAspireClient =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, nameof(LogUsingAspireClient)), "Using Aspire-provided CosmosClient for database {DatabaseName}");

    private static readonly Action<ILogger, string, Exception?> LogUsingConnectionString =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(2, nameof(LogUsingConnectionString)), "Using connection string for CosmosDB database {DatabaseName}");

    /// <summary>
    ///     Add Sekiban DCB with CosmosDB storage
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDb(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register CosmosDbContext as singleton
        services.AddSingleton<CosmosDbContext>(provider =>
        {
            var logger = provider.GetService<ILogger<CosmosDbContext>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "SekibanDcb";

            // Check for connection strings in order of specificity
            var connectionString = configuration.GetConnectionString("SekibanDcbCosmos")
                ?? configuration.GetConnectionString("SekibanDcbCosmosDb")
                ?? configuration.GetConnectionString("CosmosDb")
                ?? configuration.GetConnectionString("cosmosdb");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    "No CosmosDB connection string found. Configure a connection string in " +
                    "'ConnectionStrings:SekibanDcbCosmos', 'ConnectionStrings:SekibanDcbCosmosDb', " +
                    "'ConnectionStrings:CosmosDb', or 'ConnectionStrings:cosmosdb'");
            }

            return new CosmosDbContext(connectionString, databaseName, logger);
        });

        // Register IEventStore implementation
        services.AddSingleton<IEventStore, CosmosDbEventStore>();

        return services;
    }

    /// <summary>
    ///     Add Sekiban DCB with CosmosDB storage using connection string
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDb(
        this IServiceCollection services,
        string connectionString,
        string databaseName = "SekibanDcb")
    {
        // Register CosmosDbContext as singleton
        services.AddSingleton<CosmosDbContext>(provider =>
        {
            var logger = provider.GetService<ILogger<CosmosDbContext>>();
            return new CosmosDbContext(connectionString, databaseName, logger);
        });

        // Register IEventStore implementation
        services.AddSingleton<IEventStore, CosmosDbEventStore>();

        return services;
    }

    /// <summary>
    ///     Add Sekiban DCB with CosmosDB storage for Aspire
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosDbWithAspire(
        this IServiceCollection services)
    {
        // Register CosmosDbContext as singleton
        services.AddSingleton<CosmosDbContext>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var logger = provider.GetService<ILogger<CosmosDbContext>>();
            var databaseName = configuration["CosmosDb:DatabaseName"] ?? "SekibanDcb";

            // Try to get CosmosClient from Aspire DI first (if Aspire has registered it)
            var cosmosClient = provider.GetService<Microsoft.Azure.Cosmos.CosmosClient>();

            if (cosmosClient != null)
            {
                // Use Aspire-provided CosmosClient
                if (logger != null)
                {
                    LogUsingAspireClient(logger, databaseName, null);
                }
                return new CosmosDbContext(cosmosClient, databaseName, logger);
            }

            // Fall back to connection string if no Aspire client is available
            // Check for connection strings in order of specificity
            var connectionString = configuration.GetConnectionString("SekibanDcbCosmos")
                ?? configuration.GetConnectionString("SekibanDcbCosmosDb")
                ?? configuration.GetConnectionString("CosmosDb")
                ?? configuration.GetConnectionString("cosmosdb");

            if (!string.IsNullOrEmpty(connectionString))
            {
                if (logger != null)
                {
                    LogUsingConnectionString(logger, databaseName, null);
                }
                return new CosmosDbContext(connectionString, databaseName, logger);
            }

            throw new InvalidOperationException(
                "No CosmosDB configuration found. Either provide a CosmosClient through Aspire, " +
                "or configure a connection string in 'ConnectionStrings:SekibanDcbCosmos', " +
                "'ConnectionStrings:SekibanDcbCosmosDb', 'ConnectionStrings:CosmosDb', or 'ConnectionStrings:cosmosdb'");
        });

        // Register IEventStore implementation
        services.AddSingleton<IEventStore, CosmosDbEventStore>();

        // Register hosted service to ensure containers are created
        services.AddHostedService<CosmosDbInitializer>();

        return services;
    }
}
