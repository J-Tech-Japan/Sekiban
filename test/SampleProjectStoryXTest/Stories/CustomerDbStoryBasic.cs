using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Queries;
using Xunit;
namespace SampleProjectStoryXTest.Stories;

public class CustomerDbStoryBasic : TestBase
{
    private readonly AggregateCommandExecutor _aggregateCommandExecutor;
    private readonly SingleAggregateService _aggregateService;

    public CustomerDbStoryBasic(TestFixture testFixture) : base(testFixture)
    {
        _aggregateCommandExecutor = GetService<AggregateCommandExecutor>();
        _aggregateService = GetService<SingleAggregateService>();
    }
    [Fact]
    public void Test1()
    {
        
        
        
    }
}
