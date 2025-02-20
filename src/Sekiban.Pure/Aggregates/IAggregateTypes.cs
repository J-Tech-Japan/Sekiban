using ResultBoxes;
using System;
using System.Collections.Generic;

namespace Sekiban.Pure.Aggregates;

public interface IAggregateTypes
{
    public ResultBox<IAggregate> ToTypedPayload(Aggregate aggregate);
    public List<Type> GetAggregateTypes();
}
