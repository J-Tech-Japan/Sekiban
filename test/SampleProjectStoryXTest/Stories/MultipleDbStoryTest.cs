using CosmosInfrastructure;
using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Settings;
using Sekiban.EventSourcing.TestHelpers;
using Sekiban.EventSourcing.TestHelpers.StoryTests;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories
{
    public class MultipleDbStoryTest : TestBase
    {
        private const string SecondaryDb = "Secondary";
        private const string DefaultDb = "Default";
        private readonly ISekibanContext _sekibanContext;

        public MultipleDbStoryTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture) =>
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
            // Default を明示的に指定して、１件データを作成
            await _sekibanContext.SekibanActionAsync(
                DefaultDb,
                async () =>
                {
                    await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(new CreateBranch("TEST"));
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

            // Secondaryで3件データを作成
            await _sekibanContext.SekibanActionAsync(
                SecondaryDb,
                async () =>
                {
                    await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(new CreateBranch("JAPAN"));
                    await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(new CreateBranch("USA"));
                    await aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(new CreateBranch("MEXICO"));
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
}
