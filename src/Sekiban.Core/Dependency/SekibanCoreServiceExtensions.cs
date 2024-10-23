using MemStat.Net;
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
using Sekiban.Core.Usecase;
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
                var snapshotTakingBackgroundService
                    = serviceProvider.GetRequiredService<SnapshotTakingBackgroundService>();
                snapshotTakingBackgroundService.ServiceProvider = serviceProvider;
                return snapshotTakingBackgroundService;
            });
        services.AddTransient<SnapshotGenerator>();
        services.AddTransient<ISingleProjectionSnapshotAccessor, SingleProjectionSnapshotAccessor>();
        services.AddSingleton<ICommandExecuteAwaiter, CommandExecuteAwaiter>();
        services.AddTransient<MultiProjectionCollectionGenerator>();
        services.AddScoped<EventNonBlockingStatus>();

        services.AddTransient<ISekibanUsecaseExecutor, SekibanUsecaseExecutor>();
        services.AddTransient<ISekibanUsecaseContext, SekibanUsecaseContext>();
        services.AddResourceMonitoring();
        return services;
    }

    public static IServiceCollection AddSekibanCoreInMemory(
        this IServiceCollection services,
        ISekibanDateProducer? sekibanDateProducer = null)
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
                var snapshotTakingBackgroundService
                    = serviceProvider.GetRequiredService<SnapshotTakingBackgroundService>();
                snapshotTakingBackgroundService.ServiceProvider = serviceProvider;
                return snapshotTakingBackgroundService;
            });
        services.AddTransient<SnapshotGenerator>();
        services.AddTransient<ISingleProjectionSnapshotAccessor, SingleProjectionSnapshotAccessor>();
        services.AddSingleton<ICommandExecuteAwaiter, CommandExecuteAwaiter>();
        services.AddTransient<MultiProjectionCollectionGenerator>();
        services.AddScoped<EventNonBlockingStatus>();
        services.AddTransient<ISekibanUsecaseExecutor, SekibanUsecaseExecutor>();
        services.AddTransient<ISekibanUsecaseContext, SekibanUsecaseContext>();
        services.AddTransient<IMemoryUsageFinder, MemoryUsageFinder>();
        return services;
    }

    public static IServiceCollection AddSekibanCoreAggregateTest(
        this IServiceCollection services,
        ISekibanDateProducer? sekibanDateProducer = null)
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
                var snapshotTakingBackgroundService
                    = serviceProvider.GetRequiredService<SnapshotTakingBackgroundService>();
                snapshotTakingBackgroundService.ServiceProvider = serviceProvider;
                return snapshotTakingBackgroundService;
            });
        services.AddTransient<SnapshotGenerator>();
        services.AddTransient<ISingleProjectionSnapshotAccessor, SingleProjectionSnapshotAccessor>();
        services.AddSingleton<ICommandExecuteAwaiter, CommandExecuteAwaiter>();
        services.AddTransient<MultiProjectionCollectionGenerator>();
        services.AddScoped<EventNonBlockingStatus>();
        services.AddTransient<ISekibanUsecaseExecutor, SekibanUsecaseExecutor>();
        services.AddTransient<ISekibanUsecaseContext, SekibanUsecaseContext>();
        services.AddTransient<IMemoryUsageFinder, MemoryUsageFinder>();
        return services;
    }

    public static IServiceCollection AddSekibanHTTPUser(
        this IServiceCollection services,
        HttpContextType contextType = HttpContextType.Local)
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

    public static IServiceCollection AddSekibanMultiProjectionSnapshotBackgroundService<TSettings>(
        this IServiceCollection services) where TSettings : IMultiProjectionsSnapshotGenerateSetting
    {
        services.AddHostedService<MultiProjectionSnapshotCollectionBackgroundService<TSettings>>();
        return services;
    }
    public static IServiceCollection AddSekibanAppSettingsFromObject(
        this IServiceCollection services,
        AggregateSettings settings)
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

// public class MemoryUsageFinder : 
// {
//     public double GetTotalMemory() => GetTotalMemoryUsage();
//     public double GetPercentage() => GetMemoryUsagePercentage();
//
//     private static double GetMemoryUsagePercentage()
//     {
//         if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
//         {
//             var info = new ComputerInfo();
//             double totalMemory = info.TotalPhysicalMemory;
//             double availableMemory = info.AvailablePhysicalMemory;
//             return (totalMemory - availableMemory) / totalMemory * 100.0;
//         }
//         if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
//         {
//             return GetUnixMemoryUsagePercentage();
//         }
//         throw new PlatformNotSupportedException("Unsupported OS.");
//     }
//
//     private static double GetUnixMemoryUsagePercentage()
//     {
//         var info = new ProcessStartInfo
//         {
//             FileName = "sh",
//             Arguments = "-c \"free -m | grep Mem\"",
//             RedirectStandardOutput = true,
//             UseShellExecute = false
//         };
//
//         using (var process = Process.Start(info))
//         using (var reader = process.StandardOutput)
//         {
//             var output = reader.ReadLine();
//             if (output != null)
//             {
//                 var parts = output.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
//                 if (parts.Length >= 3 &&
//                     double.TryParse(parts[1], out var totalMemory) &&
//                     double.TryParse(parts[2], out var usedMemory))
//                 {
//                     return usedMemory / totalMemory * 100.0;
//                 }
//             }
//         }
//
//         return -1.0; // メモリ使用量の取得に失敗した場合
//     }
//
//     private static double GetTotalMemoryUsage()
//     {
//         if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
//         {
//             return GetWindowsMemoryUsage();
//         }
//         if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
//         {
//             return GetLinuxMemoryUsage();
//         }
//         if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
//         {
//             return GetMacOSMemoryUsage();
//         }
//         throw new PlatformNotSupportedException("This platform is not supported.");
//     }
//
//     private static double GetWindowsMemoryUsage()
//     {
//         var info = new ComputerInfo();
//         var totalMemory = info.TotalPhysicalMemory;
//         var availableMemory = info.AvailablePhysicalMemory;
//         var usedMemory = (double)(totalMemory - availableMemory) / totalMemory * 100.0;
//         return usedMemory;
//     }
//
//     private static double GetLinuxMemoryUsage()
//     {
//         var info = new ProcessStartInfo
//         {
//             FileName = "sh",
//             Arguments = "-c \"free -b | grep Mem\"",
//             RedirectStandardOutput = true,
//             UseShellExecute = false,
//             CreateNoWindow = true
//         };
//
//         using (var process = Process.Start(info))
//         using (var reader = process.StandardOutput)
//         {
//             var output = reader.ReadLine();
//             if (output != null)
//             {
//                 var parts = output.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
//                 if (parts.Length >= 3 &&
//                     ulong.TryParse(parts[1], out var totalMemory) &&
//                     ulong.TryParse(parts[2], out var usedMemory))
//                 {
//                     return (double)usedMemory / totalMemory * 100.0;
//                 }
//             }
//         }
//
//         return -1.0; // メモリ使用量の取得に失敗した場合
//     }
//
//     private static double GetMacOSMemoryUsage()
//     {
//         var info = new ProcessStartInfo
//         {
//             FileName = "sh",
//             Arguments = "-c \"vm_stat | grep 'Pages free'\"",
//             RedirectStandardOutput = true,
//             UseShellExecute = false,
//             CreateNoWindow = true
//         };
//
//         using (var process = Process.Start(info))
//         using (var reader = process.StandardOutput)
//         {
//             var output = reader.ReadLine();
//             if (output != null)
//             {
//                 var parts = output.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
//                 if (parts.Length >= 3 && ulong.TryParse(parts[2].Replace(".", ""), out var freePages))
//                 {
//                     ulong pageSize = 4096; // 通常のmacOSのページサイズ（4KB）
//                     var freeMemory = freePages * pageSize;
//
//                     info.Arguments = "-c \"sysctl hw.memsize\"";
//                     using (var memProcess = Process.Start(info))
//                     using (var memReader = memProcess.StandardOutput)
//                     {
//                         var memOutput = memReader.ReadLine();
//                         if (memOutput != null)
//                         {
//                             var memParts = memOutput.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
//                             if (memParts.Length >= 2 && ulong.TryParse(memParts[1], out var totalMemory))
//                             {
//                                 var usedMemory = totalMemory - freeMemory;
//                                 return (double)usedMemory / totalMemory * 100.0;
//                             }
//                         }
//                     }
//                 }
//             }
//         }
//
//         return -1.0; // メモリ使用量の取得に失敗した場合
//     }
// }
