using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.MaterializedView.Postgres;

public static class SekibanDcbMaterializedViewPostgresExtensions
{
    public static IServiceCollection AddSekibanDcbMaterializedViewPostgres(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionStringName = "DcbPostgres")
    {
        var connectionString = ResolveConnectionString(configuration, connectionStringName) ??
            throw new InvalidOperationException($"Connection string '{connectionStringName}' not found.");
        return services.AddSekibanDcbMaterializedViewPostgres(connectionString);
    }

    public static IServiceCollection AddSekibanDcbMaterializedViewPostgres(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSekibanDcbMaterializedView();
        services.TryAddSingleton<IServiceIdProvider, DefaultServiceIdProvider>();
        services.TryAddSingleton<IMvRegistryStore>(_ => new PostgresMvRegistryStore(connectionString));
        services.TryAddSingleton<IMvExecutor>(sp =>
            new PostgresMvExecutor(
                sp.GetRequiredService<Sekiban.Dcb.Storage.IEventStore>(),
                sp.GetRequiredService<Sekiban.Dcb.Domains.IEventTypes>(),
                sp.GetRequiredService<IServiceIdProvider>(),
                sp.GetRequiredService<IMvRegistryStore>(),
                sp.GetRequiredService<IOptions<MvOptions>>(),
                sp.GetRequiredService<ILogger<PostgresMvExecutor>>(),
                connectionString));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, MvCatchUpWorker>());
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
