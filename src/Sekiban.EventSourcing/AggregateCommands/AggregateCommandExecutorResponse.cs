namespace Sekiban.EventSourcing.AggregateCommands;

public record AggregateCommandExecutorResponse<TContents, C> where TContents : IAggregateContents, new() where C : IAggregateCommand
{
    public AggregateDto<TContents>? AggregateDto { get; init; } = null;
    public AggregateCommandDocument<C> Command { get; init; } = new();
    public List<IAggregateEvent> Events { get; init; } = new();

    public AggregateCommandExecutorResponse(AggregateCommandDocument<C> command) =>
        Command = command;
    public AggregateCommandExecutorResponse() { }
}
