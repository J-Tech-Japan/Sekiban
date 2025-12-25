using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Tests.Queries;

public record ItemRemoved(Guid Id) : IEventPayload;
