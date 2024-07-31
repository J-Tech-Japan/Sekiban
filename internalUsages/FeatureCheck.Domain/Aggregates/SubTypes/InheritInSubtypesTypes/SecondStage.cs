using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes;

public record SecondStage(int FirstProperty, int SecondProperty)
    : FirstStage(FirstProperty), IAggregateSubtypePayload<IInheritInSubtypesType, SecondStage>
{
    public static SecondStage CreateInitialPayload(SecondStage? _) => new(0, 0);
}
