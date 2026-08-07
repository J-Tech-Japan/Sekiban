namespace Sekiban.Dcb;

/// <summary>
///     Provides the identity of the user executing a command. Register an implementation in DI to flow the real
///     executing user into <see cref="Sekiban.Dcb.Events.EventMetadata.ExecutedUser" /> for every event produced on
///     the command path. When no implementation is registered, or when it returns <c>null</c> or an empty string,
///     the default literal <c>"GeneralSekibanExecutor"</c> is preserved.
/// </summary>
/// <remarks>
///     Release note (dcb-v10.9.0): <c>IExecutedUserProvider</c> is an opt-in addition. When it is absent or returns
///     <c>null</c>/empty, the command path continues to write <c>"GeneralSekibanExecutor"</c>, so the default
///     behavior is unchanged. The serialized/WASM commit path remains pinned to <c>"SerializedSekibanExecutor"</c>.
/// </remarks>
public interface IExecutedUserProvider
{
    /// <summary>
    ///     Returns the identity of the user executing the current command. This is evaluated exactly once per
    ///     event-producing command execution and the value is reused for every event that command emits.
    /// </summary>
    /// <returns>The executed-user value, or <c>null</c>/empty to fall back to the default literal.</returns>
    string GetExecutedUser();
}
