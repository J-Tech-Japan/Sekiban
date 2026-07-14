# Sekiban.Dcb.Core.Testing

The volatile in-memory stores and helpers Sekiban's own tests are built on, in a package a runtime project has no
reason to reference.

They used to live in `Sekiban.Dcb.Core`, in the `Sekiban.Dcb.InMemory` namespace, where nothing stopped a production
host from composing itself out of them — and something didn't. A production system registered the in-memory executor
as its `ISekibanExecutor`, every command succeeded, and no event ever reached the database it had configured. Naming
cannot prevent that. A package boundary can: a project that does not reference this package **cannot compile** against
these types.

```csharp
using Sekiban.Dcb.Testing;

var eventStore = new InMemoryEventStore(domainTypes.EventTypes);
```

The old `Sekiban.Dcb.InMemory` types still work and still behave identically — they are `[Obsolete]`, not removed, and
they will not be removed before the next major version. Nothing you have breaks today.

**Do not reference this package from a runtime project.** If you want a local development environment that behaves like
production, the answer is a single-silo localhost Orleans host, not an in-memory executor — see
[the localhost Orleans composition guide](https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/dcb_llm/14_localhost_orleans.md).
