using CustomerDomainContext.Aggregates.Clients;
using Sekiban.EventSourcing.TestHelpers;
using System.Threading.Tasks;
using Xunit;
namespace SampleProjectStoryXTest.SingleAggregates;

public class ClientSpec : TestBase
{
    public ClientSpec(TestFixture testFixture) : base(testFixture) { }
    [Fact]
    public async Task ClientCreateSpec()
    {
        var helper = new AggregateTestHelper<Client, ClientDto>(_serviceProvider);
    }
}
