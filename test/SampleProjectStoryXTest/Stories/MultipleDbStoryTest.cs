using CosmosInfrastructure;
using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Settings;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories;

public class MultipleDbStoryTest : TestBase
{
    private const string SecondaryDb = "Secondary";
    private const string DefaultDb = "Default";
    private readonly ISekibanContext _sekibanContext;

    public MultipleDbStoryTest(TestFixture testFixture, ITestOutputHelper testOutputHelper) : base(testFixture)
    {
        _sekibanContext = GetService<ISekibanContext>();
        // create recent activity
        var aggregateCommandExecutor = GetService<IAggregateCommandExecutor>();
        aggregateCommandExecutor
            .ExecCreateCommandAsync<SnapshotManager, SnapshotManagerDto, CreateSnapshotManager>(new CreateSnapshotManager(SnapshotManager.SharedId))
            .Wait();
    }

    [Fact(DisplayName = "CosmosDb ストーリーテスト 複数データベースでの動作を検証する")]
    public async Task CosmosDbStory()
    {
        // 注意点 DIでオブジェクトを生成した時点でDBの接続が決まるため、リポジトリ、Writerを含むオブジェクトを複数DBで共用しない
        // クラスが違う場合は、Transientを使用するため、共用しないが、同じスコープ内で複数DBを使用する場合は、
        // クラスが同じで、複数のDBを使用する場合は、
        // _serviceProvider.GetService をすることによって、明示的にRepository, Writerのオブジェクトを再生成する必要がある

        using var scope = _serviceProvider.CreateScope();

        var cosmosDbFactory = GetService<CosmosDbFactory>();
        // 何もしないで実行したら "Default"の動作となる
        // 先に全データを削除する
        await cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Default);
        await cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Dissolvable);
        await cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand, AggregateContainerGroup.Dissolvable);
        await cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand);

        // Secondary の設定ないで実行する
        await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () =>
            {
                // cosmosDbFactory とオブジェクトを共用しない
                var cosmosDbFactoryFunc = GetService<CosmosDbFactory>();
                await cosmosDbFactoryFunc.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Default);
                await cosmosDbFactoryFunc.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand);
            });

        // Default を明示的に指定して、１件データを作成
        await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () =>
            {
                // オブジェクトを共用しないため、ここでDIする
                var aggregateCommandExecutor = GetService<IAggregateCommandExecutor>();
                await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(new CreateBranch("TEST"));
            });

        // Default で Listを取得すると1件取得
        var list = await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () =>
            {
                // オブジェクトを共用しないため、ここでDIする
                var multipleAggregateProjectionServiceFunc = GetService<MultipleAggregateProjectionService>();
                return await multipleAggregateProjectionServiceFunc.GetAggregateList<Branch, BranchDto>();
            });
        Assert.Single(list);

        // SekibanContext を使わないと Default で Listを取得すると1件取得
        var multipleAggregateProjectionService = GetService<MultipleAggregateProjectionService>();
        list = await multipleAggregateProjectionService.GetAggregateList<Branch, BranchDto>();
        Assert.Single(list);

        // Secondary で Listを取得すると0件取得
        list = await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () =>
            {
                // オブジェクトを共用しないため、ここでDIする
                var multipleAggregateProjectionServiceFunc = GetService<MultipleAggregateProjectionService>();
                return await multipleAggregateProjectionServiceFunc.GetAggregateList<Branch, BranchDto>();
            });
        Assert.Empty(list);

        // Secondaryで3件データを作成
        await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () =>
            {
                // オブジェクトを共用しないため、ここでDIする
                var aggregateCommandExecutor = GetService<IAggregateCommandExecutor>();
                await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(new CreateBranch("JAPAN"));
                await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(new CreateBranch("USA"));
                await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(new CreateBranch("MEXICO"));
            });

        // Default で Listを取得すると1件取得
        list = await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () =>
            {
                // オブジェクトを共用しないため、ここでDIする
                var multipleAggregateProjectionServiceFunc = GetService<MultipleAggregateProjectionService>();
                return await multipleAggregateProjectionServiceFunc.GetAggregateList<Branch, BranchDto>();
            });
        Assert.Single(list);

        // Secondary で Listを取得すると3件取得
        list = await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () =>
            {
                // オブジェクトを共用しないため、ここでDIする
                var multipleAggregateProjectionServiceFunc = GetService<MultipleAggregateProjectionService>();
                return await multipleAggregateProjectionServiceFunc.GetAggregateList<Branch, BranchDto>();
            });
        Assert.Equal(3, list.Count);
    }
}
