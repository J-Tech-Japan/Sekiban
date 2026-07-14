# Sekiban.Dcb.WithResult.Testing

`InMemoryDcbExecutorForTesting` — the in-process executor for the `ResultBox` facade, in a package a runtime project
has no reason to reference.

It declares itself `TestingInProcess`, so the production guard (`AddSekibanDcbProductionGuard`) refuses to start a
Production host that resolved it — always, with no override, unlike volatile storage, which the operator can authorise
by name. That asymmetry is the intent: a volatile store in Production can be a decision; in-process actors are a
unit-test runtime, and never one.

```csharp
using Sekiban.Dcb.Testing;

var executor = new InMemoryDcbExecutorForTesting(domainTypes, new InMemoryEventStore(domainTypes.EventTypes));
```

**What the boundary buys, precisely.** `InMemoryDcbExecutorForTesting` cannot be reached without referencing this
package, so the executor a new test composes is one a runtime project cannot even name. It does **not** take the old one
away: `Sekiban.Dcb.InMemory.InMemoryDcbExecutor` stays public in the runtime package, still compiles, still behaves
identically, and is `[Obsolete]` rather than removed — it will not be removed before the next major version. For that
older path the backstop is the guard, not the compiler: both executors declare `TestingInProcess`, so
`AddSekibanDcbProductionGuard()` refuses to start a Production host that resolved either of them, however it was
obtained.

**Do not reference this package from a runtime project.** For local development that behaves like production, use a
single-silo localhost Orleans host — see
[the localhost Orleans composition guide](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/14_localhost_orleans.md).
