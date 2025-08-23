using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            
            // Suppress specific warnings and errors related to migrations
            options.ConfigureWarnings(warnings =>
            {
                // Suppress migration-related warnings
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
                
                // Log migration-related command errors as warnings instead of errors
                warnings.Log(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandError);
            });
            
            // Use a custom logger to filter out migration history table errors
            var loggerFactory = sp.GetService<ILoggerFactory>();
            if (loggerFactory != null)
            {
                options.UseLoggerFactory(loggerFactory);
                options.EnableSensitiveDataLogging(false);
            }
        });

        // Add a hosted service to ensure database tables exist
        services.AddHostedService<DatabaseInitializerService>();

        // IEventStore実装を登録
        services.AddSingleton<IEventStore, PostgresEventStore>();

        return services;
    }

    /// <summary>
    /// Background service to ensure database tables exist without using migrations
    /// </summary>
    private class DatabaseInitializerService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DatabaseInitializerService> _logger;

        public DatabaseInitializerService(IServiceProvider serviceProvider, ILogger<DatabaseInitializerService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContextFactory = scope.ServiceProvider.GetService<IDbContextFactory<SekibanDcbDbContext>>();
            
            if (dbContextFactory != null)
            {
                try
                {
                    await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                    
                    // Check if database can be connected
                    var canConnect = await context.Database.CanConnectAsync(cancellationToken);
                    if (canConnect)
                    {
                        // Use EnsureCreated instead of migrations to avoid migration history table issues
                        var created = await context.Database.EnsureCreatedAsync(cancellationToken);
                        if (created)
                        {
                            _logger.LogInformation("Sekiban DCB database tables created successfully.");
                        }
                        else
                        {
                            _logger.LogDebug("Sekiban DCB database tables already exist.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Cannot connect to database. Tables will be created when connection is available.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize database. Tables will be created on first use.");
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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

            var delay = TimeSpan.FromSeconds(delaySeconds);
            var retryCount = 0;

            // データベース接続確認とリトライ処理
            while (retryCount < maxRetries)
            {
                using var dbContext = dbContextFactory.CreateDbContext();
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
                        if (retryCount == 0)
                        {
                            logger.LogInformation("Waiting for database to be ready...");
                        }
                        logger.LogDebug("Cannot connect to database. Attempt {RetryCount}/{MaxRetries}. Retrying in {DelaySeconds} seconds...",
                            retryCount + 1, maxRetries, delaySeconds);
                        await Task.Delay(delay);
                        retryCount++;
                    }
                }
                catch (Exception ex)
                {
                    if (retryCount == 0)
                    {
                        logger.LogInformation("Database not ready yet. Will retry connection...");
                    }
                    logger.LogDebug(ex, "Database connection attempt {RetryCount}/{MaxRetries} failed. Retrying in {DelaySeconds} seconds...",
                        retryCount + 1, maxRetries, delaySeconds);
                    await Task.Delay(delay);
                    retryCount++;
                }
            }

            if (retryCount >= maxRetries)
            {
                logger.LogError("Failed to connect to database after {MaxRetries} attempts. Creating database schema...", maxRetries);
                using var dbContext = dbContextFactory.CreateDbContext();
                await dbContext.Database.EnsureCreatedAsync();
                logger.LogInformation("Database schema created successfully using EnsureCreated.");
                return;
            }

            // マイグレーション実行（リトライロジック付き）
            retryCount = 0;
            while (retryCount < maxRetries)
            {
                using var dbContext = dbContextFactory.CreateDbContext();
                try
                {
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
                    break; // Success - exit the retry loop
                }
                catch (Exception ex) when (retryCount < maxRetries - 1)
                {
                    // Check if it's a transient error (like migration history table not existing yet)
                    if (ex.Message.Contains("__EFMigrationsHistory") || 
                        ex.Message.Contains("does not exist") ||
                        ex.Message.Contains("42P01") || // PostgreSQL table does not exist error code
                        ex.Message.Contains("connection") ||
                        ex.InnerException?.Message.Contains("__EFMigrationsHistory") == true)
                    {
                        if (retryCount == 0)
                        {
                            logger.LogDebug("Migration history table might not exist yet. Creating database structure...");
                        }
                        else
                        {
                            logger.LogDebug("Migration check attempt {RetryCount}/{MaxRetries} failed. Retrying in {DelaySeconds} seconds...",
                                retryCount + 1, maxRetries, delaySeconds);
                        }
                        await Task.Delay(delay);
                        retryCount++;
                    }
                    else
                    {
                        // Not a transient error, rethrow
                        throw;
                    }
                }
            }
            
            if (retryCount >= maxRetries)
            {
                logger.LogInformation("Initial migration check did not complete. Attempting direct migration...");
                using var dbContext = dbContextFactory.CreateDbContext();
                try
                {
                    await dbContext.Database.MigrateAsync();
                    logger.LogInformation("Database migration completed successfully.");
                }
                catch (Exception migEx)
                {
                    // Final attempt - check if it's because the database is already up to date
                    try
                    {
                        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
                        if (!pendingMigrations.Any())
                        {
                            logger.LogInformation("Database is already up to date.");
                        }
                        else
                        {
                            logger.LogError(migEx, "Migration failed with pending migrations.");
                            throw;
                        }
                    }
                    catch
                    {
                        logger.LogWarning("Could not verify migration status, but database operations may still work.");
                    }
                }
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
