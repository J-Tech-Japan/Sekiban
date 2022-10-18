using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.MultipleAggregate;

public record SingleAggregateListProjectionDto<TAggregateDto> : IMultipleAggregateProjectionContents where TAggregateDto : ISingleAggregate
{
    public IReadOnlyCollection<TAggregateDto> List { get; init; } = new List<TAggregateDto>();
}
