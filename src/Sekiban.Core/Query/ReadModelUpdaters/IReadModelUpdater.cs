using Sekiban.Core.Events;
using System.Collections.Immutable;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModel
{
    string LastSortableUniqueId { get; }
}
public interface IReadModelChange<TReadModel> where TReadModel : IReadModel
{
    public TReadModel ReadModel { get; }
}
public record ReadModelUpdated<TReadModel>(TReadModel ReadModel) : IReadModelChange<TReadModel> where TReadModel : IReadModel;
public record ReadModelInserted<TReadModel>(TReadModel ReadModel) : IReadModelChange<TReadModel> where TReadModel : IReadModel;
public record ReadModelDeleted<TReadModel>(TReadModel ReadModel) : IReadModelChange<TReadModel> where TReadModel : IReadModel;
public record ReadModelUnchanged<TReadModel>(TReadModel ReadModel) : IReadModelChange<TReadModel> where TReadModel : IReadModel;
public interface IReadModelUpdater<TReadModel> where TReadModel : IReadModel
{
    public static abstract ImmutableList<TReadModel> RetrieveItemsForUpdate(ImmutableList<IEvent> events);
    public static abstract IEnumerable<IReadModelChange<TReadModel>> OnNewEvent(ImmutableList<TReadModel> readModels, IEvent ev);
    public static abstract void Persistent(IEnumerable<IReadModelChange<TReadModel>> readModelChanges);
}
