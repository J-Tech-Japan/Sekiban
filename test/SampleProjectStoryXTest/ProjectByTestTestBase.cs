using Sekiban.EventSourcing.TestHelpers;
using System;
namespace SampleProjectStoryXTest
{
    public class ProjectByTestTestBase : ByTestTestBase
    {
        public ProjectByTestTestBase(bool inMemory = true) : base(inMemory) { }
        public override IServiceProvider SetupService(bool inMemory) =>
            DependencyHelper.CreateDefaultProvider(new TestFixture(), inMemory);
    }
}
