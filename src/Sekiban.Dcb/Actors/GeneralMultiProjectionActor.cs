using System;
using System.Threading.Tasks;
using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Actors
{
    /// <summary>
    /// General actor implementation that manages a single multi-projector instance by name.
    /// </summary>
    public class GeneralMultiProjectionActor : IMultiProjectionActorCommon
    {
    private readonly IMultiProjectorTypes _types;
    private readonly DcbDomainTypes _domain;
        private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;
        private readonly string _projectorName;
        private IMultiProjectorCommon? _projector;
        private Guid _lastEventId;
        private string _lastSortableUniqueId = string.Empty;
        private int _version;

        public GeneralMultiProjectionActor(DcbDomainTypes domainTypes, string projectorName)
        {
            _types = domainTypes.MultiProjectorTypes;
            _domain = domainTypes;
            _jsonOptions = domainTypes.JsonSerializerOptions;
            _projectorName = projectorName;
        }

    public async Task AddEventsAsync(System.Collections.Generic.IReadOnlyList<Event> events)
        {
            // Lazy init projector if needed
            if (_projector == null)
            {
                var init = _types.GenerateInitialPayload(_projectorName);
                if (!init.IsSuccess)
                {
                    throw init.GetException();
                }
                _projector = init.GetValue();
            }

            foreach (var ev in events)
            {
                var projected = _types.Project(_projector!, ev);
                if (!projected.IsSuccess)
                {
                    throw projected.GetException();
                }
                _projector = projected.GetValue();

                _lastEventId = ev.Id;
                _lastSortableUniqueId = ev.SortableUniqueIdValue;
                _version++;
            }
            await Task.CompletedTask;
        }

    public async Task AddSerializableEventsAsync(System.Collections.Generic.IReadOnlyList<SerializableEvent> events)
        {
            var list = new System.Collections.Generic.List<Event>(events.Count);
            foreach (var se in events)
            {
        var payload = _domain.EventTypes.DeserializeEventPayload(se.EventPayloadName, System.Text.Encoding.UTF8.GetString(se.Payload))
                  ?? throw new InvalidOperationException($"Unknown event type: {se.EventPayloadName}");
                var ev = new Event(
                    payload,
                    se.SortableUniqueIdValue,
            se.EventPayloadName,
                    se.Id,
            se.EventMetadata,
            se.Tags);
                list.Add(ev);
            }
            await AddEventsAsync(list);
        }

        public Task<ResultBox<MultiProjectionState>> GetStateAsync()
        {
            if (_projector == null)
            {
                var init = _types.GenerateInitialPayload(_projectorName);
                if (!init.IsSuccess)
                {
                    return Task.FromResult(ResultBox.Error<MultiProjectionState>(init.GetException()));
                }
                _projector = init.GetValue();
            }

            var projectorNameRb = _types.GetMultiProjectorNameFromMultiProjector(_projector!);
            if (!projectorNameRb.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<MultiProjectionState>(projectorNameRb.GetException()));
            }

            var state = new MultiProjectionState(
                (IMultiProjectionPayload)_projector!,
                projectorNameRb.GetValue(),
                _projector!.GetVersion(),
                _lastSortableUniqueId,
                _lastEventId,
                _version
            );
            return Task.FromResult(ResultBox.FromValue(state));
        }

        public Task SetCurrentState(SerializableMultiProjectionState state)
        {
            var rb = _types.Deserialize(state.Payload, state.MultiProjectionPayloadType, _jsonOptions);
            if (!rb.IsSuccess)
            {
                throw rb.GetException();
            }
            _projector = rb.GetValue();
            _lastEventId = state.LastEventId;
            _lastSortableUniqueId = state.LastSortableUniqueId;
            _version = state.Version;
            return Task.CompletedTask;
        }

        public Task<ResultBox<SerializableMultiProjectionState>> GetSerializableStateAsync()
        {
            // Ensure projector exists
            if (_projector == null)
            {
                var init = _types.GenerateInitialPayload(_projectorName);
                if (!init.IsSuccess)
                {
                    return Task.FromResult(ResultBox.Error<SerializableMultiProjectionState>(init.GetException()));
                }
                _projector = init.GetValue();
            }

            // Resolve projector name and serialize payload
            var projectorNameRb = _types.GetMultiProjectorNameFromMultiProjector(_projector!);
            if (!projectorNameRb.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<SerializableMultiProjectionState>(projectorNameRb.GetException()));
            }
            var name = projectorNameRb.GetValue();

            var payloadBytesRb = _types.Serialize(_projector!, _jsonOptions);
            if (!payloadBytesRb.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<SerializableMultiProjectionState>(payloadBytesRb.GetException()));
            }

            var state = new SerializableMultiProjectionState(
                payloadBytesRb.GetValue(),
                _projector!.GetType().FullName ?? _projector!.GetType().Name,
                name,
                _projector!.GetVersion(),
                _lastSortableUniqueId,
                _lastEventId,
                _version);

            return Task.FromResult(ResultBox.FromValue(state));
        }
    }
}
