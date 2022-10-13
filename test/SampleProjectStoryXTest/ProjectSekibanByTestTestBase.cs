using Sekiban.EventSourcing.TestHelpers.StoryTests;
using System;
namespace SampleProjectStoryXTest;

public class ProjectSekibanByTestTestBase : SekibanByTestTestBase
{
    public ProjectSekibanByTestTestBase(bool inMemory = true) : base(inMemory) { }
    public override IServiceProvider SetupService(bool inMemory)
    {
        return DependencyHelper.CreateDefaultProvider(new SekibanTestFixture(), inMemory);
    }
}
