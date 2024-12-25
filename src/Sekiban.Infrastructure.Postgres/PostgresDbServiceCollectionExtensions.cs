using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Core.Documents;
using Sekiban.Infrastructure.Aws.S3;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
using Sekiban.Infrastructure.Postgres.Documents;
using System.Configuration;
namespace Sekiban.Infrastructure.Postgres;

/// <summary>
///     Add PostgresDB services for Sekiban
/// </summary>
public static class PostgresDbServiceCollectionExtensions
{
    /// <summary>
    ///     Add PostgresDB services for Sekiban
    ///     Need to add blob storage
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection
        AddSekibanPostgresDbOnly(this IHostApplicationBuilder builder) =>
        AddSekibanPostgresDbOnly(builder.Services, builder.Configuration);


    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="builder"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbWithAzureBlobStorage(
        this IHostApplicationBuilder builder)
    {
        builder.AddSekibanAzureBlobStorage();
        return AddSekibanPostgresDbOnly(builder.Services, builder.Configuration);
    }

    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbWithAwsS3(
        this IHostApplicationBuilder builder)
    {
        builder.AddSekibanAwsS3();
        return AddSekibanPostgresDbOnly(builder.Services, builder.Configuration);
    }

    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="postgresDbOptions"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbOnly(
        this IHostApplicationBuilder builder,
        SekibanPostgresOptions postgresDbOptions) =>
        AddSekibanPostgresDbOnly(builder.Services, postgresDbOptions);

    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="connectionStringName"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbOnlyFromConnectionStringName(
        this IHostApplicationBuilder builder,
        string connectionStringName)
    {
        var configuration = builder.Configuration.Build();
        var options = SekibanPostgresOptions.FromConnectionStringName(connectionStringName, configuration);
        return AddSekibanPostgresDbOnly(builder.Services, options);
    }
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbOnlyFromConnectionStringName(
        this IServiceCollection services,
        string connectionStringName,
        IConfiguration configuration)
    {
        var options = SekibanPostgresOptions.FromConnectionStringName(
            connectionStringName,
            configuration as IConfigurationRoot ??
            throw new ConfigurationErrorsException("postgres configuration failed."));
        return AddSekibanPostgresDbOnly(services, options);
    }

    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbOnly(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = SekibanPostgresOptions.FromConfiguration(configuration);
        return AddSekibanPostgresDbOnly(services, options);
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
        return AddSekibanPostgresDbOnly(services, options);
    }

    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbWithAwsS3(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSekibanAwsS3(configuration);
        var options = SekibanPostgresOptions.FromConfiguration(configuration);
        return AddSekibanPostgresDbOnly(services, options);
    }

    /// <summary>
    ///     Add PostgresDB services for Sekiban
    /// </summary>
    /// <param name="services"></param>
    /// <param name="postgresDbOptions"></param>
    /// <returns></returns>
    public static SekibanPostgresDbOptionsServiceCollection AddSekibanPostgresDbOnly(
        this IServiceCollection services,
        SekibanPostgresOptions postgresDbOptions)
    {
        // データストア
        services.AddTransient<PostgresDbFactory>();
        services.AddSingleton(postgresDbOptions);
        services.AddTransient<IDocumentPersistentWriter, PostgresDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, PostgresDocumentRepository>();
        services.AddTransient<IDocumentRemover, PostgresDbDocumentRemover>();
        services.AddTransient<IEventPersistentWriter, PostgresDocumentWriter>();
        services.AddTransient<IEventPersistentRepository, PostgresDocumentRepository>();
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
        return AddSekibanPostgresDbOnly(services, options);
    }
}
