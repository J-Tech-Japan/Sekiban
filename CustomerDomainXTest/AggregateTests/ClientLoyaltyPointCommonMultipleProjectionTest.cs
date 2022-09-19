using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Events;
using CustomerDomainContext.Projections;
using CustomerDomainXTest;
using System;
using System.Collections.Generic;
using Xunit;
namespace SampleProjectStoryXTest.SingleAggregates;

public class ClientLoyaltyPointCommonMultipleProjectionTest : MultipleAggregateProjectionTestBase<ClientLoyaltyPointMultipleProjection>
{
    private static readonly Guid branchId = Guid.NewGuid();
    private static readonly string branchName = "Test Branch";

    [Fact]
    public void ProjectionTest()
    {
        GivenEvents((branchId, typeof(Branch), new BranchCreated(branchName)))
            .WhenProjection()
            .ThenNotThrowsAnException()
            .ThenDto(
                new ClientLoyaltyPointMultipleProjection
                {
                    Branches = new List<ClientLoyaltyPointMultipleProjection.ProjectedBranch>
                    {
                        new() { BranchId = branchId, BranchName = branchName }
                    }
                });
    }
}
