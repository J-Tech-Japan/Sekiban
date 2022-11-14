using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Customer.Domain.Aggregates.LoyaltyPoints.Events;
using System;
using System.Linq;
using Xunit;
namespace Customer.Test.AggregateTests;

public class LoyaltyPointTest : AggregateTestBase<LoyaltyPoint>
{
    [Fact]
    public void CreateAndAddTest()
    {
        var branchId = RunEnvironmentCreateCommand(new CreateBranch("Test"));
        var clientId = RunEnvironmentCreateCommand(new CreateClient(branchId, "Test Name", "test@example.com"));
        WhenCreate(new CreateLoyaltyPointAndAddPoint(clientId, 1000));
        ThenNotThrowsAnException();
        var eventTime = GetLatestEvents()
            .Where(m => m.GetPayload() is LoyaltyPointAdded)
            .Select(m => m.GetPayload() is LoyaltyPointAdded added ? added.HappenedDate : DateTime.Now)
            .First();
        ThenPayloadIs(new LoyaltyPoint(1000, eventTime, false));



    }
}
