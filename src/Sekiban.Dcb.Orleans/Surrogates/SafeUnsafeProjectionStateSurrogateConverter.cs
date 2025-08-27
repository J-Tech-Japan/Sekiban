using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Orleans.Surrogates;

[RegisterConverter]
public sealed class SafeUnsafeProjectionStateSurrogateConverter<TKey, TState> :
    IConverter<SafeUnsafeProjectionState<TKey, TState>, SafeUnsafeProjectionStateSurrogate<TKey, TState>>
    where TKey : notnull where TState : class
{
    public SafeUnsafeProjectionState<TKey, TState> ConvertFromSurrogate(
        in SafeUnsafeProjectionStateSurrogate<TKey, TState> surrogate) =>
        // Create a new instance - state dictionaries will be reconstructed on demand
        new();

    public SafeUnsafeProjectionStateSurrogate<TKey, TState> ConvertToSurrogate(
        in SafeUnsafeProjectionState<TKey, TState> value) =>
        // Create empty surrogate - no persistent state needed
        // The internal dictionaries are private and will be reconstructed on demand
        new();
}
