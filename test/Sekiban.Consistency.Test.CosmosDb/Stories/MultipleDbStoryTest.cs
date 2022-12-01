using System.Threading.Tasks;
using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Document;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Setting;
using Sekiban.Infrastructure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace SampleProjectStoryXTest.Stories;

public class MultipleDbStoryTest : TestBase
{
    private const string SecondaryDb = "Secondary";
    private const string DefaultDb = "Default";
    private readonly ISekibanContext _sekibanContext;

    public MultipleDbStoryTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture)
    {
        _sekibanContext = GetService<ISekibanContext>();
    }

    [Fact(DisplayName = "CosmosDb ストーリーテスト 複数データベースでの動作を検証する")]
    public async Task CosmosDbStory()
    {
        var cosmosDbFactory = GetService<CosmosDbFactory>();
        var commandExecutor = GetService<ICommandExecutor>();
        var multiProjectionService = GetService<IMultiProjectionService>();

        // 何もしないで実行したら "Default"の動作となる
        // 先に全データを削除する
        await cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default);
        await cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Dissolvable);
        await cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command,
            AggregateContainerGroup.Dissolvable);
        await cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command);

        // Secondary の設定ないで実行する
        await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () =>
            {
                await cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default);
                await cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command);
            });
        // Default を明示的に指定して、１件データを作成
        await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () =>
            {
                await commandExecutor.ExecCommandAsync<Branch, CreateBranch>(
                    new CreateBranch("TEST"));
            });

        // Default で Listを取得すると1件取得
        var list = await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () => await multiProjectionService.GetAggregateList<Branch>());
        Assert.Single(list);

        // 何もつけない場合も Default のDbから取得
        list = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Single(list);

        // Secondary で Listを取得すると0件取得
        list = await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () => await multiProjectionService.GetAggregateList<Branch>());
        Assert.Empty(list);

        // Secondaryで3件データを作成
        await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () =>
            {
                await commandExecutor.ExecCommandAsync<Branch, CreateBranch>(
                    new CreateBranch("JAPAN"));
                await commandExecutor.ExecCommandAsync<Branch, CreateBranch>(
                    new CreateBranch("USA"));
                await commandExecutor.ExecCommandAsync<Branch, CreateBranch>(
                    new CreateBranch("MEXICO"));
            });

        // Default で Listを取得すると1件取得
        list = await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () => await multiProjectionService.GetAggregateList<Branch>());
        Assert.Single(list);

        // 何もつけない場合も Default のDbから取得
        list = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Single(list);

        // Secondary で Listを取得すると3件取得
        list = await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () => await multiProjectionService.GetAggregateList<Branch>());
        Assert.Equal(3, list.Count);
    }
}
