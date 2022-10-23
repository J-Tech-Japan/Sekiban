using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Cache;
using Sekiban.Core.Command;
using Sekiban.Core.Command.UserInformation;
using Sekiban.Core.Document;
using Sekiban.Core.PubSub;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.MultipleAggregate.MultipleProjection;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Query.SingleAggregate.SingleProjection;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Dependency;

public static class ServiceCollectionExtensions
{
    public enum HttpContextType
    {
        Local = 1,
        Azure = 2
    }
    public enum MultipleProjectionType
    {
        Simple = 1,
        MemoryCache = 2
    }
    public static IServiceCollection AddSekibanCore(
        this IServiceCollection services,
        ISekibanDateProducer? sekibanDateProducer = null,
        MultipleProjectionType multipleProjectionType = MultipleProjectionType.MemoryCache)
    {
        services.AddMemoryCache();

        services.AddTransient<AggregateEventPublisher>();

        services.AddTransient<IAggregateCommandExecutor, AggregateCommandExecutor>();
        services.AddTransient<ISingleAggregateService, SingleAggregateService>();
        services.AddTransient<IMultipleAggregateProjectionService, MultipleAggregateProjectionService>();
        switch (multipleProjectionType)
        {
            case MultipleProjectionType.Simple:
                services.AddTransient<IMultipleProjection, SimpleMultipleProjection>();
                break;
            case MultipleProjectionType.MemoryCache:
                services.AddTransient<IMultipleProjection, MemoryCacheMultipleProjection>();
                break;
        }
        var sekibanDateProducer1 = sekibanDateProducer ?? new SekibanDateProducer();
        services.AddSingleton(sekibanDateProducer1);
        services.AddSingleton<IUpdateNotice>(new SekibanUpdateNoticeManager(sekibanDateProducer1));
        SekibanDateProducer.Register(sekibanDateProducer1);
        services.AddTransient<ISingleProjection, MemoryCacheSingleProjection>();
        services.AddTransient<ISingleAggregateFromInitial, SimpleSingleAggregateFromInitial>();
        services.AddSingleton(new InMemoryDocumentStore());
        services.AddTransient<IDocumentTemporaryWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentTemporaryRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentWriter, DocumentWriterSplitter>();
        services.AddTransient<IDocumentRepository, DocumentRepositorySplitter>();
        services.AddSingleton(new HybridStoreManager(true));
        services.AddScoped<ISekibanContext, SekibanContext>();
        services.AddTransient<IQueryFilterService, QueryFilterService>();
        services.AddTransient<QueryFilterHandler>();
        services.AddTransient<ISingleAggregateProjectionCache, SingleAggregateProjectionCache>();
        services.AddTransient<IMultipleAggregateProjectionCache, MultipleAggregateProjectionCache>();
        services.AddTransient<ISnapshotDocumentCache, SnapshotDocumentCache>();
        return services;
    }
    public static IServiceCollection AddSekibanCoreInMemory(this IServiceCollection services, ISekibanDateProducer? sekibanDateProducer = null)
    {
        services.AddMemoryCache();

        services.AddTransient<AggregateEventPublisher>();

        services.AddTransient<IAggregateCommandExecutor, AggregateCommandExecutor>();
        services.AddTransient<ISingleAggregateService, SingleAggregateService>();
        services.AddTransient<IMultipleAggregateProjectionService, MultipleAggregateProjectionService>();
        services.AddTransient<IMultipleProjection, MemoryCacheMultipleProjection>();
        services.AddTransient<ISingleProjection, SimpleProjectionWithSnapshot>();
        var sekibanDateProducer1 = sekibanDateProducer ?? new SekibanDateProducer();
        services.AddSingleton(sekibanDateProducer1);
        SekibanDateProducer.Register(sekibanDateProducer1);
        services.AddSingleton<IUpdateNotice>(new SekibanUpdateNoticeManager(sekibanDateProducer1));
        services.AddTransient<ISingleAggregateFromInitial, SimpleSingleAggregateFromInitial>();
        services.AddSingleton(new InMemoryDocumentStore());
        services.AddTransient<IDocumentWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentRepository, InMemoryDocumentRepository>();
        services.AddSingleton(new HybridStoreManager(true));
        services.AddScoped<ISekibanContext, SekibanContext>();

        services.AddTransient<IDocumentTemporaryRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentPersistentRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentTemporaryWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentPersistentWriter, InMemoryDocumentWriter>();
        services.AddTransient<IQueryFilterService, QueryFilterService>();
        services.AddTransient<QueryFilterHandler>();
        services.AddTransient<ISingleAggregateProjectionCache, SingleAggregateProjectionCache>();
        services.AddTransient<IMultipleAggregateProjectionCache, MultipleAggregateProjectionCache>();
        services.AddTransient<ISnapshotDocumentCache, SnapshotDocumentCache>();
        return services;
    }
    public static IServiceCollection AddSekibanCoreAggregateTest(this IServiceCollection services, ISekibanDateProducer? sekibanDateProducer = null)
    {
        services.AddMemoryCache();


        services.AddTransient<AggregateEventPublisher>();

        services.AddTransient<IAggregateCommandExecutor, AggregateCommandExecutor>();
        services.AddTransient<ISingleAggregateService, SingleAggregateService>();
        services.AddTransient<IMultipleAggregateProjectionService, MultipleAggregateProjectionService>();
        services.AddTransient<IMultipleProjection, MemoryCacheMultipleProjection>();
        services.AddTransient<ISingleProjection, SimpleProjectionWithSnapshot>();
        var sekibanDateProducer1 = sekibanDateProducer ?? new SekibanDateProducer();
        services.AddSingleton(sekibanDateProducer1);
        SekibanDateProducer.Register(sekibanDateProducer1);
        services.AddSingleton<IUpdateNotice>(new SekibanUpdateNoticeManager(sekibanDateProducer1));
        services.AddTransient<ISingleAggregateFromInitial, SimpleSingleAggregateFromInitial>();
        services.AddSingleton(new InMemoryDocumentStore());
        services.AddTransient<IDocumentWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentRepository, InMemoryDocumentRepository>();
        services.AddSingleton(new HybridStoreManager(true));
        services.AddScoped<ISekibanContext, SekibanContext>();

        services.AddTransient<IDocumentTemporaryRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentPersistentRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentTemporaryWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentPersistentWriter, InMemoryDocumentWriter>();
        services.AddTransient<IQueryFilterService, QueryFilterService>();
        services.AddTransient<QueryFilterHandler>();
        services.AddTransient<ISingleAggregateProjectionCache, SingleAggregateProjectionCache>();
        services.AddTransient<IMultipleAggregateProjectionCache, MultipleAggregateProjectionCache>();
        services.AddTransient<ISnapshotDocumentCache, SnapshotDocumentCache>();
        return services;
    }

    public static IServiceCollection AddSekibanHTTPUser(this IServiceCollection services, HttpContextType contextType = HttpContextType.Local)
    {
        // ユーザー情報
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        switch (contextType)
        {
            case HttpContextType.Local:
                services.AddTransient<IUserInformationFactory, HttpContextUserInformationFactory>();
                break;
            case HttpContextType.Azure:
                services.AddTransient<IUserInformationFactory, AzureAdUserInformationFactory>();
                break;
        }
        return services;
    }
    public static IServiceCollection AddSekibanSettingsFromAppSettings(this IServiceCollection services)
    {
        // 設定はConfigurationから指定することもできる、設定オブジェクトをnewで生成することも可能
        services.AddTransient<IAggregateSettings, ConfigurationAggregateSettings>();
        return services;
    }
    public static IServiceCollection AddSekibanAppSettingsFromObject(this IServiceCollection services, AggregateSettings settings)
    {
        // 例
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
        services.AddSingleton<IAggregateSettings>(settings);
        return services;
    }
}
