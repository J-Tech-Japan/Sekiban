using FeatureCheck.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb;

public class ProjectSekibanByTestTestBase : SekibanByTestTestBase
{
    private readonly ISekibanTestFixture _fixture = new TestBase<FeatureCheckDependency>.SekibanTestFixture();
    public ProjectSekibanByTestTestBase(ITestOutputHelper testOutputHelper, ISekibanServiceProviderGenerator serviceProviderGenerator)
    {
        _fixture.TestOutputHelper = testOutputHelper;
        ServiceProvider = serviceProviderGenerator.Generate(
            _fixture,
            new FeatureCheckDependency(),
            collection => collection.AddLogging(builder => builder.AddXUnit(_fixture.TestOutputHelper)));
    }
}
