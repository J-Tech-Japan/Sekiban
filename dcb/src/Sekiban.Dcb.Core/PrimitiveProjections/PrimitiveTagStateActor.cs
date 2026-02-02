using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.Primitives;

/// <summary>
///     Tag state actor that delegates projection to a primitive runtime (e.g., WASM).
/// </summary>
public sealed class PrimitiveTagStateActor : ITagStateActor
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly TagStateOptions _options;
    private readonly ITagStatePersistent _statePersistent;
    private readonly IPrimitiveProjectionHost _primitiveHost;
    private readonly IPrimitiveProjectionKeyFactory _keyFactory;
    private readonly TagStateId _tagStateId;
    private readonly string _serviceId;
    private readonly ILogger<PrimitiveTagStateActor> _logger;

    private IPrimitiveProjectionInstance? _instance;
    private int _version;
    private string _lastSortableUniqueId = string.Empty;

    public PrimitiveTagStateActor(
        TagStateId tagStateId,
        string serviceId,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        TagStateOptions options,
        IActorObjectAccessor actorAccessor,
        ITagStatePersistent statePersistent,
        IPrimitiveProjectionHost primitiveHost,
        IPrimitiveProjectionKeyFactory keyFactory,
        ILogger<PrimitiveTagStateActor>? logger = null)
    {
        _tagStateId = tagStateId ?? throw new ArgumentNullException(nameof(tagStateId));
        _serviceId = serviceId ?? DefaultServiceIdProvider.DefaultServiceId;
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _actorAccessor = actorAccessor ?? throw new ArgumentNullException(nameof(actorAccessor));
        _statePersistent = statePersistent ?? throw new ArgumentNullException(nameof(statePersistent));
        _primitiveHost = primitiveHost ?? throw new ArgumentNullException(nameof(primitiveHost));
        _keyFactory = keyFactory ?? throw new ArgumentNullException(nameof(keyFactory));
        _logger = logger ?? NullLogger<PrimitiveTagStateActor>.Instance;
    }

    public Task<string> GetTagStateActorIdAsync() => Task.FromResult(_tagStateId.GetTagStateId());

    public async Task<SerializableTagState> GetStateAsync()
    {
        var tagState = await GetTagStateAsync();

        if (tagState.Payload is EmptyTagStatePayload)
        {
            return new SerializableTagState(
                Array.Empty<byte>(),
                tagState.Version,
                tagState.LastSortedUniqueId,
                tagState.TagGroup,
                tagState.TagContent,
                tagState.TagProjector,
                nameof(EmptyTagStatePayload),
                tagState.ProjectorVersion);
        }

        var serializeResult = _domainTypes.TagStatePayloadTypes.SerializePayload(tagState.Payload);
        if (!serializeResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to serialize payload: {serializeResult.GetException().Message}");
        }

        return new SerializableTagState(
            serializeResult.GetValue(),
            tagState.Version,
            tagState.LastSortedUniqueId,
            tagState.TagGroup,
            tagState.TagContent,
            tagState.TagProjector,
            tagState.Payload.GetType().Name,
            tagState.ProjectorVersion);
    }

    public async Task<TagState> GetTagStateAsync()
    {
        string? currentLatestSortableUniqueId = null;
        var tagConsistentActorId = $"{_tagStateId.TagGroup}:{_tagStateId.TagContent}";
        var tagConsistentActorResult =
            await _actorAccessor.GetActorAsync<ITagConsistentActorCommon>(tagConsistentActorId);
        if (tagConsistentActorResult.IsSuccess)
        {
            var tagConsistentActor = tagConsistentActorResult.GetValue();
            var latestSortableUniqueIdResult = await tagConsistentActor.GetLatestSortableUniqueIdAsync();
            if (latestSortableUniqueIdResult.IsSuccess)
            {
                currentLatestSortableUniqueId = latestSortableUniqueIdResult.GetValue();
            }
        }

        var cachedState = await _statePersistent.LoadStateAsync();

        if (cachedState != null && cachedState.LastSortedUniqueId == currentLatestSortableUniqueId)
        {
            await EnsureInstanceRestoredAsync(cachedState);
            return cachedState;
        }

        var computedState = await ComputeStateFromEventsAsync(currentLatestSortableUniqueId, cachedState);
        await _statePersistent.SaveStateAsync(computedState);
        return computedState;
    }

    public async Task UpdateStateAsync(TagState newState)
    {
        if (newState.TagGroup != _tagStateId.TagGroup ||
            newState.TagContent != _tagStateId.TagContent ||
            newState.TagProjector != _tagStateId.TagProjectorName)
        {
            throw new InvalidOperationException(
                $"Cannot change tag state identity. Expected {_tagStateId}, " +
                $"but got {newState.TagGroup}:{newState.TagContent}:{newState.TagProjector}");
        }

        await _statePersistent.SaveStateAsync(newState);
        await EnsureInstanceRestoredAsync(newState);
    }

    public async Task ClearCacheAsync()
    {
        await _statePersistent.ClearStateAsync();
        _instance?.Dispose();
        _instance = null;
        _version = 0;
        _lastSortableUniqueId = string.Empty;
    }

    private async Task<TagState> ComputeStateFromEventsAsync(string? latestSortableUniqueId, TagState? cachedState)
    {
        var projectorVersionResult = _domainTypes.TagProjectorTypes.GetProjectorVersion(_tagStateId.TagProjectorName);
        var projectorVersion = projectorVersionResult.IsSuccess ? projectorVersionResult.GetValue() : string.Empty;

        if (string.IsNullOrEmpty(latestSortableUniqueId))
        {
            return TagState.GetEmpty(_tagStateId) with { ProjectorVersion = projectorVersion };
        }

        var tag = CreateTag(_tagStateId.TagGroup, _tagStateId.TagContent);
        SortableUniqueId? since = null;
        if (cachedState != null && !string.IsNullOrWhiteSpace(cachedState.LastSortedUniqueId))
        {
            since = new SortableUniqueId(cachedState.LastSortedUniqueId);
            await EnsureInstanceRestoredAsync(cachedState);
        }

        var readResult = await _eventStore.ReadEventsByTagAsync(tag, since);
        if (!readResult.IsSuccess)
        {
            throw readResult.GetException();
        }

        var events = readResult.GetValue()
            .OrderBy(ev => ev.SortableUniqueIdValue, StringComparer.Ordinal)
            .ToList();

        await EnsureInstanceRestoredAsync(cachedState);

        foreach (var ev in events)
        {
            ApplyEvent(ev);
        }

        if (_instance == null)
        {
            return TagState.GetEmpty(_tagStateId) with { ProjectorVersion = projectorVersion };
        }

        var stateJson = _instance.SerializeState();
        var payloadResult = _domainTypes.TagStatePayloadTypes.DeserializePayload(
            _tagStateId.TagProjectorName,
            Encoding.UTF8.GetBytes(stateJson ?? string.Empty));

        var payload = payloadResult.IsSuccess
            ? payloadResult.GetValue()
            : new EmptyTagStatePayload();

        return new TagState(
            payload,
            _version,
            _lastSortableUniqueId,
            _tagStateId.TagGroup,
            _tagStateId.TagContent,
            _tagStateId.TagProjectorName,
            projectorVersion);
    }

    private void ApplyEvent(Event ev)
    {
        if (_instance == null)
        {
            return;
        }

        var payloadJson = _domainTypes.EventTypes.SerializeEventPayload(ev.Payload);
        _instance.ApplyEvent(ev.EventType, payloadJson, ev.Tags, ev.SortableUniqueIdValue);
        _version++;
        if (string.IsNullOrEmpty(_lastSortableUniqueId) ||
            string.Compare(ev.SortableUniqueIdValue, _lastSortableUniqueId, StringComparison.Ordinal) > 0)
        {
            _lastSortableUniqueId = ev.SortableUniqueIdValue;
        }
    }

    private async Task EnsureInstanceRestoredAsync(TagState? cachedState)
    {
        if (_instance == null)
        {
            var key = _keyFactory.GetTagStateKey(_tagStateId.GetTagStateId(), _serviceId);
            _instance = _primitiveHost.CreateInstance(key);
            _logger.LogDebug("Primitive tag state instance created for {TagStateId}", _tagStateId.GetTagStateId());
        }

        if (cachedState == null)
        {
            _version = 0;
            _lastSortableUniqueId = string.Empty;
            return;
        }

        _version = cachedState.Version;
        _lastSortableUniqueId = cachedState.LastSortedUniqueId;

        var serializeResult = _domainTypes.TagStatePayloadTypes.SerializePayload(cachedState.Payload);
        if (!serializeResult.IsSuccess)
        {
            return;
        }

        var json = Encoding.UTF8.GetString(serializeResult.GetValue());
        if (!string.IsNullOrWhiteSpace(json))
        {
            _instance?.RestoreState(json);
        }
    }

    private ITag CreateTag(string tagGroup, string tagContent)
    {
        var tagString = $"{tagGroup}:{tagContent}";
        return _domainTypes.TagTypes.GetTag(tagString);
    }
}
