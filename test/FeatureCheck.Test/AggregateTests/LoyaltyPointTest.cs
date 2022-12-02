using System;
using System.Linq;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using Xunit;

namespace Customer.Test.AggregateTests;

public class LoyaltyPointTest : AggregateTest<LoyaltyPoint>
{
    [Fact]
    public void CreateAndAddTest()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("Test"));
        var clientId = RunEnvironmentCommand(new CreateClient(branchId, "Test Name", "test@example.com"));
        WhenCommand(new LoyaltyPointAndAddPoint(clientId, 1000));
        ThenNotThrowsAnException();
        var eventTime = GetLatestEvents()
            .Where(m => m.GetPayload() is LoyaltyPointAdded)
            .Select(m => m.GetPayload() is LoyaltyPointAdded added ? added.HappenedDate : DateTime.Now)
            .First();
        ThenPayloadIs(new LoyaltyPoint(1000, eventTime, false));
    }
}
