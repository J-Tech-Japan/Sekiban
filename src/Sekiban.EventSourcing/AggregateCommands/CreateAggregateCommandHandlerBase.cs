﻿namespace Sekiban.EventSourcing.AggregateCommands;

public abstract class CreateAggregateCommandHandlerBase<T, C> : ICreateAggregateCommandHandler<T, C>
    where T : IAggregate where C : ICreateAggregateCommand<T>
{
    async Task<AggregateCommandResponse<T>> ICreateAggregateCommandHandler<T, C>.HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument) =>
        new(await CreateAggregateAsync(aggregateCommandDocument.Payload));

    protected abstract Task<T> CreateAggregateAsync(C command);
}