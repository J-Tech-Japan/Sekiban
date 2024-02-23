using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
using Sekiban.Infrastructure.Postgres.Documents;
namespace Sekiban.Infrastructure.Postgres;

/// <summary>
///     Add PostgresDB services for Sekiban
/// </summary>
public static class PostgresDbServiceCollectionExtensions
{
    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDb(this WebApplicationBuilder builder) =>
        AddSekibanPostgresDb(builder.Services, builder.Configuration);


    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbWithAzureBlobStorage(this WebApplicationBuilder builder)
    {
        builder.AddSekibanAzureBlobStorage();
        return AddSekibanPostgresDb(builder.Services, builder.Configuration);
    }

    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="postgresDbOptions"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDb(
        this WebApplicationBuilder builder,
        SekibanPostgresOptions postgresDbOptions) =>
        AddSekibanPostgresDb(builder.Services, postgresDbOptions);
    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDb(this IServiceCollection services, IConfiguration configuration)
    {
        var options = SekibanPostgresOptions.FromConfiguration(configuration);
        return AddSekibanPostgresDb(services, options);
    }
    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbWithAzureBlobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSekibanAzureBlobStorage(configuration);
        var options = SekibanPostgresOptions.FromConfiguration(configuration);
        return AddSekibanPostgresDb(services, options);
    }
    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="postgresDbOptions"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDb(
        this IServiceCollection services,
        SekibanPostgresOptions postgresDbOptions)
    {
        // データストア
        services.AddTransient<PostgresDbFactory>();
        services.AddSingleton(postgresDbOptions);
        services.AddTransient<IDocumentPersistentWriter, PostgresDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, PostgresDocumentRepository>();
        services.AddTransient<IDocumentRemover, PostgresDbDocumentRemover>();

        return new SekibanPostgresDbOptionsServiceCollection(postgresDbOptions, services);
    }
    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="section">Configuration Section</param>
    /// <param name="configurationRoot">Configuration Root to get Connection String</param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbFromConfigurationSection(
        this IServiceCollection services,
        IConfigurationSection section,
        IConfigurationRoot configurationRoot)
    {
        var options = SekibanPostgresOptions.FromConfigurationSection(section, configurationRoot);
        return AddSekibanPostgresDb(services, options);
    }
}
