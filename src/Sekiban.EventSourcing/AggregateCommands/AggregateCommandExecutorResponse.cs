using System.ComponentModel.DataAnnotations;
namespace Sekiban.EventSourcing.AggregateCommands;

public record AggregateCommandExecutorResponse(Guid? AggregateId, Guid? CommandId, int Version, IEnumerable<ValidationResult>? ValidationResults)
{
}
