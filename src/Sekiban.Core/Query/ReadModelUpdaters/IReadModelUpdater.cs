using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelUpdater<TReadModel> where TReadModel : IReadModel
{
    public static abstract ImmutableList<TReadModel> RetrieveItemsForUpdate(ImmutableList<IEvent> events);
    public static abstract IEnumerable<IReadModelChange<TReadModel>> OnNewEvent(ImmutableList<TReadModel> readModels, IEvent ev);
    public static abstract void Persistent(IEnumerable<IReadModelChange<TReadModel>> readModelChanges);
}
