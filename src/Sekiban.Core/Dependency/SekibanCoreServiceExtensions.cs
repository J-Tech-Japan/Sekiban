using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Cache;
using Sekiban.Core.Command;
using Sekiban.Core.Command.UserInformation;
using Sekiban.Core.Documents;
using Sekiban.Core.PubSub;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot.BackgroundServices;
using ISingleProjection = Sekiban.Core.Query.SingleProjections.Projections.ISingleProjection;
namespace Sekiban.Core.Dependency;

/// <summary>
///     Extension methods for <see cref="IServiceCollection" />
/// </summary>
public static class SekibanCoreServiceExtensions
{
    public enum HttpContextType
    {
        Local = 1, Azure = 2
    }

    public enum MultiProjectionType
    {
        Simple = 1, MemoryCache = 2
    }

    public static IServiceCollection AddSekibanCore(
        this IServiceCollection services,
        SekibanSettings settings,
        ISekibanDateProducer? sekibanDateProducer = null,
        MultiProjectionType multiProjectionType = MultiProjectionType.MemoryCache)
    {
        services.AddMemoryCache();
        services.AddLogging();
        services.AddTransient<IMemoryCacheAccessor, MemoryCacheAccessor>();
        services.AddTransient<EventPublisher>();

        services.AddTransient<ICommandExecutor, CommandExecutor>();
        services.AddTransient<IAggregateLoader, AggregateLoader>();
        services.AddTransient<IMultiProjectionService, MultiProjectionService>();
        switch (multiProjectionType)
        {
            case MultiProjectionType.Simple:
                services.AddTransient<IMultiProjection, SimpleMultiProjection>();
                break;
            case MultiProjectionType.MemoryCache:
                services.AddTransient<IMultiProjection, MemoryCacheMultiProjection>();
                break;
        }

        var sekibanDateProducer1 = sekibanDateProducer ?? new SekibanDateProducer();
        services.AddSingleton(sekibanDateProducer1);
        services.AddSingleton<IUpdateNotice>(new SekibanUpdateNoticeManager(sekibanDateProducer1));
        SekibanDateProducer.Register(sekibanDateProducer1);
        services.AddTransient<ISingleProjection, MemoryCacheSingleProjection>();
        services.AddTransient<ISingleProjectionFromInitial, SimpleSingleProjectionFromInitial>();
        services.AddSingleton(new InMemoryDocumentStore());
        services.AddTransient<IDocumentTemporaryWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentTemporaryRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentWriter, DocumentWriterSplitter>();
        services.AddTransient<IDocumentRepository, DocumentRepositorySplitter>();
        services.AddSingleton(new HybridStoreManager(true));
        services.AddScoped<ISekibanContext, SekibanContext>();
        services.AddTransient<IQueryExecutor, QueryExecutor>();
        services.AddTransient<ISekibanExecutor, SekibanExecutor>();
        services.AddTransient<QueryHandler>();
        services.AddSingleton(settings.MemoryCache);
        services.AddTransient<ISingleProjectionCache, SingleProjectionCache>();
        services.AddTransient<IMultiProjectionCache, MultiProjectionCache>();
        services.AddTransient<ISnapshotDocumentCache, SnapshotDocumentCache>();
        services.AddTransient<IMultiProjectionSnapshotGenerator, MultiProjectionSnapshotGenerator>();
        services.AddSingleton(new SnapshotTakingBackgroundService());
        services.AddHostedService(
            serviceProvider =>
            {
                var snapshotTakingBackgroundService = serviceProvider.GetRequiredService<SnapshotTakingBackgroundService>();
                snapshotTakingBackgroundService.ServiceProvider = serviceProvider;
                return snapshotTakingBackgroundService;
            });
        services.AddTransient<SnapshotGenerator>();
        services.AddTransient<ISingleProjectionSnapshotAccessor, SingleProjectionSnapshotAccessor>();
        services.AddSingleton<ICommandExecuteAwaiter, CommandExecuteAwaiter>();
        services.AddTransient<MultiProjectionCollectionGenerator>();
        services.AddScoped<EventNonBlockingStatus>();
        return services;
    }

    public static IServiceCollection AddSekibanCoreInMemory(this IServiceCollection services, ISekibanDateProducer? sekibanDateProducer = null)
    {
        services.AddMemoryCache();
        services.AddLogging();
        services.AddTransient<IMemoryCacheAccessor, MemoryCacheAccessor>();

        services.AddTransient<EventPublisher>();

        services.AddTransient<ICommandExecutor, CommandExecutor>();
        services.AddTransient<IAggregateLoader, AggregateLoader>();
        services.AddTransient<IMultiProjectionService, MultiProjectionService>();
        services.AddTransient<IMultiProjection, MemoryCacheMultiProjection>();
        services.AddTransient<ISingleProjection, SimpleProjectionWithSnapshot>();
        var sekibanDateProducer1 = sekibanDateProducer ?? new SekibanDateProducer();
        services.AddSingleton(sekibanDateProducer1);
        SekibanDateProducer.Register(sekibanDateProducer1);
        services.AddSingleton<IUpdateNotice>(new SekibanUpdateNoticeManager(sekibanDateProducer1));
        services.AddTransient<ISingleProjectionFromInitial, SimpleSingleProjectionFromInitial>();
        services.AddSingleton(new InMemoryDocumentStore());
        services.AddTransient<IDocumentWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentRepository, InMemoryDocumentRepository>();
        services.AddSingleton(new HybridStoreManager(true));
        services.AddScoped<ISekibanContext, SekibanContext>();

        services.AddTransient<IDocumentTemporaryRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentPersistentRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentTemporaryWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentPersistentWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentRemover, InMemoryDocumentRemover>();
        services.AddTransient<IQueryExecutor, QueryExecutor>();
        services.AddTransient<ISekibanExecutor, SekibanExecutor>();
        services.AddTransient<QueryHandler>();
        services.AddScoped<MemoryCacheSetting>();
        services.AddTransient<ISingleProjectionCache, SingleProjectionCache>();
        services.AddTransient<IMultiProjectionCache, MultiProjectionCache>();
        services.AddTransient<ISnapshotDocumentCache, SnapshotDocumentCache>();

        services.AddTransient<IMultiProjectionSnapshotGenerator, MultiProjectionSnapshotGenerator>();
        services.AddTransient<IBlobAccessor, NothingBlobAccessor>();
        services.AddSingleton(new SnapshotTakingBackgroundService());
        services.AddHostedService(
            serviceProvider =>
            {
                var snapshotTakingBackgroundService = serviceProvider.GetRequiredService<SnapshotTakingBackgroundService>();
                snapshotTakingBackgroundService.ServiceProvider = serviceProvider;
                return snapshotTakingBackgroundService;
            });
        services.AddTransient<SnapshotGenerator>();
        services.AddTransient<ISingleProjectionSnapshotAccessor, SingleProjectionSnapshotAccessor>();
        services.AddSingleton<ICommandExecuteAwaiter, CommandExecuteAwaiter>();
        services.AddTransient<MultiProjectionCollectionGenerator>();
        services.AddScoped<EventNonBlockingStatus>();
        return services;
    }

    public static IServiceCollection AddSekibanCoreAggregateTest(this IServiceCollection services, ISekibanDateProducer? sekibanDateProducer = null)
    {
        services.AddMemoryCache();
        services.AddLogging();
        services.AddTransient<IMemoryCacheAccessor, MemoryCacheAccessor>();

        services.AddTransient<EventPublisher>();

        services.AddTransient<ICommandExecutor, CommandExecutor>();
        services.AddTransient<IAggregateLoader, AggregateLoader>();
        services.AddTransient<IMultiProjectionService, MultiProjectionService>();
        services.AddTransient<IMultiProjection, MemoryCacheMultiProjection>();
        services.AddTransient<ISingleProjection, SimpleProjectionWithSnapshot>();
        var sekibanDateProducer1 = sekibanDateProducer ?? new SekibanDateProducer();
        services.AddSingleton(sekibanDateProducer1);
        SekibanDateProducer.Register(sekibanDateProducer1);
        services.AddSingleton<IUpdateNotice>(new SekibanUpdateNoticeManager(sekibanDateProducer1));
        services.AddTransient<ISingleProjectionFromInitial, SimpleSingleProjectionFromInitial>();
        services.AddSingleton(new InMemoryDocumentStore());
        services.AddTransient<IDocumentWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentRepository, InMemoryDocumentRepository>();
        services.AddSingleton(new HybridStoreManager(true));
        services.AddScoped<ISekibanContext, SekibanContext>();

        services.AddTransient<IDocumentTemporaryRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentPersistentRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentTemporaryWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentPersistentWriter, InMemoryDocumentWriter>();
        services.AddTransient<IQueryExecutor, QueryExecutor>();
        services.AddTransient<ISekibanExecutor, SekibanExecutor>();
        services.AddTransient<QueryHandler>();
        services.AddScoped<MemoryCacheSetting>();
        services.AddTransient<ISingleProjectionCache, SingleProjectionCache>();
        services.AddTransient<IMultiProjectionCache, MultiProjectionCache>();
        services.AddTransient<ISnapshotDocumentCache, SnapshotDocumentCache>();

        services.AddTransient<IMultiProjectionSnapshotGenerator, MultiProjectionSnapshotGenerator>();
        services.AddTransient<IBlobAccessor, NothingBlobAccessor>();
        services.AddSingleton(new SnapshotTakingBackgroundService());
        services.AddHostedService(
            serviceProvider =>
            {
                var snapshotTakingBackgroundService = serviceProvider.GetRequiredService<SnapshotTakingBackgroundService>();
                snapshotTakingBackgroundService.ServiceProvider = serviceProvider;
                return snapshotTakingBackgroundService;
            });
        services.AddTransient<SnapshotGenerator>();
        services.AddTransient<ISingleProjectionSnapshotAccessor, SingleProjectionSnapshotAccessor>();
        services.AddSingleton<ICommandExecuteAwaiter, CommandExecuteAwaiter>();
        services.AddTransient<MultiProjectionCollectionGenerator>();
        services.AddScoped<EventNonBlockingStatus>();
        return services;
    }

    public static IServiceCollection AddSekibanHTTPUser(this IServiceCollection services, HttpContextType contextType = HttpContextType.Local)
    {
        // Users Information
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

    public static IServiceCollection AddSekibanMultiProjectionSnapshotBackgroundService<TSettings>(this IServiceCollection services)
        where TSettings : IMultiProjectionsSnapshotGenerateSetting
    {
        services.AddHostedService<MultiProjectionSnapshotCollectionBackgroundService<TSettings>>();
        return services;
    }
    public static IServiceCollection AddSekibanAppSettingsFromObject(this IServiceCollection services, AggregateSettings settings)
    {
        // Example
        // services.AddSingleton<IAggregateSettings>(
        //     new AggregateSettings
        //     {
        //         Helper = new AggregateSettingHelper(
        //             true,
        //             true,
        //             80,
        //             15,
        //             new List<AggregateSetting>
        //             {
        //                 new(nameof(Client), true, true),
        //                 new(nameof(ClientNameHistorySingleProjectionPayload), true, false, 111),
        //                 new(nameof(RecentActivity), true, true, 82, 10)
        //             })
        //     });
        services.AddSingleton<IAggregateSettings>(settings);
        return services;
    }
}
