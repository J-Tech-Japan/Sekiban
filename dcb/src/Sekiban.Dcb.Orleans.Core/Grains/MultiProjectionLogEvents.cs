using Microsoft.Extensions.Logging;

namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Log event IDs for MultiProjection grain operations.
///     Used for structured logging and filtering.
/// </summary>
public static class MultiProjectionLogEvents
{
    /// <summary>Grain activation started.</summary>
    public static readonly EventId ActivationStarted = new(1001, "ActivationStarted");

    /// <summary>State successfully restored from external store.</summary>
    public static readonly EventId StateRestoreSuccess = new(1002, "StateRestoreSuccess");

    /// <summary>State restoration failed.</summary>
    public static readonly EventId StateRestoreFailed = new(1003, "StateRestoreFailed");

    /// <summary>Catch-up from event store started.</summary>
    public static readonly EventId CatchUpStarted = new(1004, "CatchUpStarted");

    /// <summary>Catch-up from event store completed.</summary>
    public static readonly EventId CatchUpCompleted = new(1005, "CatchUpCompleted");

    /// <summary>State persistence completed.</summary>
    public static readonly EventId PersistenceCompleted = new(1006, "PersistenceCompleted");

    /// <summary>Grain activated in unhealthy state.</summary>
    public static readonly EventId UnhealthyActivation = new(1007, "UnhealthyActivation");

    /// <summary>Blob storage read failed.</summary>
    public static readonly EventId BlobReadFailed = new(1008, "BlobReadFailed");

    /// <summary>Grain deactivation started.</summary>
    public static readonly EventId DeactivationStarted = new(1009, "DeactivationStarted");

    /// <summary>Query rejected due to unhealthy state.</summary>
    public static readonly EventId QueryRejected = new(1010, "QueryRejected");

    /// <summary>External store not configured.</summary>
    public static readonly EventId NoExternalStore = new(1011, "NoExternalStore");

    /// <summary>No state found for projector version.</summary>
    public static readonly EventId StateNotFound = new(1012, "StateNotFound");
}
