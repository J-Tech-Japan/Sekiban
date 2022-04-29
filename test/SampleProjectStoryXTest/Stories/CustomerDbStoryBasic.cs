using CosmosInfrastructure;
using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Projections;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Consts;
using CustomerDomainContext.Shared.Exceptions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Shared.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
namespace SampleProjectStoryXTest.Stories;

public class CustomerDbStoryBasic : TestBase
{
    private readonly AggregateCommandExecutor _aggregateCommandExecutor;
    private readonly SingleAggregateService _aggregateService;
    private readonly CosmosDbFactory _cosmosDbFactory;
    public CustomerDbStoryBasic(TestFixture testFixture) : base(testFixture)
    {
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        _aggregateCommandExecutor = GetService<AggregateCommandExecutor>();
        _aggregateService = GetService<SingleAggregateService>();
    }
    [Fact(DisplayName = "CosmosDb ストーリーテスト 集約の機能のテストではなく、CosmosDbと連携して正しく動くかをテストしています。")]
    public async Task CosmosDbStory()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.CosmosActionAsync<IEnumerable<AggregateEvent>?>(
            DocumentType.AggregateEvent,
            async container =>
            {
                var query = container.GetItemLinqQueryable<Document>()
                    .Where(
                        b => true);
                var feedIterator = container.GetItemQueryIterator<dynamic>(
                    query.ToQueryDefinition(),
                    null,
                    null);
                var todelete = new List<Document>(); 
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        if (item == null) {continue;}
                        if (item is not JObject jobj) { continue; }
                        todelete.Add(jobj.ToObject<Document>() ?? throw new Exception());
                    }
                }
                foreach (var d in todelete)
                {
                    await container.DeleteItemAsync<Document>(d.Id.ToString(), new PartitionKey(d.PartitionKey));
                }
                return null;
            });


        // create list branch
        var branchList = (await _aggregateService.DtoListAsync<Branch, BranchDto>()).ToList();
        Assert.Empty(branchList);
        var branchResult = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(new CreateBranch("Japan"));
        Assert.NotNull(branchResult);
        Assert.NotNull(branchResult.AggregateDto);
        var branchId = branchResult.AggregateDto!.AggregateId;
        branchList = (await _aggregateService.DtoListAsync<Branch, BranchDto>()).ToList();
        Assert.Single(branchList);
        var branchFromList =
            branchList.First(m => m.AggregateId == branchId);
        Assert.NotNull(branchFromList);

        // loyalty point should be []  
        var loyaltyPointList =  (await _aggregateService.DtoListAsync<LoyaltyPoint, LoyaltyPointDto>()).ToList();
        Assert.Empty(loyaltyPointList);

        var clientNameList =  (await _aggregateService.DtoListAsync<ClientNameHistoryProjection>()).ToList();
        Assert.Empty(clientNameList);

        // create client
        var clientList =  (await _aggregateService.DtoListAsync<Client, ClientDto>()).ToList();
        Assert.Empty(clientList);
        var originalName = "Tanaka Taro";
        var createClientResult =
            await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, ClientDto, CreateClient>(
                new CreateClient(branchId, originalName, "tanaka@example.com"));
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateDto);
        var clientId = createClientResult.AggregateDto!.AggregateId;
        clientList =  (await _aggregateService.DtoListAsync<Client, ClientDto>()).ToList();
        Assert.Single(clientList);

        // singleAggregateProjection
        clientNameList =  (await _aggregateService.DtoListAsync<ClientNameHistoryProjection>()).ToList();
        Assert.Single(clientNameList);
        var tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Single(tanakaProjection.ClientNames); 
        Assert.Equal(originalName, tanakaProjection.ClientNames.First().Name);
        var secondName = "田中 太郎";
        
        // should throw version error 
        await Assert.ThrowsAsync<JJAggregateCommandInconsistentVersionException>(async () =>
            {
                await _aggregateCommandExecutor
                    .ExecChangeCommandAsync<Client, ClientDto, ChangeClientName>(
                        new ChangeClientName(clientId, secondName));
            }
        );
        // change name
        var changeNameResult =
            await _aggregateCommandExecutor
                .ExecChangeCommandAsync<Client, ClientDto, ChangeClientName>(
                    new ChangeClientName(clientId, secondName) { ReferenceVersion = createClientResult.AggregateDto!.Version});
        // change name projection
        clientNameList =  (await _aggregateService.DtoListAsync<ClientNameHistoryProjection>()).ToList();
        Assert.Single(clientNameList);
        tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Equal(2, tanakaProjection.ClientNames.Count); 
        Assert.Equal(originalName, tanakaProjection.ClientNames.First().Name);
        Assert.Equal(secondName, tanakaProjection.ClientNames[1].Name);

        // loyalty point should be created with event subscribe
        loyaltyPointList =  (await _aggregateService.DtoListAsync<LoyaltyPoint, LoyaltyPointDto>()).ToList();
        Assert.Single(clientList);
        // first point = 0
        var loyaltyPoint =
            await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointDto>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint!.CurrentPoint);

        var addPointResult =
            await _aggregateCommandExecutor
                .ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, AddLoyaltyPoint>(
                    new AddLoyaltyPoint(
                        clientId,
                        DateTime.Now,
                        LoyaltyPointReceiveTypeKeys.FlightDomestic,
                        1000,
                        "") { ReferenceVersion = loyaltyPoint.Version});
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateDto);
        
        loyaltyPoint =
            await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointDto>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint!.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<JJLoyaltyPointNotEnoughException>(async () =>
            {
                await _aggregateCommandExecutor
                    .ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, UseLoyaltyPoint>(
                        new UseLoyaltyPoint(clientId, DateTime.Now, LoyaltyPointUsageTypeKeys.FlightUpgrade, 2000, "") { ReferenceVersion = addPointResult!.AggregateDto!.Version });
            }
        );
        var usePointResult = await _aggregateCommandExecutor
            .ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, UseLoyaltyPoint>(
                new UseLoyaltyPoint(clientId, DateTime.Now, LoyaltyPointUsageTypeKeys.FlightUpgrade, 200, "") { ReferenceVersion = addPointResult!.AggregateDto!.Version });
        Assert.NotNull(usePointResult);
        Assert.NotNull(usePointResult.AggregateDto);

        loyaltyPoint =
            await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointDto>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint!.CurrentPoint);
        // delete client
        var deleteClientResult = await _aggregateCommandExecutor
            .ExecChangeCommandAsync<Client, ClientDto, DeleteClient>(
                new DeleteClient(clientId) { ReferenceVersion = changeNameResult!.AggregateDto!.Version });
        Assert.NotNull(deleteClientResult);
        Assert.NotNull(deleteClientResult.AggregateDto);
        // client deleted
        clientList =  (await _aggregateService.DtoListAsync<Client, ClientDto>(QueryListType.ActiveOnly)).ToList();
        Assert.Empty(clientList);
        // can find deleted client
        clientList =  (await _aggregateService.DtoListAsync<Client, ClientDto>(QueryListType.DeletedOnly)).ToList();
        Assert.Single(clientList);
        clientList =  (await _aggregateService.DtoListAsync<Client, ClientDto>(QueryListType.ActiveAndDeleted)).ToList();
        Assert.Single(clientList);

        // loyalty point should be created with event subscribe
        loyaltyPointList =  (await _aggregateService.DtoListAsync<LoyaltyPoint, LoyaltyPointDto>()).ToList();
        Assert.Empty(loyaltyPointList);
        loyaltyPointList =  (await _aggregateService.DtoListAsync<LoyaltyPoint, LoyaltyPointDto>(QueryListType.DeletedOnly)).ToList();
        Assert.Single(loyaltyPointList);

    }
}
