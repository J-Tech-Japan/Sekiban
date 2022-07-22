namespace Sekiban.EventSourcing.AggregateCommands;

public class AggregateCommandExecutorResponse<TContents, C> where TContents : IAggregateContents where C : IAggregateCommand
{
    public AggregateDto<TContents>? AggregateDto { get; set; } = null;
    public AggregateCommandDocument<C> Command { get; init; }
    public List<AggregateEvent> Events { get; set; } = new();

    public AggregateCommandExecutorResponse(AggregateCommandDocument<C> command) =>
        Command = command;
}
