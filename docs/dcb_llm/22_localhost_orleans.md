# Localhost Orleans, and where the in-memory stack belongs

## The taxonomy

There are three environments, and they are not on a sliding scale — each has one right answer.

| | executor | event store | how you get it |
|---|---|---|---|
| **Real environments** (production, staging that is really production) | distributed runtime (Orleans) | durable (Postgres / Cosmos / DynamoDB / Sqlite file) | your cluster, plus `AddSekibanDcbProductionGuard()` |
| **Local development** | distributed runtime (Orleans, one silo, on your machine) | your choice, said out loud — durable or volatile | `silo.UseSekibanDcbLocalhost()` |
| **Unit tests** | in-process (`InMemoryDcbExecutorForTesting`) | volatile (`InMemoryEventStore`) | the `Sekiban.Dcb.*.Testing` packages |

The middle row is the one that used to be missing, and its absence is why the bottom row leaked into the top one. If
the only cheap thing to run locally is an in-memory executor, people will run an in-memory executor — and one of them
will register it in a production host, where every command succeeds, no event ever reaches the database, and nothing
says a word. That happened. It is the reason for all of this.

So local development gets a real Orleans runtime, cheaply.

## The composition

```csharp
builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
```

One silo, `UseLocalhostClustering`, in-memory grain storage and streams, no external clustering dependency, nothing to
install. It is a **real** Orleans runtime: your grains are placed, your payloads are serialized, your projections run
through the same code path they will in production. It reports itself as `DistributedRuntime`, because it is one.

What it deliberately does **not** do is choose your event store. That line stays yours:

```csharp
// A realistic local environment: a real runtime over a real database.
builder.Services.AddSekibanDcbPostgres(builder.Configuration);

// A fast one that forgets everything when you stop it. Legitimate — but say so, and never in Production.
builder.Services.AddSingleton<IEventStore>(new InMemoryEventStore(domainTypes.EventTypes));
```

Both are legitimate. Neither should be implicit. `AddSekibanDcbStartupBanner()` will tell you, at every start, which
one you actually got.

### Web

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
builder.Services.AddSingleton(DomainType.GetDomainTypes());
builder.Services.AddSekibanDcbPostgres(builder.Configuration);
builder.Services.AddSingleton<ISekibanExecutor>(sp => new OrleansDcbExecutor(
    sp.GetRequiredService<IClusterClient>(),
    sp.GetRequiredService<IEventStore>(),
    sp.GetRequiredService<DcbDomainTypes>()));
builder.Services.AddSekibanDcbStartupBanner();

var app = builder.Build();
app.MapPost("/students", async (ISekibanExecutor executor, CreateStudent command) =>
    await executor.ExecuteAsync(command));
app.Run();
```

The silo starts with the web host and stops with it. This is what the Sekiban templates already do.

### Worker

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
// ...same domain / store / executor registration...
builder.Services.AddHostedService<ProjectionWorker>();
builder.Build().Run();
```

Your `BackgroundService` starts after the silo is up and can resolve `ISekibanExecutor` like any other service. On
shutdown the host stops your worker first and drains the silo after it, so work in flight is finished rather than
abandoned.

### CLI / batch — start, do one thing, exit

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.UseOrleans(silo => silo.UseSekibanDcbLocalhost());
// ...same domain / store / executor registration...

using var host = builder.Build();
await host.StartAsync();                       // the silo is up when this returns

var executor = host.Services.GetRequiredService<ISekibanExecutor>();
await executor.ExecuteAsync(new CreateStudent(id, name, 3));

await host.StopAsync();                        // graceful drain: returns when the silo is actually down
```

`StartAsync` / `StopAsync` are deterministic — when `StopAsync` returns, the silo has shut down, so a batch job can
exit knowing its writes are done rather than hoping.

**The trade-off you are buying, stated plainly.** A silo has a cold start. On a developer machine it is roughly a
second, and you pay it on every invocation of a short-lived process — a CLI that used to start instantly with an
in-memory executor will not feel instant any more. That is a real cost, and it is the cost of the CLI exercising the
same runtime as production instead of a different one that happens to be faster. If your CLI runs in a tight loop where
that second matters, run it against a silo you started once (point it at a long-running local host) rather than
starting a new one per invocation.

All three shapes are covered by tests in `dcb/tests/Sekiban.Dcb.Orleans.Tests/LocalhostCompositionTests.cs` — real
hosts, really started, really used, really stopped. If a shape here stops working, that file fails.

## The Testing packages

The volatile stack now lives in packages a runtime project has no reason to reference:

| package | what is in it |
|---|---|
| `Sekiban.Dcb.Core.Testing` | `InMemoryEventStore`, `InMemoryMultiProjectionStateStore`, `InMemoryObjectAccessor`, the in-process publishers/streams, `InMemoryBlobStorageSnapshotAccessor` — namespace `Sekiban.Dcb.Testing` |
| `Sekiban.Dcb.WithResult.Testing` | `InMemoryDcbExecutorForTesting` for the `ResultBox` facade |
| `Sekiban.Dcb.WithoutResult.Testing` | `InMemoryDcbExecutorForTesting` for the exception-based facade |

```csharp
using Sekiban.Dcb.Testing;

var eventStore = new InMemoryEventStore(domainTypes.EventTypes);
var executor = new InMemoryDcbExecutorForTesting(domainTypes, eventStore);
```

The two facades get their own packages on purpose. One "Sekiban.Dcb.Testing" holding both executors would be a package
whose meaning depends on which one you happened to import.

**A project that does not reference these packages cannot compile against these types.** That is the whole point:
naming did not prevent the incident, and a boundary the compiler enforces does.

## Migration

Nothing you have breaks. The old types still work, still behave identically, and are `[Obsolete]` rather than removed —
they will not be removed before the next major version.

Find your usages:

```bash
grep -rn "InMemoryDcbExecutor\|Sekiban\.Dcb\.InMemory" --include=*.cs .
```

Then, by shape:

| what you have | what it means | what to do |
|---|---|---|
| `new InMemoryDcbExecutor(domainTypes)` in a test | a private volatile store you never see | `new InMemoryDcbExecutorForTesting(domainTypes, new InMemoryEventStore(domainTypes.EventTypes))` |
| `new InMemoryDcbExecutor(domainTypes, volatileStore)` in a test | a unit test, correctly composed | rename the type, add the `Sekiban.Dcb.*.Testing` package, change `using Sekiban.Dcb.InMemory` to `using Sekiban.Dcb.Testing` |
| **`new InMemoryDcbExecutor(domainTypes, durableStore)` in anything that runs for real** | **the dangerous one** — a durable store makes the events survive, but commands still execute on in-process actors with no cluster coordination, so two hosts do not see each other's tag reservations | move to a **localhost silo** (this document) if it is local, or to a real cluster if it is not. There is no in-process executor that is safe for a real environment; that is why `AddSekibanDcbProductionGuard()` fails closed on it |
| `Sekiban.Dcb.InMemory.InMemoryEventStore` etc. in a test | volatile stores, correctly used | reference `Sekiban.Dcb.Core.Testing`, change the `using` |
| `InMemoryTagStatePersistent` | **not** a test double — the tag-state actor's real in-process cache | leave it alone. It stays in `Sekiban.Dcb.Core`, and it is not obsolete |

We migrated this repository first: every one of its own usages — tests and template content, 58 files — now goes
through the Testing packages, and the solution builds with zero obsolescence warnings. The migration we are asking you
to make is the one we made.

## What we cannot tell you

We can enumerate the two-argument usages **in this repository**. We cannot enumerate yours. If you are running
`InMemoryDcbExecutor` with a durable store in an environment that matters, the grep above will find it, and the row in
bold is what it means — but it is your grep, and nobody else can run it for you. The production guard exists precisely
because that class of thing cannot be found by reading names.

## See also

- [Storage providers and the production guard](11_storage_providers.md)
- [Unit testing](12_unit_testing.md)
- [Orleans setup](10_orleans_setup.md)
