using CustomerDomainContext.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Shared;
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
        serviceCollection.AddQueryFilters(
            CustomerDependency.GetProjectionQueryFilterTypes(),
            CustomerDependency.GetProjectionListQueryFilterTypes(),
            CustomerDependency.GetAggregateListQueryFilterTypes(),
            CustomerDependency.GetSingleAggregateProjectionListQueryFilterTypes());

    }
}
