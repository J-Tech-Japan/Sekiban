using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public class AggregateListProjectionTestBase<TAggregate, TAggregateContents> : CommonMultipleAggregateProjectionTestBase<
    SingleAggregateListProjector<TAggregate, AggregateDto<TAggregateContents>, DefaultSingleAggregateProjector<TAggregate>>,
    SingleAggregateListProjectionDto<AggregateDto<TAggregateContents>>> where TAggregate : TransferableAggregateBase<TAggregateContents>
    where TAggregateContents : IAggregateContents, new()
{
    public AggregateListProjectionTestBase(SekibanDependencyOptions dependencyOptions) : base(dependencyOptions)
    {
    }
}
