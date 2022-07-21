namespace Sekiban.EventSourcing.AggregateCommands;

public class AggregateCommandExecutorResponse<TContents, C> where TContents : IAggregateContents, new() where C : IAggregateCommand
{
    public AggregateDtoBase<TContents>? AggregateDto { get; set; } = null;
    public AggregateCommandDocument<C> Command { get; init; }
    public List<AggregateEvent> Events { get; set; } = new();

    public AggregateCommandExecutorResponse(AggregateCommandDocument<C> command) =>
        Command = command;
}
