using MediatR;
using ResultBoxes;
using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelUpdater<TReadModel, TId> where TReadModel : IReadModel<TId>
{
    public static abstract ResultBox<ImmutableList<TReadModel>> RetrieveItemsForUpdate(ImmutableList<IEvent> events);
    public static abstract ResultBox<ReadModelChanges<TReadModel, TId>> OnNewEvent(
        ReadModelChanges<TReadModel, TId> changes,
        IEvent ev);
    public static abstract ResultBox<Unit> Persistent(ReadModelChanges<TReadModel, TId> changes);
}
