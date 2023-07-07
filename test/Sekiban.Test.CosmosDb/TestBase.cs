using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Snapshot.BackgroundServices;
using Sekiban.Testing.Story;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb;

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

    protected readonly HybridStoreManager hybridStoreManager;
    protected readonly InMemoryDocumentStore inMemoryDocumentStore;
    protected readonly IMemoryCacheAccessor memoryCache;
    protected readonly IMultiProjectionService multiProjectionService;
    protected readonly IQueryExecutor queryExecutor;
    protected readonly SekibanTestFixture sekibanTestFixture;
    protected readonly IServiceProvider serviceProvider;
    protected ITestOutputHelper _testOutputHelper => sekibanTestFixture.TestOutputHelper!;
    public TestBase(SekibanTestFixture sekibanTestFixture, ITestOutputHelper output, ISekibanServiceProviderGenerator providerGenerator)
    {
        sekibanTestFixture.TestOutputHelper = output;
        this.sekibanTestFixture = sekibanTestFixture;
        serviceProvider = providerGenerator.Generate(sekibanTestFixture, new TDependency());
        var backgroundService = serviceProvider.GetRequiredService<SnapshotTakingBackgroundService>();
        backgroundService.ServiceProvider = serviceProvider;
        Task.Run(() => backgroundService.StartAsync(CancellationToken.None));
        documentRemover = GetService<IDocumentRemover>();
        commandExecutor = GetService<ICommandExecutor>();
        aggregateLoader = GetService<IAggregateLoader>();

        hybridStoreManager = GetService<HybridStoreManager>();
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
        var toReturn = serviceProvider.GetService<T>();
        if (toReturn is null)
        {
            throw new Exception("The object has not been registered." + typeof(T));
        }
        return toReturn;
    }

    protected void RemoveAllFromDefault()
    {
        documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Default).Wait();
        documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Default).Wait();
    }
    protected void RemoveAllFromDefaultAndDissolvable()
    {
        documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Default).Wait();
        documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Default).Wait();
        documentRemover.RemoveAllEventsAsync(AggregateContainerGroup.Dissolvable).Wait();
        documentRemover.RemoveAllItemsAsync(AggregateContainerGroup.Dissolvable).Wait();
    }

    protected void ResetInMemoryDocumentStoreAndCache()
    {
        // Remove in memory data
        inMemoryDocumentStore.ResetInMemoryStore();
        hybridStoreManager.ClearHybridPartitions();
        (memoryCache.Cache as MemoryCache)?.Compact(1);
    }

    public class SekibanTestFixture : ISekibanTestFixture
    {
        public SekibanTestFixture()
        {
            var builder = new ConfigurationBuilder().SetBasePath(ApplicationEnvironment.ApplicationBasePath)
                .AddJsonFile("appsettings.json", false, false)
                .AddEnvironmentVariables()
                .AddUserSecrets(Assembly.GetExecutingAssembly());
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }
        public ITestOutputHelper? TestOutputHelper { get; set; }
    }
}
