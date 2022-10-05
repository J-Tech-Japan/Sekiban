using CustomerDomainContext.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.TestHelpers;
namespace CustomerDomainXTest;

public class
    CustomerMultipleAggregateProjectionTestBase<TProjection, TProjectionContents> : MultipleAggregateProjectionTestBase<TProjection,
        TProjectionContents> where TProjection : MultipleAggregateProjectionBase<TProjectionContents>, new()
    where TProjectionContents : IMultipleAggregateProjectionContents, new()

{
    protected CustomerMultipleAggregateProjectionTestBase() : base(CustomerDependency.GetOptions())
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
