namespace Sekiban.EventSourcing.AggregateCommands;

public class AggregateCommandExecutorResponse<Q, C>
    where Q : AggregateDtoBase, new()
    where C : IAggregateCommand
{
    public Q? AggregateDto { get; set; } = null;
    public AggregateCommandDocument<C> Command { get; init; }
    public List<AggregateEvent> Events { get; set; } = new();

    public AggregateCommandExecutorResponse(AggregateCommandDocument<C> command) =>
        Command = command;
}
