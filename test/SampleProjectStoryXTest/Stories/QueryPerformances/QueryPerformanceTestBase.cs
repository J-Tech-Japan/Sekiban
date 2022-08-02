using CosmosInfrastructure;
using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.TestHelpers;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ServiceCollectionExtensions = Sekiban.EventSourcing.ServiceCollectionExtensions;
namespace SampleProjectStoryXTest.Stories.QueryPerformances;

public abstract class QueryPerformanceTestBase : TestBase
{
    protected readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    protected readonly ISingleAggregateService _aggregateService;
    protected readonly CosmosDbFactory _cosmosDbFactory;
    protected readonly IDocumentPersistentRepository _documentPersistentRepository;
    protected readonly HybridStoreManager _hybridStoreManager;
    protected readonly InMemoryDocumentStore _inMemoryDocumentStore;
    protected readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    protected readonly ITestOutputHelper _testOutputHelper;
    protected QueryPerformanceTestBase(
        TestFixture testFixture,
        ITestOutputHelper testOutputHelper,
        ServiceCollectionExtensions.MultipleProjectionType multipleProjectionType) : base(testFixture, false, multipleProjectionType)
    {
        _testOutputHelper = testOutputHelper;
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        _aggregateCommandExecutor = GetService<IAggregateCommandExecutor>();
        _aggregateService = GetService<ISingleAggregateService>();
        _documentPersistentRepository = GetService<IDocumentPersistentRepository>();
        _inMemoryDocumentStore = GetService<InMemoryDocumentStore>();
        _hybridStoreManager = GetService<HybridStoreManager>();
        _multipleAggregateProjectionService = GetService<IMultipleAggregateProjectionService>();
    }
    [Fact]
    public void TestQuery1()
    {
        // 先に全データを削除する
        _cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Default).Wait();
        _cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Dissolvable).Wait();
        _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand, AggregateContainerGroup.Dissolvable).Wait();
        _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand).Wait();
    }

    [Theory]
    [InlineData(3, 3, 3, 1)]
    [InlineData(3, 3, 3, 2)]
    [InlineData(3, 3, 3, 3)]
    [InlineData(3, 3, 3, 4)]
    [InlineData(3, 3, 3, 5)]
    [InlineData(3, 3, 3, 6)]
    [InlineData(3, 3, 3, 7)]
    [InlineData(3, 3, 3, 8)]
    [InlineData(3, 3, 3, 9)]
    [InlineData(3, 3, 3, 10)]
    [InlineData(1, 1, 100, 11)]
    [InlineData(1, 1, 100, 12)]
    [InlineData(1, 1, 100, 13)]
    [InlineData(1, 1, 100, 14)]
    [InlineData(1, 1, 100, 15)]
    [InlineData(1, 1, 100, 16)]
    [InlineData(1, 1, 100, 17)]
    [InlineData(1, 1, 100, 18)]
    [InlineData(1, 1, 100, 19)]
    public async Task TestQuery2(int numberOfBranch, int numberOfClient, int changeNameCount, int id)
    {
        for (var i = 0; i < numberOfBranch; i++)
        {
            // create list branch
            var branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();
            _testOutputHelper.WriteLine($"create branch {branchList.Count}");

            var firstcount = branchList.Count;
            var branchResult
                = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(new CreateBranch($"Branch {i}"));
            var branchId = branchResult!.Command.AggregateId;
            Assert.NotNull(branchResult);
            Assert.NotNull(branchResult.AggregateDto);
            branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();
            _testOutputHelper.WriteLine($"branch created {branchList.Count}");
            Assert.Equal(firstcount + 1, branchList.Count);
            var branchFromList = branchList.First(m => m.AggregateId == branchId);
            Assert.NotNull(branchFromList);
            for (var j = 0; j < numberOfClient; j++)
            {
                var clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>();
                _testOutputHelper.WriteLine($"create client {clientList.Count}");
                var firstClientCount = clientList.Count;
                var clientCreateResult = await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, ClientContents, CreateClient>(
                    new CreateClient(branchId, $"clientname {i}-{j}", $"test{i}.{j}.{id}@example.com"));
                clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>();
                _testOutputHelper.WriteLine($"client created {clientList.Count}");
                Assert.Equal(firstClientCount + 1, clientList.Count);
                for (var k = 0; k < changeNameCount; k++)
                {
                    _testOutputHelper.WriteLine($"client change name {k + 1}");
                    var aggregate = await _aggregateService.GetAggregateDtoAsync<Client, ClientContents>(clientCreateResult.Command.AggregateId);
                    await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, ChangeClientName>(
                        new ChangeClientName(clientCreateResult.Command.AggregateId, $"change{i}-{j}-{k}")
                        {
                            ReferenceVersion = aggregate?.Version ?? 0
                        });
                    clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>();
                    _testOutputHelper.WriteLine($"client name changed {k + 1} - {clientList.Count}");
                    Assert.Equal(firstClientCount + 1, clientList.Count);
                }
            }
        }
    }
}
