using CosmosInfrastructure;
using CosmosInfrastructure.DomainCommon.EventSourcings;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.PubSubs;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Settings;
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
        services.AddMediatR(Assembly.GetExecutingAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly());
        services.AddTransient<AggregateEventPublisher>();

        services.AddTransient<IAggregateCommandExecutor, AggregateCommandExecutor>();
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
        services.AddSingleton(new HybridStoreManager(true));

        // ユーザー情報
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTransient<IUserInformationFactory, HttpContextUserInformationFactory>();

        // 設定はConfigurationから指定することもできる、設定オブジェクトをnewで生成することも可能
        services.AddTransient<IAggregateSettings, ConfigurationAggregateSettings>();
        // services.AddSingleton<IAggregateSettings>(
        //     new AggregateSettings
        //     {
        //         Helper = new AggregateSettingHelper(
        //             true,
        //             true,
        //             80,
        //             15,
        //             new List<SingleAggregateSetting>
        //             {
        //                 new(nameof(Client), true, true),
        //                 new(nameof(ClientNameHistoryProjection), true, false, 111),
        //                 new(nameof(RecentActivity), true, true, 82, 10)
        //             })
        //     });

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
