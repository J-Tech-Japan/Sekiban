using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Storage;
namespace Sekiban.Dcb.Testing;

#pragma warning disable CS0618 // the base type is [Obsolete] on purpose, and this is where it points

/// <summary>
///     The in-process executor for unit tests, with a name that says so, in a package a runtime project has no reason
///     to reference.
///     The old name — <c>Sekiban.Dcb.InMemory.InMemoryDcbExecutor</c>, in the production package — read like a test
///     type too, and a production system registered it as its <c>ISekibanExecutor</c> anyway: every command succeeded,
///     and no event ever reached the database it had configured. Naming did not prevent that.
///     What the package boundary buys, exactly: <b>this</b> type cannot be reached without referencing
///     Sekiban.Dcb.WithResult.Testing, so the executor a new test composes is one a runtime project cannot even name. It
///     does not remove the old type — that stays public in the production package until the next major, and code using
///     it still compiles.
///     For the old path, and for this one, the backstop is the same: it declares itself
///     <see cref="ExecutorRuntimeKind.TestingInProcess" />, so a Production host that resolves it and has opted into
///     <c>AddSekibanDcbProductionGuard</c> refuses to start — however the executor was obtained.
///     Behaviour is identical to the type it replaces — this is a boundary, not a rewrite.
/// </summary>
public class InMemoryDcbExecutorForTesting : InMemoryDcbExecutor, IExecutorRuntimeDescriptorProvider
{
    /// <summary>
    ///     Creates the executor over the event store you pass. Pass the store you mean to use: in a unit test that is
    ///     <c>new InMemoryEventStore(domainTypes.EventTypes)</c>, and the point of passing it is that the choice is
    ///     visible in the code that made it.
    /// </summary>
    public InMemoryDcbExecutorForTesting(
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        IExecutedUserProvider? executedUserProvider = null)
        : base(domainTypes, eventStore, executedUserProvider)
    {
    }

    /// <summary>In-process actors, this process only — stated, so that the guard can act on it rather than guess.</summary>
    public new ExecutorRuntimeDescriptor DescribeRuntime() =>
        new(ExecutorRuntimeKind.TestingInProcess, "InMemory (in-process actors, testing package)");
}

#pragma warning restore CS0618
