﻿using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Aggregate;

public interface IAggregate : IAggregateIdentifier, ISingleProjection
{
}