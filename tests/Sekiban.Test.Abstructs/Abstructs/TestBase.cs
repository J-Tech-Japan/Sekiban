using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Core;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Documents;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Snapshot.BackgroundServices;
using Sekiban.Testing.Story;
using System.Reflection;
using Xunit.Abstractions;
namespace Sekiban.Test.Abstructs.Abstructs;

[Collection("Sequential")]
public class TestBase<TDependency> : IClassFixture<TestBase<TDependency>.SekibanTestFixture>, IDisposable
    where TDependency : IDependencyDefinition, new()
{
    protected readonly IAggregateLoader aggregateLoader;
    protected readonly ICommandExecutor commandExecutor;
    protected readonly IConfiguration configuration;
    protected readonly IDocumentPersistentRepository documentPersistentRepository;
    protected readonly IDocumentPersistentWriter documentPersistentWriter;
    protected readonly IDocumentRemover documentRemover;

    protected readonly InMemoryDocumentStore inMemoryDocumentStore;
    protected readonly IMemoryCacheAccessor memoryCache;
    protected readonly IMultiProjectionService multiProjectionService;
    protected readonly IQueryExecutor queryExecutor;
    protected readonly ISekibanExecutor sekibanExecutor;
    protected readonly SekibanTestFixture sekibanTestFixture;
    protected readonly IServiceProvider serviceProvider;

    protected ITestOutputHelper TestOutputHelper => sekibanTestFixture.TestOutputHelper!;

    public TestBase(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper output,
        ISekibanServiceProviderGenerator providerGenerator)
    {
        sekibanTestFixture.TestOutputHelper = output;
        this.sekibanTestFixture = sekibanTestFixture;
        serviceProvider = providerGenerator.Generate(
            sekibanTestFixture,
            new TDependency(),
            collection => collection.AddLogging(builder => builder.AddXUnit(sekibanTestFixture.TestOutputHelper)));
        var backgroundService = serviceProvider.GetRequiredService<SnapshotTakingBackgroundService>();
        backgroundService.ServiceProvider = serviceProvider;
        Task.Run(() => backgroundService.StartAsync(CancellationToken.None));
        documentRemover = GetService<IDocumentRemover>();
        commandExecutor = GetService<ICommandExecutor>();
        aggregateLoader = GetService<IAggregateLoader>();
        sekibanExecutor = GetService<ISekibanExecutor>();
        inMemoryDocumentStore = GetService<InMemoryDocumentStore>();
        memoryCache = GetService<IMemoryCacheAccessor>();
        documentPersistentWriter = GetService<IDocumentPersistentWriter>();
        documentPersistentRepository = GetService<IDocumentPersistentRepository>();
        multiProjectionService = GetService<IMultiProjectionService>();
        queryExecutor = GetService<IQueryExecutor>();
        configuration = GetService<IConfiguration>();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
        }
    }

    public T GetService<T>()
    {
        var toReturn = serviceProvider.GetService<T>() ??
            throw new SekibanTypeNotFoundException("The object has not been registered." + typeof(T));
        return toReturn;
    }

    protected void RemoveAllFromDefault()
    {
        ResetInMemoryDocumentStoreAndCache();
        documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Default).Wait();
        documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Default).Wait();
    }

    protected void RemoveAllFromDefaultAndDissolvable()
    {
        ResetInMemoryDocumentStoreAndCache();
        documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Default).Wait();
        documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Default).Wait();
        documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Dissolvable).Wait();
        documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Dissolvable).Wait();
    }
    protected ResultBox<UnitValue> RemoveAllFromDefaultAndDissolvableWithResultBox() =>
        ResultBox<UnitValue>.WrapTry(RemoveAllFromDefaultAndDissolvable);

    protected void ResetInMemoryDocumentStoreAndCache()
    {
        // Remove in memory data
        inMemoryDocumentStore.ResetInMemoryStore();
        (memoryCache.Cache as MemoryCache)?.Compact(1);
    }

    protected ResultBox<UnitValue> ResetInMemoryDocumentStoreAndCacheWithResultBox() =>
        ResultBox<UnitValue>.WrapTry(ResetInMemoryDocumentStoreAndCache);

    public class SekibanTestFixture : ISekibanTestFixture
    {
        public SekibanTestFixture()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(ApplicationEnvironment.ApplicationBasePath)
                .AddJsonFile("appsettings.json", false, false)
                .AddEnvironmentVariables()
                .AddUserSecrets(Assembly.GetExecutingAssembly());
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }
        public ITestOutputHelper? TestOutputHelper { get; set; }
    }
}
