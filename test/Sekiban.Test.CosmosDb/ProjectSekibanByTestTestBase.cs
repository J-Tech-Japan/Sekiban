using FeatureCheck.Domain.Shared;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb;

public class ProjectSekibanByTestTestBase : SekibanByTestTestBase
{
    private readonly ISekibanTestFixture _fixture = new TestBase.SekibanTestFixture();
    public ProjectSekibanByTestTestBase(ITestOutputHelper testOutputHelper, ISekibanServiceProviderGenerator serviceProviderGenerator)
    {
        _fixture.TestOutputHelper = testOutputHelper;
        ServiceProvider = serviceProviderGenerator.Generate(_fixture, new FeatureCheckDependency());
    }
}
