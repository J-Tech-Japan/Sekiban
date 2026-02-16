using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# implementation for serialized MultiProjection projection primitive.
///     Wraps GeneralMultiProjectionActor behind the primitive/accumulator abstraction.
/// </summary>
public sealed class NativeMultiProjectionProjectionPrimitive : IMultiProjectionProjectionPrimitive
{
    private readonly DcbDomainTypes _domainTypes;

    public NativeMultiProjectionProjectionPrimitive(DcbDomainTypes domainTypes)
    {
        _domainTypes = domainTypes;
    }

    public IMultiProjectionProjectionAccumulator CreateAccumulator(
        string projectorName,
        string projectorVersion,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null)
    {
        return CreateNativeAccumulator(projectorName, projectorVersion, options, logger);
    }

    internal NativeMultiProjectionProjectionAccumulator CreateNativeAccumulator(
        string projectorName,
        string projectorVersion,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null)
    {
        var actor = new GeneralMultiProjectionActor(_domainTypes, projectorName, options, logger);
        return new NativeMultiProjectionProjectionAccumulator(
            actor,
            projectorName,
            projectorVersion,
            logger ?? NullLogger.Instance);
    }

    internal sealed class NativeMultiProjectionProjectionAccumulator : IMultiProjectionProjectionAccumulator
    {
        private readonly GeneralMultiProjectionActor _actor;
        private readonly string _projectorName;
        private readonly string _projectorVersion;
        private readonly ILogger _logger;

        public NativeMultiProjectionProjectionAccumulator(
            GeneralMultiProjectionActor actor,
            string projectorName,
            string projectorVersion,
            ILogger logger)
        {
            _actor = actor;
            _projectorName = projectorName;
            _projectorVersion = projectorVersion;
            _logger = logger;
        }

        /// <summary>
        ///     Provides native-only access to the underlying actor for query execution
        ///     and operations that require the full actor API (e.g., promote, state access).
        /// </summary>
        internal GeneralMultiProjectionActor GetActor() => _actor;

        public bool ApplySnapshot(SerializableMultiProjectionStateEnvelope? snapshot)
        {
            if (snapshot == null)
            {
                return true;
            }

            try
            {
                _actor.SetSnapshotAsync(snapshot).GetAwaiter().GetResult();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[{ProjectorName}] Failed to apply snapshot",
                    _projectorName);
                return false;
            }
        }

        public bool ApplyEvents(
            IReadOnlyList<SerializableEvent> events,
            string? latestSortableUniqueId,
            CancellationToken cancellationToken = default)
        {
            if (events.Count == 0)
            {
                return true;
            }

            try
            {
                _actor.AddSerializableEventsAsync(events, finishedCatchUp: true)
                    .GetAwaiter().GetResult();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[{ProjectorName}] Failed to apply {EventCount} events",
                    _projectorName,
                    events.Count);
                return false;
            }
        }

        public ResultBox<SerializableMultiProjectionStateEnvelope> GetSnapshot()
        {
            return _actor.GetSnapshotAsync(canGetUnsafeState: true)
                .GetAwaiter().GetResult();
        }

        public ResultBox<MultiProjectionStateMetadata> GetMetadata()
        {
            var unsafeResult = _actor.GetStateAsync(canGetUnsafeState: true)
                .GetAwaiter().GetResult();
            var safeResult = _actor.GetStateAsync(canGetUnsafeState: false)
                .GetAwaiter().GetResult();

            if (!safeResult.IsSuccess)
            {
                return ResultBox.Error<MultiProjectionStateMetadata>(safeResult.GetException());
            }

            var safeState = safeResult.GetValue();

            int unsafeVersion = 0;
            string? unsafeLastSortableUniqueId = null;
            Guid? unsafeLastEventId = null;

            if (unsafeResult.IsSuccess)
            {
                var unsafeState = unsafeResult.GetValue();
                unsafeVersion = unsafeState.Version;
                unsafeLastSortableUniqueId = unsafeState.LastSortableUniqueId;
                unsafeLastEventId = unsafeState.LastEventId;
            }

            return ResultBox.FromValue(new MultiProjectionStateMetadata(
                ProjectorName: _projectorName,
                ProjectorVersion: _projectorVersion,
                IsCatchedUp: safeState.IsCatchedUp,
                SafeVersion: safeState.Version,
                SafeLastSortableUniqueId: safeState.LastSortableUniqueId,
                UnsafeVersion: unsafeVersion,
                UnsafeLastSortableUniqueId: unsafeLastSortableUniqueId,
                UnsafeLastEventId: unsafeLastEventId,
                IsSafeState: safeState.IsSafeState));
        }
    }
}
