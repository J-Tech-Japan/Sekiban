using CosmosInfrastructure;
using CosmosInfrastructure.DomainCommon.EventSourcings;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.PubSubs;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Snapshots;
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

        services.AddTransient<ISingleAggregateProjectionQueryStore, MemoryCacheSingleAggregateProjectionQueryStore>();
        services.AddTransient<IDocumentPersistentWriter, CosmosDocumentWriter>();
        services.AddTransient<IDocumentPersistentRepository, CosmosDocumentRepository>();
        services.AddSingleton(new InMemoryDocumentStore());
        services.AddTransient<IDocumentTemporaryWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentTemporaryRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentWriter, DocumentWriterSplitter>();
        services.AddSingleton<IDocumentRepository, DocumentRepositorySplitter>();
        services.AddSingleton(new HybridStoreManager());

        // ユーザー情報
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTransient<IUserInformationFactory, HttpContextUserInformationFactory>();

        // ドメイン統合
        services.AddTransient<IIntegratedEventPublisher, NoIntegratedEventPublisher>();

        // 各ドメインコンテキスト
        services.AddSingleton(CustomerDomainContext.Shared.Dependency.GetRegisteredAggregateEvents());
        services.AddTransient(CustomerDomainContext.Shared.Dependency.GetDependencies());
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
