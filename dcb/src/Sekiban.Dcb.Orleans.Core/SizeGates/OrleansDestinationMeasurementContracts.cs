using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Orleans;

/// <summary>
/// Internal bridge between Orleans.Core's provider-neutral publisher and a provider package. Provider assemblies may
/// capture opaque measurement state here, but the shared assembly never references a provider SDK.
/// </summary>
internal interface IOrleansDestinationMeasurementCapture
{
    bool Matches(string providerName);

    object? Capture(
        string providerName,
        string streamNamespace,
        Guid streamId,
        SerializableEvent serializedEvent,
        IReadOnlyDictionary<string, object>? requestContext,
        out string? failureReason);
}

/// <summary>Opaque provider state plus the exact context captured for one destination.</summary>
internal sealed record OrleansDestinationPlanState(
    string DestinationKey,
    string ProviderName,
    string StreamNamespace,
    Guid StreamId,
    object? MeasurementState,
    IReadOnlyDictionary<string, object>? RequestContext,
    string? FailureReason)
{
    /// <summary>The service identity used in the destination key for the prepared publication.</summary>
    public string ServiceId { get; init; } = string.Empty;

    /// <summary>
    /// The provider/stream target prepared before admission. Retries use this opaque target directly and never
    /// resolve the provider or stream again.
    /// </summary>
    public IOrleansPreparedStreamTarget? PreparedTarget { get; init; }
}
