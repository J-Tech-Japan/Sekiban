using FeatureCheck.Domain.Shared;
using Sekiban.Core.Aggregate;
using Sekiban.Testing.SingleProjections;
namespace FeatureCheck.Test;

public class AggregateTest<TAggregatePayload> : AggregateTest<TAggregatePayload, FeatureCheckDependency>
    where TAggregatePayload : IAggregatePayload, new()
{
}