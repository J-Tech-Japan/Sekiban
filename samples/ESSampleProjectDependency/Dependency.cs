using CosmosInfrastructure;
using CosmosInfrastructure.DomainCommon.EventSourcings;
using CustomerDomainContext.Aggregates.Branches.Events;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.PubSubs;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Snapshots;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
namespace ESSampleProjectDependency;

public static class Dependency
{
    public static void Register(IServiceCollection services)
    {
        services.AddMemoryCache();

        // データストア
        services.AddTransient<CosmosDbFactory>();

        // イベントソーシング
        services.AddMediatR(Assembly.GetExecutingAssembly());
        services.AddTransient<AggregateEventPublisher>();

        services.AddTransient<AggregateCommandExecutor>();
        services.AddTransient<SingleAggregateService>();
        services.AddTransient<SnapshotListWriter>();
        services.AddTransient<SnapshotListReader>();

        services
            .AddTransient<ISingleAggregateProjectionQueryStore,
                MemoryCacheSingleAggregateProjectionQueryStore>();
        services.AddTransient<IDocumentWriter, CosmosDocumentWriter>();
        services.AddTransient<IDocumentRepository, CosmosDocumentRepository>();

        // ドメイン統合
        services.AddTransient<IIntegratedEventPublisher, NoIntegratedEventPublisher>();

        // 各ドメインコンテキスト
        services.AddTransient(CustomerDomainContext.Shared.Dependency.Enumerate());

        services.AddTransient<IUserInformationFactory, HttpContextUserInformationFactory>();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddSingleton(new RegisteredEventTypes(typeof(BranchCreated).Assembly));
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
            }
            else
            {
                services.AddTransient(serviceType, implementationType);
            }
        }
    }
}
