using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
namespace Sekiban.Core.Documents;

public interface IEventWriter
{
    Task SaveEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream) where TEvent : IEvent;
}
