using DotNext;
using MediatR;
using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelUpdater<TReadModel, TId> where TReadModel : IReadModel<TId>
{
    public static abstract Result<ImmutableList<TReadModel>> RetrieveItemsForUpdate(ImmutableList<IEvent> events);
    public static abstract Result<ReadModelChanges<TReadModel, TId>> OnNewEvent(ReadModelChanges<TReadModel, TId> changes, IEvent ev);
    public static abstract Result<Unit> Persistent(ReadModelChanges<TReadModel, TId> changes);
}
