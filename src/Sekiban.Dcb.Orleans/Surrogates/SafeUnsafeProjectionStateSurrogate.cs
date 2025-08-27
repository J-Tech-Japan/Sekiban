namespace Sekiban.Dcb.Orleans.Surrogates;

[GenerateSerializer]
public record struct SafeUnsafeProjectionStateSurrogate<TKey, TState>() where TKey : notnull where TState : class;
