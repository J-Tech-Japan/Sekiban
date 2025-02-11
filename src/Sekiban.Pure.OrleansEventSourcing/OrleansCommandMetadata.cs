using Sekiban.Pure.Command.Handlers;
namespace Sekiban.Pure.OrleansEventSourcing;

[GenerateSerializer]
public record OrleansCommandMetadata([property:Id(0)]Guid CommandId, [property:Id(1)]string CausationId,
    [property:Id(2)]string CorrelationId, [property:Id(3)]string ExecutedUser)
{
    public static OrleansCommandMetadata FromCommandMetadata(CommandMetadata metadata) => new(metadata.CommandId, metadata.CausationId, metadata.CorrelationId, metadata.ExecutedUser);
    public CommandMetadata ToCommandMetadata() => new(CommandId, CausationId, CorrelationId, ExecutedUser);
}