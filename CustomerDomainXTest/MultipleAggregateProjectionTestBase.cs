using CustomerDomainContext.Projections.ClientLoyaltyPointLists;
using CustomerDomainContext.Projections.ClientLoyaltyPointMultiples;
using CustomerDomainContext.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.TestHelpers;
namespace CustomerDomainXTest;

public class
    MultipleAggregateProjectionTestBase<TProjection, TProjectionContents> : CommonMultipleAggregateProjectionTestBase<TProjection,
        TProjectionContents> where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()

{
    protected MultipleAggregateProjectionTestBase() : base(CustomerDependency.GetOptions())
    {
    }

    protected override void SetupDependency(IServiceCollection serviceCollection)
    {
        base.SetupDependency(serviceCollection);
        serviceCollection
            .AddTransient<ProjectionQueryFilterTestChecker<ClientLoyaltyPointMultipleProjection,
                ClientLoyaltyPointMultipleProjection.ContentsDefinition, ClientLoyaltyPointMultipleProjectionQueryFilter,
                ClientLoyaltyPointMultipleProjectionQueryFilter.QueryFilterParameter, ClientLoyaltyPointMultipleProjection.ContentsDefinition>>();
        serviceCollection
            .AddTransient<ProjectionQueryListFilterTestChecker<ClientLoyaltyPointListProjection, ClientLoyaltyPointListProjection.ContentsDefinition,
                ClientLoyaltyPointQueryFilter, ClientLoyaltyPointQueryFilter.QueryFilterParameter,
                ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord>>();
        serviceCollection.AddTransient<ClientLoyaltyPointQueryFilter>();
        serviceCollection.AddTransient<ClientLoyaltyPointMultipleProjectionQueryFilter>();
    }
}
