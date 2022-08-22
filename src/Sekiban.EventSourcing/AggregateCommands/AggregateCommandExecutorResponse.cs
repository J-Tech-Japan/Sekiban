using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.AggregateCommands;

public record AggregateCommandExecutorResponse<TContents, C> where TContents : IAggregateContents, new() where C : IAggregateCommand
{
    public AggregateDto<TContents>? AggregateDto { get; init; } = null;
    public AggregateCommandDocument<C>? Command { get; init; }
    public List<IAggregateEvent> Events { get; init; } = new();
    public IEnumerable<ValidationResult> ValidationResults { get; init; } = new List<ValidationResult>();
    public AggregateCommandExecutorResponse(AggregateCommandDocument<C> command) =>
        Command = command;
    public AggregateCommandExecutorResponse() { }
}
