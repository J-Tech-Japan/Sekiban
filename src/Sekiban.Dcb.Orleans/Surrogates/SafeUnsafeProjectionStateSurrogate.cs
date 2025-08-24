namespace Sekiban.Dcb.Orleans.Surrogates;

[GenerateSerializer]
public record struct SafeUnsafeProjectionStateSurrogate<TKey, TState>(
    [property: Id(0)]
    string SafeWindowThreshold) where TKey : notnull where TState : class;
