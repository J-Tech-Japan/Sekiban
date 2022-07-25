using CosmosInfrastructure;
using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Settings;
using Sekiban.EventSourcing.TestHelpers;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories;

public class MultipleDbStoryTest : TestBase
{
    private const string SecondaryDb = "Secondary";
    private const string DefaultDb = "Default";
    private readonly ISekibanContext _sekibanContext;

    public MultipleDbStoryTest(TestFixture testFixture, ITestOutputHelper testOutputHelper) : base(testFixture) =>
        _sekibanContext = GetService<ISekibanContext>();

    [Fact(DisplayName = "CosmosDb ストーリーテスト 複数データベースでの動作を検証する")]
    public async Task CosmosDbStory()
    {
        var cosmosDbFactory = GetService<CosmosDbFactory>();
        var aggregateCommandExecutor = GetService<IAggregateCommandExecutor>();
        var multipleAggregateProjectionService = GetService<IMultipleAggregateProjectionService>();

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
                await cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Default);
                await cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand);
            });
        var branchId = Guid.NewGuid();
        // Default を明示的に指定して、１件データを作成
        await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () =>
            {
                await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(
                    branchId,
                    new CreateBranch(branchId, "TEST"));
            });

        // Default で Listを取得すると1件取得
        var list = await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () => await multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>());
        Assert.Single(list);

        // 何もつけない場合も Default のDbから取得
        list = await multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();
        Assert.Single(list);

        // Secondary で Listを取得すると0件取得
        list = await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () => await multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>());
        Assert.Empty(list);

        var branchId1 = Guid.NewGuid();
        var branchId2 = Guid.NewGuid();
        var branchId3 = Guid.NewGuid();
        // Secondaryで3件データを作成
        await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () =>
            {
                await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(
                    branchId1,
                    new CreateBranch(branchId1, "JAPAN"));
                await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(
                    branchId2,
                    new CreateBranch(branchId2, "USA"));
                await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(
                    branchId3,
                    new CreateBranch(branchId3, "MEXICO"));
            });

        // Default で Listを取得すると1件取得
        list = await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () => await multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>());
        Assert.Single(list);

        // 何もつけない場合も Default のDbから取得
        list = await multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();
        Assert.Single(list);

        // Secondary で Listを取得すると3件取得
        list = await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () => await multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>());
        Assert.Equal(3, list.Count);
    }
}
