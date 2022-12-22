using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sekiban.Core.Query.MultiProjections;

public class TargetAggregatePayloadCollection
{
    private ImmutableList<IAggregatePayload> TargetAggregatePayloads { get; set; } = ImmutableList<IAggregatePayload>.Empty;
    public TargetAggregatePayloadCollection Add(IAggregatePayload aggregate)
    {
        TargetAggregatePayloads.Add(aggregate);
        return this;
    }
}