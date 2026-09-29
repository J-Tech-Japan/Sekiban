namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Marks a multi-projection payload whose <c>Project</c> implementation mutates and returns its input instance.
///     The dual-state wrapper uses snapshot serialization to isolate its safe state before projecting unsafe events.
///     Implementations must also return a fresh instance from <c>GenerateInitialPayload</c> on every call.
/// </summary>
public interface IMutatesProjectionInput;
