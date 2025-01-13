using ResultBoxes;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Query;

public interface IQueryCommon<TOutput> where TOutput : notnull;
public interface IListQueryCommon<TQuery> where TQuery : IListQueryCommon<TQuery>, IEquatable<TQuery>;
public interface IListQueryCommon<TQuery, TOutput> : IListQueryCommon<TQuery>
    where TQuery : IListQueryCommon<TQuery>, IEquatable<TQuery> where TOutput : notnull;
public interface IMultiProjectionQueryCommon<TMultiProjector, TOutput> where TOutput : notnull
    where TMultiProjector : IMultiProjector<TMultiProjector>;
public interface
    IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput> : IMultiProjectionQueryCommon<TMultiProjector, TOutput>,
    IListQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TMultiProjector : IMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
{
    public static abstract ResultBox<IEnumerable<TOutput>> HandleFilter(
        MultiProjectionState<TMultiProjector> projection,
        TQuery query,
        IQueryContext context);
    public static abstract ResultBox<IEnumerable<TOutput>> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
public interface IQueryContext;
public record MultiProjectionState<TMultiProjector>(
    TMultiProjector Payload,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version,
    string RootPartitionKey) where TMultiProjector : IMultiProjector<TMultiProjector>
{
    public MultiProjectionState() : this(
        TMultiProjector.GenerateInitialPayload(),
        Guid.Empty,
        string.Empty,
        0,
        0,
        string.Empty)
    {
    }
    public string GetPayloadVersionIdentifier() => Payload.GetVersion();
    public ResultBox<MultiProjectionState<TMultiProjector>> ApplyEvent(IEvent ev) =>
        Payload
            .Project(Payload, ev)
            .Remap(
                p => this with
                {
                    Payload = p,
                    LastEventId = ev.Id,
                    LastSortableUniqueId = ev.SortableUniqueId,
                    Version = Version + 1
                });
}
