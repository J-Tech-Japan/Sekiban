using FeatureCheck.Domain.Aggregates.SubTypes.InterfaceBaseTypes;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
namespace FeatureCheck.Test.AggregateTests;

public class InterfaceParentTest : AggregateTest<ICartAggregate, FeatureCheckDependency>;
