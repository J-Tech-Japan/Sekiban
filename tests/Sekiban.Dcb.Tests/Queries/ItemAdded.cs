using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Tests.Queries;

public record ItemAdded(Guid Id, string Name, string Category, decimal Price, DateTime CreatedAt) : IEventPayload;
