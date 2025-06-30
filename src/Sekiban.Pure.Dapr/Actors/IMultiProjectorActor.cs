using Dapr.Actors;
using ResultBoxes;
using Sekiban.Pure.Query;
using Sekiban.Pure.Dapr.Serialization;

namespace Sekiban.Pure.Dapr.Actors;

public interface IMultiProjectorActor : IActor
{
    Task<SerializableQueryResult> QueryAsync(SerializableQuery query);
    Task<SerializableListQueryResult> QueryListAsync(SerializableListQuery query);
    Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId);
    Task BuildStateAsync();
    Task RebuildStateAsync();
    Task HandlePublishedEvent(DaprEventEnvelope envelope);
}