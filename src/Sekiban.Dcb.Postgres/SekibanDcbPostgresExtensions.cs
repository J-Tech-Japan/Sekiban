using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Aspire環境でSekiban DCB PostgreSQLを設定し、マイグレーション機能を含める
    /// </summary>
    public static IServiceCollection AddSekibanDcbPostgresWithAspire(
        this IServiceCollection services,
        string connectionName = "DcbPostgres")
    {
        // DbContextFactoryを登録
        services.AddDbContextFactory<SekibanDcbDbContext>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(connectionName);
            options.UseNpgsql(connectionString);
        });

        // IEventStore実装を登録
        services.AddSingleton<IEventStore, PostgresEventStore>();

        return services;
    }

    /// <summary>
    /// Aspire環境でSekiban DCB PostgreSQLを設定し、ドメインタイプも含める
    /// </summary>
    public static IServiceCollection AddSekibanDcbPostgresWithAspire<TDomainTypesProvider>(
        this IServiceCollection services,
        string connectionName = "DcbPostgres")
        where TDomainTypesProvider : IDcbDomainTypesProvider
    {
        // DbContextFactoryを登録
        services.AddDbContextFactory<SekibanDcbDbContext>((sp, options) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(connectionName);
            options.UseNpgsql(connectionString);
        });

        // DcbDomainTypesを登録
        services.AddSingleton<DcbDomainTypes>(sp =>
        {
            var jsonOptions = new System.Text.Json.JsonSerializerOptions();
            return TDomainTypesProvider.Generate(jsonOptions);
        });

        // IEventStore実装を登録
        services.AddSingleton<IEventStore, PostgresEventStore>();

        return services;
    }

    /// <summary>
    /// データベースマイグレーションを実行する
    /// </summary>
    public static async Task<WebApplication> MigrateSekibanDcbDatabaseAsync(
        this WebApplication app,
        int maxRetries = 10,
        int delaySeconds = 2)
    {
        using var scope = app.Services.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SekibanDcbDbContext>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<WebApplication>>();

        await MigrateDatabaseInternalAsync(dbContextFactory, logger, maxRetries, delaySeconds);
        return app;
    }

    /// <summary>
    /// サービスプロバイダーからデータベースマイグレーションを実行する
    /// </summary>
    public static async Task MigrateSekibanDcbDatabaseAsync(
        this IServiceProvider serviceProvider,
        int maxRetries = 10,
        int delaySeconds = 2)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SekibanDcbDbContext>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger>();

        await MigrateDatabaseInternalAsync(dbContextFactory, logger, maxRetries, delaySeconds);
    }

    private static async Task MigrateDatabaseInternalAsync(
        IDbContextFactory<SekibanDcbDbContext> dbContextFactory,
        ILogger logger,
        int maxRetries,
        int delaySeconds)
    {
        try
        {
            logger.LogInformation("Starting Sekiban DCB database migration...");

            using var dbContext = dbContextFactory.CreateDbContext();
            var delay = TimeSpan.FromSeconds(delaySeconds);
            var retryCount = 0;

            // データベース接続確認とリトライ処理
            while (retryCount < maxRetries)
            {
                try
                {
                    var canConnect = await dbContext.Database.CanConnectAsync();
                    if (canConnect)
                    {
                        logger.LogInformation("Database connection successful.");
                        break;
                    }
                    else
                    {
                        logger.LogWarning("Cannot connect to database. Attempt {RetryCount}/{MaxRetries}. Retrying in {DelaySeconds} seconds...",
                            retryCount + 1, maxRetries, delaySeconds);
                        await Task.Delay(delay);
                        retryCount++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Database connection failed. Attempt {RetryCount}/{MaxRetries}. Retrying in {DelaySeconds} seconds...",
                        retryCount + 1, maxRetries, delaySeconds);
                    await Task.Delay(delay);
                    retryCount++;
                }
            }

            if (retryCount >= maxRetries)
            {
                logger.LogError("Failed to connect to database after {MaxRetries} attempts. Creating database schema...", maxRetries);
                await dbContext.Database.EnsureCreatedAsync();
                logger.LogInformation("Database schema created successfully using EnsureCreated.");
                return;
            }

            // マイグレーション実行
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                logger.LogInformation("Found {Count} pending migrations. Applying migrations...", pendingMigrations.Count());
                await dbContext.Database.MigrateAsync();
                logger.LogInformation("Sekiban DCB database migration completed successfully.");
            }
            else
            {
                logger.LogInformation("No pending migrations found. Sekiban DCB database is up to date.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while migrating the Sekiban DCB database. Attempting to ensure database is created...");

            try
            {
                using var dbContext = dbContextFactory.CreateDbContext();
                await dbContext.Database.EnsureCreatedAsync();
                logger.LogInformation("Sekiban DCB database schema created successfully using EnsureCreated.");
            }
            catch (Exception fallbackEx)
            {
                logger.LogError(fallbackEx, "Failed to create Sekiban DCB database schema. Application will continue but database operations may fail.");
                throw;
            }
        }
    }
}
