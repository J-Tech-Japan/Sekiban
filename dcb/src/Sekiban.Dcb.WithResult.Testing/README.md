# Sekiban.Dcb.WithResult.Testing

`InMemoryDcbExecutorForTesting` — the in-process executor for the `ResultBox` facade, in a package a runtime project
has no reason to reference.

It declares itself `TestingInProcess`, so the production guard (`AddSekibanDcbProductionGuard`) refuses to start a
Production host that resolved it. That is the intent: in-process actors are a unit-test runtime, not a small
production runtime.

```csharp
using Sekiban.Dcb.Testing;

var executor = new InMemoryDcbExecutorForTesting(domainTypes, new InMemoryEventStore(domainTypes.EventTypes));
```

The old `Sekiban.Dcb.InMemory.InMemoryDcbExecutor` still works and behaves identically — it is `[Obsolete]`, not
removed, and will not be removed before the next major version.

**Do not reference this package from a runtime project.** For local development that behaves like production, use a
single-silo localhost Orleans host — see
[the localhost Orleans composition guide](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/14_localhost_orleans.md).
