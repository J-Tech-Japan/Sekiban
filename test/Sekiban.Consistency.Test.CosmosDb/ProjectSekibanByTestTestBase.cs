using Sekiban.Testing.Story;
using System;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest;

public class ProjectSekibanByTestTestBase : SekibanByTestTestBase
{
    private readonly ISekibanTestFixture _fixture = new TestBase.SekibanTestFixture();
    public ProjectSekibanByTestTestBase(
        ITestOutputHelper testOutputHelper,
        bool inMemory = true) : base(inMemory)
    {
        _fixture.TestOutputHelper = testOutputHelper;
    }

    public override IServiceProvider SetupService(bool inMemory) => DependencyHelper.CreateDefaultProvider(_fixture, inMemory);
}
