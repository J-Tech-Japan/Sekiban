using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes;

public record FirstStage(int FirstProperty)
    : IInheritInSubtypesType, IAggregateSubtypePayload<IInheritInSubtypesType, FirstStage>
{
    public static FirstStage CreateInitialPayload(FirstStage? _) => new(0);
}
