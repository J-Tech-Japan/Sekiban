using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.SubTypes.InheritInSubtypesTypes;

public interface IInheritInSubtypesType : IParentAggregatePayload<IInheritInSubtypesType, FirstStage>;
