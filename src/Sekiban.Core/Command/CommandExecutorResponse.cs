using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Command;

public record CommandExecutorResponse(Guid? AggregateId, Guid? CommandId, int Version, IEnumerable<ValidationResult>? ValidationResults)
{
}