using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Command;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate.Commands;
using System.Reflection;
using SnapshotManager = Sekiban.Core.Snapshot.Aggregate.SnapshotManager;
namespace Sekiban.Core.Dependency;

public static class SekibanEventSourcingDependency
{
    public static Assembly GetAssembly() => Assembly.GetExecutingAssembly();

    public static IServiceCollection AddSekibanCoreWithDependency(
        this IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null,
        ServiceCollectionExtensions.MultiProjectionType multiProjectionType = ServiceCollectionExtensions.MultiProjectionType.MemoryCache)

    {
        Register(services, dependencyDefinition, sekibanDateProducer, multiProjectionType);
        return services;
    }
    public static IEnumerable<(Type serviceType, Type? implementationType)> GetDependencies()
    {
        // AddAggregate: RecentInMemoryActivity
        yield return (typeof(ICreateCommandHandler<SnapshotManager, Snapshot.Aggregate.Commands.SnapshotManager>),
            typeof(CreateSnapshotManagerHandler));
        yield return (typeof(IChangeCommandHandler<SnapshotManager, ReportVersionToSnapshotManger>),
            typeof(ReportVersionToSnapshotMangerHandler));
    }

    public static void Register(
        IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null,
        ServiceCollectionExtensions.MultiProjectionType multiProjectionType = ServiceCollectionExtensions.MultiProjectionType.MemoryCache)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), GetAssembly());
        // Sekibanイベントソーシング
        services.AddSekibanCore(sekibanDateProducer ?? new SekibanDateProducer(), multiProjectionType);
        // TODO : services.AddSekibanCosmosDB();
        services.AddSekibanHTTPUser();

        services.AddSekibanSettingsFromAppSettings();

        // 各ドメインコンテキスト
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().RegisteredEventTypes);
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().SekibanAggregateTypes);
        services.AddTransient(dependencyDefinition.GetSekibanDependencyOptions().TransientDependencies);
        services.AddTransient(GetDependencies());
    }

    public static IServiceCollection AddSekibanCoreInMemoryTestWithDependency(
        this IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null)

    {
        RegisterForInMemoryTest(services, dependencyDefinition, sekibanDateProducer);
        return services;
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

    public static IServiceCollection AddSekibanCoreForAggregateTestWithDependency(
        this IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null)

    {
        RegisterForAggregateTest(services, dependencyDefinition, sekibanDateProducer);
        return services;
    }
    public static void RegisterForAggregateTest(
        IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), GetAssembly());

        // Sekibanイベントソーシング
        services.AddSekibanCoreAggregateTest(sekibanDateProducer);

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
            }
            else
            {
                services.AddTransient(serviceType, implementationType);
            }
        }
    }
}
