using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.PubSubs;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Settings;
namespace Sekiban.EventSourcing;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSekibanCore(this IServiceCollection services)
    {
        services.AddMemoryCache();

        services.AddTransient<AggregateEventPublisher>();

        services.AddTransient<IAggregateCommandExecutor, AggregateCommandExecutor>();
        services.AddTransient<SingleAggregateService>();
        services.AddTransient<MultipleAggregateProjectionService>();

        services.AddSingleton(new InMemoryDocumentStore());
        services.AddTransient<IDocumentTemporaryWriter, InMemoryDocumentWriter>();
        services.AddTransient<IDocumentTemporaryRepository, InMemoryDocumentRepository>();
        services.AddTransient<IDocumentWriter, DocumentWriterSplitter>();
        services.AddSingleton<IDocumentRepository, DocumentRepositorySplitter>();
        services.AddSingleton(new HybridStoreManager(true));

        return services;
    }

    public static IServiceCollection AddSekibanHTTPUser(this IServiceCollection services)
    {
        // ユーザー情報
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTransient<IUserInformationFactory, HttpContextUserInformationFactory>();
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