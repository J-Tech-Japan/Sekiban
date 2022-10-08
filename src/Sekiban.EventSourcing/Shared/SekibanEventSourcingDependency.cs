using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Settings;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;
using System.Reflection;
namespace Sekiban.EventSourcing.Shared;

public static class SekibanEventSourcingDependency
{
    public static Assembly GetAssembly()
    {
        return Assembly.GetExecutingAssembly();
    }

    public static IEnumerable<(Type serviceType, Type? implementationType)> GetDependencies()
    {
        // Aggregate: RecentInMemoryActivity
        yield return (typeof(ICreateAggregateCommandHandler<SnapshotManager, CreateSnapshotManager>), typeof(CreateSnapshotManagerHandler));
        yield return (typeof(IChangeAggregateCommandHandler<SnapshotManager, ReportAggregateVersionToSnapshotManger>),
            typeof(ReportAggregateVersionToSnapshotMangerHandler));
    }

    public static void Register(
        IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType = ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), GetAssembly());
        // Sekibanイベントソーシング
        services.AddSekibanCore(sekibanDateProducer ?? new SekibanDateProducer(), multipleProjectionType);
        // TODO : services.AddSekibanCosmosDB();
        services.AddSekibanHTTPUser();

        services.AddSekibanSettingsFromAppSettings();

        // 各ドメインコンテキスト
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().RegisteredEventTypes);
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().SekibanAggregateTypes);
        services.AddTransient(dependencyDefinition.GetSekibanDependencyOptions().TransientDependencies);
        services.AddTransient(GetDependencies());
    }

    public static void RegisterForInMemoryTest(
        IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), GetAssembly());

        // Sekibanイベントソーシング
        services.AddSekibanCoreInMemory(sekibanDateProducer);

        services.AddSekibanHTTPUser();

        services.AddSekibanSettingsFromAppSettings();

        // 各ドメインコンテキスト
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().RegisteredEventTypes);
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().SekibanAggregateTypes);
        services.AddTransient(dependencyDefinition.GetSekibanDependencyOptions().TransientDependencies);
        services.AddTransient(GetDependencies());
    }

    public static void RegisterForAggregateTest(
        IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), GetAssembly());

        // Sekibanイベントソーシング
        services.AddSekibanCoreInAggregateTest(sekibanDateProducer);

        services.AddSekibanHTTPUser();

        services.AddSekibanAppSettingsFromObject(new AggregateSettings());

        // 各ドメインコンテキスト
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().RegisteredEventTypes);
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().SekibanAggregateTypes);
        services.AddTransient(dependencyDefinition.GetSekibanDependencyOptions().TransientDependencies);
        services.AddTransient(GetDependencies());
    }

    public static void AddTransient(this IServiceCollection services, IEnumerable<(Type serviceType, Type? implementationType)> dependencies)
    {
        foreach (var (serviceType, implementationType) in dependencies)
        {
            if (implementationType is null)
            {
                services.AddTransient(serviceType);
            } else
            {
                services.AddTransient(serviceType, implementationType);
            }
        }
    }
}
