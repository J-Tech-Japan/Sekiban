# Sekiban.Dcb.Core.Testing

The volatile in-memory stores and helpers Sekiban's own tests are built on, in a package a runtime project has no
reason to reference.

They used to be available only from `Sekiban.Dcb.Core`, in the `Sekiban.Dcb.InMemory` namespace, where nothing stopped
a production host from composing itself out of them — and something didn't. A production system registered the in-memory
executor as its `ISekibanExecutor`, every command succeeded, and no event ever reached the database it had configured.

**What this package changes, precisely.** These types now have a home a runtime project has no reason to reference: the
`Sekiban.Dcb.Testing` entry points require this package, so a project without it cannot reach them. What it does **not**
do is take the old ones away — `Sekiban.Dcb.InMemory` remains public in `Sekiban.Dcb.Core` for compatibility, and code
that already uses it still compiles and still runs. The protection against *that* path is the `[Obsolete]` pointer, and the opt-in
`AddSekibanDcbProductionGuard()`, whose two answers are not the same answer: **an in-process executor is rejected in Production, always** — there is no override for it, whichever name it was obtained under — while a **volatile store is rejected by default but can be authorised** by the named, storage-only `AllowVolatileStorageInProduction`. A store that will not say what it is (`Unknown`) stays fail-closed either way.
The compile-time boundary is a wall around the new door, not around the old one; the old one closes at the next major.

```csharp
using Sekiban.Dcb.Testing;

var eventStore = new InMemoryEventStore(domainTypes.EventTypes);
```

The old `Sekiban.Dcb.InMemory` types still work and still behave identically — they are `[Obsolete]`, not removed, and
they will not be removed before the next major version. Nothing you have breaks today.

**Do not reference this package from a runtime project.** If you want a local development environment that behaves like
production, the answer is a single-silo localhost Orleans host, not an in-memory executor — see
[the localhost Orleans composition guide](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/14_localhost_orleans.md).
