using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Command;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.Aggregate;
using Sekiban.Core.Snapshot.Aggregate.Commands;
using System.Reflection;
namespace Sekiban.Core.Dependency;

/// <summary>
///     System use Sekiban Dependency Registerer
///     Application developers do not usually use this class directly
/// </summary>
public static class SekibanEventSourcingDependency
{
    public static Assembly GetAssembly() => Assembly.GetExecutingAssembly();

    public static IServiceCollection AddSekibanCoreWithDependency(
        this IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null,
        ServiceCollectionExtensions.MultiProjectionType multiProjectionType = ServiceCollectionExtensions.MultiProjectionType.MemoryCache,
        IConfiguration? configuration = null)

    {
        Register(services, dependencyDefinition, sekibanDateProducer, multiProjectionType, configuration);
        return services;
    }

    public static IEnumerable<(Type serviceType, Type? implementationType)> GetDependencies()
    {
        // AddAggregate: RecentInMemoryActivity
        yield return (typeof(ICommandHandlerCommon<SnapshotManager, CreateSnapshotManager>), typeof(CreateSnapshotManagerHandler));
        yield return (typeof(ICommandHandlerCommon<SnapshotManager, ReportVersionToSnapshotManger>), typeof(ReportVersionToSnapshotMangerHandler));
    }

    public static void Register(
        IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null,
        ServiceCollectionExtensions.MultiProjectionType multiProjectionType = ServiceCollectionExtensions.MultiProjectionType.MemoryCache,
        IConfiguration? configuration = null)
    {
        // MediatR
        services.AddMediatR(new MediatRServiceConfiguration().RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly(), GetAssembly()));
        // Sekiban Event Sourcing
        services.AddSekibanCore(sekibanDateProducer ?? new SekibanDateProducer(), multiProjectionType, configuration);
        services.AddSekibanHTTPUser();
        services.AddSekibanSettingsFromAppSettings();

        // Each Domain contexts
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().RegisteredEventTypes);
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().SekibanAggregateTypes);
        services.AddTransient(dependencyDefinition.GetSekibanDependencyOptions().TransientDependencies);
        services.AddTransient(GetDependencies());

        services.AddQueriesFromDependencyDefinition(dependencyDefinition);

        foreach (var action in dependencyDefinition.GetServiceActions())
        {
            action(services);
        }

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
        services.AddMediatR(new MediatRServiceConfiguration().RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly(), GetAssembly()));

        // Sekiban Event Sourcing
        services.AddSekibanCoreInMemory(sekibanDateProducer);

        services.AddSekibanHTTPUser();

        services.AddSekibanSettingsFromAppSettings();

        // Each Domain contexts
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().RegisteredEventTypes);
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().SekibanAggregateTypes);
        services.AddTransient(dependencyDefinition.GetSekibanDependencyOptions().TransientDependencies);
        services.AddTransient(GetDependencies());

        services.AddQueriesFromDependencyDefinition(dependencyDefinition);
        foreach (var action in dependencyDefinition.GetServiceActions())
        {
            action(services);
        }

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
        services.AddMediatR(new MediatRServiceConfiguration().RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly(), GetAssembly()));

        // Sekiban Event Sourcing
        services.AddSekibanCoreAggregateTest(sekibanDateProducer);

        services.AddSekibanHTTPUser();

        services.AddSekibanAppSettingsFromObject(new AggregateSettings());

        // Each Domain contexts
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().RegisteredEventTypes);
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().SekibanAggregateTypes);
        services.AddTransient(dependencyDefinition.GetSekibanDependencyOptions().TransientDependencies);
        services.AddTransient(GetDependencies());
        services.AddQueriesFromDependencyDefinition(dependencyDefinition);
        foreach (var action in dependencyDefinition.GetServiceActions())
        {
            action(services);
        }
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
