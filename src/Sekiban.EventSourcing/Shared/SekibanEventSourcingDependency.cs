using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;
using Sekiban.EventSourcing.TestHelpers;
using System.Reflection;
namespace Sekiban.EventSourcing.Shared;

public static class SekibanEventSourcingDependency
{
    public static Assembly GetAssembly() =>
        Assembly.GetExecutingAssembly();

    public static IEnumerable<(Type serviceType, Type? implementationType)> GetDependencies()
    {
        // Aggregate: RecentInMemoryActivity
        yield return (typeof(ICreateAggregateCommandHandler<SnapshotManager, CreateSnapshotManager>), typeof(CreateSnapshotManagerHandler));
        yield return (typeof(IChangeAggregateCommandHandler<SnapshotManager, ReportAggregateVersionToSnapshotManger>),
            typeof(ReportAggregateVersionToSnapshotMangerHandler));
    }

    public static void Register(
        IServiceCollection services,
        SekibanDependencyOptions dependencyOptions,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType = ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), GetAssembly());

        // Sekibanイベントソーシング
        services.AddSekibanCore(multipleProjectionType);
        // TODO : services.AddSekibanCosmosDB();
        services.AddSekibanHTTPUser();

        services.AddSekibanSettingsFromAppSettings();

        // 各ドメインコンテキスト
        services.AddSingleton(dependencyOptions.RegisteredEventTypes);
        services.AddSingleton(dependencyOptions.SekibanAggregateTypes);
        services.AddTransient(dependencyOptions.TransientDependencies);
        services.AddTransient(GetDependencies());
    }

    public static void RegisterForInMemoryTest(IServiceCollection services, SekibanDependencyOptions dependencyOptions)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), GetAssembly());

        // Sekibanイベントソーシング
        services.AddSekibanCoreInMemory();

        services.AddSekibanHTTPUser();

        services.AddSekibanSettingsFromAppSettings();

        // 各ドメインコンテキスト
        services.AddSingleton(dependencyOptions.RegisteredEventTypes);
        services.AddSingleton(dependencyOptions.SekibanAggregateTypes);
        services.AddTransient(dependencyOptions.TransientDependencies);
        services.AddTransient(GetDependencies());
    }

    public static void RegisterForAggregateTest(IServiceCollection services, SekibanDependencyOptions dependencyOptions)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), GetAssembly());

        // Sekibanイベントソーシング
        services.AddSekibanCoreInAggregateTest();

        services.AddSekibanHTTPUser();

        services.AddSekibanSettingsFromAppSettings();

        // 各ドメインコンテキスト
        services.AddSingleton(dependencyOptions.RegisteredEventTypes);
        services.AddSingleton(dependencyOptions.SekibanAggregateTypes);
        services.AddTransient(dependencyOptions.TransientDependencies);
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
