using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Events;
using System;
using System.Linq;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

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
