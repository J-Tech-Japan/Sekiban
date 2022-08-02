using CosmosInfrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
using System.Reflection;
using ServiceCollectionExtensions = Sekiban.EventSourcing.ServiceCollectionExtensions;
namespace ESSampleProjectDependency;

public static class Dependency
{
    public static void Register(
        IServiceCollection services,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType = ServiceCollectionExtensions.MultipleProjectionType.MemoryCache)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly());

        // Sekibanイベントソーシング
        services.AddSekibanCore(multipleProjectionType);
        services.AddSekibanCosmosDB();
        services.AddSekibanHTTPUser();

        services.AddSekibanSettingsFromAppSettings();

        // 各ドメインコンテキスト
        services.AddSingleton(
            new RegisteredEventTypes(CustomerDomainContext.Shared.Dependency.GetAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly()));

        services.AddSingleton(
            new SekibanAggregateTypes(CustomerDomainContext.Shared.Dependency.GetAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly()));

        services.AddTransient(CustomerDomainContext.Shared.Dependency.GetDependencies());
        services.AddTransient(Sekiban.EventSourcing.Shared.Dependency.GetDependencies());
    }

    public static void RegisterForInMemoryTest(IServiceCollection services)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly());

        // Sekibanイベントソーシング
        services.AddSekibanCoreInMemory();

        services.AddSekibanHTTPUser();

        services.AddSekibanSettingsFromAppSettings();

        // 各ドメインコンテキスト
        services.AddSingleton(
            new RegisteredEventTypes(CustomerDomainContext.Shared.Dependency.GetAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly()));

        services.AddSingleton(
            new SekibanAggregateTypes(CustomerDomainContext.Shared.Dependency.GetAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly()));

        services.AddTransient(CustomerDomainContext.Shared.Dependency.GetDependencies());
        services.AddTransient(Sekiban.EventSourcing.Shared.Dependency.GetDependencies());
    }

    public static void RegisterForAggregateTest(IServiceCollection services)
    {
        // MediatR
        services.AddMediatR(Assembly.GetExecutingAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly());

        // Sekibanイベントソーシング
        services.AddSekibanCoreInAggregateTest();

        services.AddSekibanHTTPUser();

        services.AddSekibanSettingsFromAppSettings();

        // 各ドメインコンテキスト
        services.AddSingleton(
            new RegisteredEventTypes(CustomerDomainContext.Shared.Dependency.GetAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly()));

        services.AddSingleton(
            new SekibanAggregateTypes(CustomerDomainContext.Shared.Dependency.GetAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly()));

        services.AddTransient(CustomerDomainContext.Shared.Dependency.GetDependencies());
        services.AddTransient(Sekiban.EventSourcing.Shared.Dependency.GetDependencies());
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
