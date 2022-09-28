using CustomerDomainContext.Shared;
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
}
