using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.MaterializedView.SqlServer;

public static class SekibanDcbMaterializedViewSqlServerExtensions
{
    public static IServiceCollection AddSekibanDcbMaterializedViewSqlServer(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "DcbSqlServer",
        bool registerHostedWorker = true)
    {
        var connectionString = ResolveConnectionString(configuration, connectionStringName) ??
            throw new InvalidOperationException($"Connection string '{connectionStringName}' not found.");
        return services.AddSekibanDcbMaterializedViewSqlServer(connectionString, registerHostedWorker);
    }

    public static IServiceCollection AddSekibanDcbMaterializedViewSqlServer(
        this IServiceCollection services,
        string connectionString,
        bool registerHostedWorker = true)
    {
        services.AddSekibanDcbMaterializedView();
        services.TryAddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
        services.TryAddSingleton<IMvRegistryStore>(_ => new SqlServerMvRegistryStore(connectionString));
        services.TryAddSingleton<IMvStorageInfoProvider>(_ =>
            new MvStorageInfoProvider(new MvStorageInfo(MvDbType.SqlServer, connectionString)));
        services.TryAddSingleton<IMvExecutor>(sp =>
            SekibanDcbMaterializedViewExtensions.CreateMaterializedViewExecutor<SqlServerMvExecutor>(
                sp,
                connectionString,
                static (factory, registry, options, logger, connection) =>
                    new SqlServerMvExecutor(factory, registry, options, logger, connection),
                static (eventStore, serviceIdProvider, registry, options, logger, connection) =>
                    new SqlServerMvExecutor(eventStore, serviceIdProvider, registry, options, logger, connection)));
        if (registerHostedWorker)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MvCatchUpWorker>());
        }

        return services;
    }

    private static string? ResolveConnectionString(IConfiguration configuration, string connectionName)
    {
        var direct = configuration.GetConnectionString(connectionName);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        var dotted = configuration[$"ConnectionStrings:{connectionName}"];
        if (!string.IsNullOrWhiteSpace(dotted))
        {
            return dotted;
        }

        var aspNetCoreStyle = configuration[connectionName];
        return string.IsNullOrWhiteSpace(aspNetCoreStyle) ? null : aspNetCoreStyle;
    }
}
