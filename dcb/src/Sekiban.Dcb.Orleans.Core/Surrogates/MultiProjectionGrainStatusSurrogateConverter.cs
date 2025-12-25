using Orleans;
using Sekiban.Dcb.Orleans.Grains;

namespace Sekiban.Dcb.Orleans.Surrogates;

/// <summary>
/// Orleans surrogate converter for MultiProjectionGrainStatus
/// </summary>
[RegisterConverter]
public sealed class MultiProjectionGrainStatusSurrogateConverter : IConverter<MultiProjectionGrainStatus, MultiProjectionGrainStatusSurrogate>
{
    public MultiProjectionGrainStatus ConvertFromSurrogate(in MultiProjectionGrainStatusSurrogate surrogate) =>
        new(
            surrogate.ProjectorName,
            surrogate.IsSubscriptionActive,
            surrogate.IsCaughtUp,
            surrogate.CurrentPosition,
            surrogate.EventsProcessed,
            surrogate.LastEventTime,
            surrogate.LastPersistTime,
            surrogate.StateSize,
            surrogate.SafeStateSize,
            surrogate.UnsafeStateSize,
            surrogate.HasError,
            surrogate.LastError);

    public MultiProjectionGrainStatusSurrogate ConvertToSurrogate(in MultiProjectionGrainStatus value) =>
        new(
            value.ProjectorName,
            value.IsSubscriptionActive,
            value.IsCaughtUp,
            value.CurrentPosition,
            value.EventsProcessed,
            value.LastEventTime,
            value.LastPersistTime,
            value.StateSize,
            value.SafeStateSize,
            value.UnsafeStateSize,
            value.HasError,
            value.LastError);
}
