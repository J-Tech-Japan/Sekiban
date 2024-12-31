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

    /// <summary>
    ///     Get Dependencies
    /// </summary>
    /// <returns></returns>
    public static IEnumerable<(Type serviceType, Type? implementationType)> GetDependencies()
    {
        // AddAggregate: RecentInMemoryActivity
        yield return (typeof(ICommandHandlerCommon<SnapshotManager, CreateSnapshotManager>),
            typeof(CreateSnapshotManager.Handler));
        yield return (typeof(ICommandHandlerCommon<SnapshotManager, ReportVersionToSnapshotManger>),
            typeof(ReportVersionToSnapshotManger.Handler));
    }

    /// <summary>
    ///     Add Sekiban with Dependency for InMemory Test
    /// </summary>
    /// <param name="services"></param>
    /// <param name="dependencyDefinition"></param>
    /// <param name="sekibanDateProducer"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanCoreInMemoryTestWithDependency(
        this IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null)

    {
        RegisterForInMemoryTest(services, dependencyDefinition, sekibanDateProducer);
        return services;
    }
    /// <summary>
    ///     Register Sekiban with Dependency for InMemory Test
    /// </summary>
    /// <param name="services"></param>
    /// <param name="dependencyDefinition"></param>
    /// <param name="sekibanDateProducer"></param>
    public static void RegisterForInMemoryTest(
        IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null)
    {
        // MediatR
        services.AddMediatR(
            new MediatRServiceConfiguration().RegisterServicesFromAssemblies(
                Assembly.GetExecutingAssembly(),
                GetAssembly()));

        // Sekiban Event Sourcing
        services.AddSekibanCoreInMemory(sekibanDateProducer);

        services.AddSekibanConstantUser();
        services.AddSingleton(SekibanSettings.Default);
        services.AddTransient<IAggregateSettings, ContextAggregateSettings>();
        // run Define() before using.
        dependencyDefinition.Define();

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
    /// <summary>
    ///     Add Sekiban with Dependency for Aggregate Test
    /// </summary>
    /// <param name="services"></param>
    /// <param name="dependencyDefinition"></param>
    /// <param name="sekibanDateProducer"></param>
    /// <returns></returns>
    public static IServiceCollection AddSekibanCoreForAggregateTestWithDependency(
        this IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null)

    {
        RegisterForAggregateTest(services, dependencyDefinition, sekibanDateProducer);
        return services;
    }
    /// <summary>
    ///     Register Sekiban with Dependency for Aggregate Test
    /// </summary>
    /// <param name="services"></param>
    /// <param name="dependencyDefinition"></param>
    /// <param name="sekibanDateProducer"></param>
    public static void RegisterForAggregateTest(
        IServiceCollection services,
        IDependencyDefinition dependencyDefinition,
        ISekibanDateProducer? sekibanDateProducer = null)
    {
        // MediatR
        services.AddMediatR(
            config =>
            {
                config.RegisterServicesFromAssemblies(Assembly.GetExecutingAssembly(), GetAssembly());
            });

        // Sekiban Event Sourcing
        services.AddSekibanCoreAggregateTest(sekibanDateProducer);

        services.AddSekibanConstantUser();
        services.AddSingleton(SekibanSettings.Default);
        services.AddSekibanAppSettingsFromObject(new AggregateSettings());
        // run Define() before using.
        dependencyDefinition.Define();
        var options = dependencyDefinition.GetSekibanDependencyOptions();
        // Each Domain contexts
        services.AddSingleton(options.RegisteredEventTypes);
        services.AddSingleton(dependencyDefinition.GetSekibanDependencyOptions().SekibanAggregateTypes);
        services.AddTransient(dependencyDefinition.GetSekibanDependencyOptions().TransientDependencies);
        services.AddTransient(GetDependencies());
        services.AddQueriesFromDependencyDefinition(dependencyDefinition);
        foreach (var action in dependencyDefinition.GetServiceActions())
        {
            action(services);
        }
    }

    public static void AddTransient(
        this IServiceCollection services,
        IEnumerable<(Type serviceType, Type? implementationType)> dependencies)
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
