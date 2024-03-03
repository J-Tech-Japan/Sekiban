using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Setting;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Test.Abstructs.Abstructs;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class MultipleDbStoryTest : TestBase<FeatureCheckDependency>
{
    private const string SecondaryDb = "Secondary";
    private const string DefaultDb = "Default";
    private readonly ISekibanContext _sekibanContext;

    public MultipleDbStoryTest(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new CosmosSekibanServiceProviderGenerator()) =>
        _sekibanContext = GetService<ISekibanContext>();

    [Fact]
    public async Task MultipleDatabaseTest()
    {
        // When executed without doing anything, it becomes the "Default" behavior.
        RemoveAllFromDefaultAndDissolvable();

        // Run command within Secondary
        await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () =>
            {
                await Task.CompletedTask;
                RemoveAllFromDefault();
            });
        // Run Command within Default Setting
        await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () =>
            {
                await commandExecutor.ExecCommandAsync(new CreateBranch("TEST"));
            });

        // If you get a List with "Default", you get one record.
        var list = await _sekibanContext.SekibanActionAsync(DefaultDb, async () => await multiProjectionService.GetAggregateList<Branch>());
        Assert.Single(list);

        // If you don't attach anything, it is also obtained from the Default Db.
        list = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Single(list);

        // If you get a List with "Secondary", you get zero records.
        list = await _sekibanContext.SekibanActionAsync(SecondaryDb, async () => await multiProjectionService.GetAggregateList<Branch>());
        Assert.Empty(list);

        // Create three pieces of data with "Secondary".
        await _sekibanContext.SekibanActionAsync(
            SecondaryDb,
            async () =>
            {
                await commandExecutor.ExecCommandAsync(new CreateBranch("JAPAN"));
                await commandExecutor.ExecCommandAsync(new CreateBranch("USA"));
                await commandExecutor.ExecCommandAsync(new CreateBranch("MEXICO"));
            });

        // If you get a List with "Default", you get one record.
        list = await _sekibanContext.SekibanActionAsync(DefaultDb, async () => await multiProjectionService.GetAggregateList<Branch>());
        Assert.Single(list);

        // If you don't attach anything, it is also obtained from the Default Db.
        list = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Single(list);

        // If you get a List with "Secondary", you get three records.
        list = await _sekibanContext.SekibanActionAsync(SecondaryDb, async () => await multiProjectionService.GetAggregateList<Branch>());
        Assert.Equal(3, list.Count);

        // nesting database can work.
        await _sekibanContext.SekibanActionAsync(
            DefaultDb,
            async () =>
            {
                // If you get a List with "Secondary", you get three records.
                list = await _sekibanContext.SekibanActionAsync(SecondaryDb, async () => await multiProjectionService.GetAggregateList<Branch>());
                Assert.Equal(3, list.Count);

                // After the nesting seconds, go back to default database.
                list = await multiProjectionService.GetAggregateList<Branch>();
                Assert.Single(list);

            });

    }
}
