using Sekiban.Dcb.Events;
namespace Dcb.Domain.WithoutResult.Student;

// DCB Pattern: One command produces ONE event that represents a business fact.
// Events can be tagged with multiple entities to affect their states.

// Entity Creation Events (single entity affected)
public record StudentCreated(Guid StudentId, string Name, int MaxClassCount = 5) : IEventPayload;

// Business Fact Events (multiple entities affected)
