using CustomerDomainContext.Shared;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.TestHelpers;
namespace CustomerDomainXTest;

public class MultipleAggregateProjectionTestBase<TProjection> : CommonMultipleAggregateProjectionTestBase<TProjection>
    where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()

{
    protected MultipleAggregateProjectionTestBase() : base(CustomerDependency.GetOptions())
    {
    }
}
