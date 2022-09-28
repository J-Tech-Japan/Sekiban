using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public record SingleAggregateListProjectionDto<TAggregateDto> : IMultipleAggregateProjectionContents where TAggregateDto : ISingleAggregate
{
    public IReadOnlyCollection<TAggregateDto> List { get; init; } = new List<TAggregateDto>();
}
