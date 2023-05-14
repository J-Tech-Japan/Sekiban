using Sekiban.Testing.Story;
using System;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest;

public class ProjectSekibanByTestTestBase : SekibanByTestTestBase
{
    private readonly ISekibanTestFixture _fixture = new TestBase.SekibanTestFixture();
    public ProjectSekibanByTestTestBase(
        ITestOutputHelper testOutputHelper,
        DependencyHelper.DatabaseType databaseType = DependencyHelper.DatabaseType.CosmosDb) : base()
    {
        _fixture.TestOutputHelper = testOutputHelper;
        ServiceProvider = SetupService(databaseType);
    } 

    public IServiceProvider SetupService(DependencyHelper.DatabaseType databaseType) => DependencyHelper.CreateDefaultProvider(_fixture, databaseType);
}
