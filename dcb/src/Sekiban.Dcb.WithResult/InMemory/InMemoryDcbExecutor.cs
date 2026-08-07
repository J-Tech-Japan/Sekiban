using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Common;
using System.Text;
using Sekiban.Dcb.Capabilities;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation of <see cref="ISekibanExecutor"/> for Dcb.
///     Uses <see cref="InMemoryObjectAccessor"/> and a provided <see cref="IEventStore"/> (e.g. test InMemoryEventStore)
///     to execute commands, queries and tag state retrieval without external infrastructure.
///     Intended for lightweight tests / prototyping; not thread-safe for high concurrency scenarios.
/// </summary>
[Obsolete(
    "Moved to Sekiban.Dcb.WithResult.Testing (namespace Sekiban.Dcb.Testing). This type is volatile/in-process and is for tests only; it lives in a production package for historical reasons, which is how it reached production once. Behaviour is unchanged and it will not be removed before the next major version.")]
public class InMemoryDcbExecutor : ISekibanExecutor, ISerializedSekibanDcbExecutor, IExecutorRuntimeDescriptorProvider
{
    /// <summary>
    ///     In-process actors, this process only. A production host that resolves this executor is a production host
    ///     running a unit-test runtime, and the production guard will not start it.
    /// </summary>
    public ExecutorRuntimeDescriptor DescribeRuntime() =>
        new(ExecutorRuntimeKind.TestingInProcess, "InMemory (in-process actors)");

    private readonly GeneralSekibanExecutor _inner;
    private readonly InMemoryObjectAccessor _accessor;

    /// <summary>
    ///     Binary-compatible overload preserved for callers compiled against the pre-SEK-G23 constructor.
    /// </summary>
    public InMemoryDcbExecutor(DcbDomainTypes domainTypes, IEventStore eventStore)
        : this(domainTypes, eventStore, null)
    {
    }

    /// <summary>
    ///     Creates executor with provided in-memory event store implementation
    /// </summary>
    public InMemoryDcbExecutor(
        DcbDomainTypes domainTypes,
        IEventStore eventStore,
        IExecutedUserProvider? executedUserProvider = null)
    {
        if (domainTypes is null)
        {
            throw new ArgumentNullException(nameof(domainTypes));
        }
        if (eventStore is null)
        {
            throw new ArgumentNullException(nameof(eventStore));
        }
        (_accessor, _inner) = InMemoryExecutorFactory.Create(
            domainTypes,
            eventStore,
            executedUserProvider,
            (es, acc, dt, pub, prov) => new GeneralSekibanExecutor(es, acc, dt, pub, prov));
    }

    /// <summary>
    ///     Creates the executor with a built-in lightweight in-memory event store (no external dependencies).
    ///     Obsolete because it is silent about the most important thing it does. A downstream production system
    ///     registered this executor as its <c>ISekibanExecutor</c>; this constructor created a private volatile event
    ///     store for it, and every command succeeded, and no event ever reached the database it had configured. Pass
    ///     the store you mean to use — <c>new InMemoryDcbExecutor(domainTypes, new InMemoryEventStore())</c> in tests
    ///     — so that the store is a decision somebody made, and can be seen in the code that made it.
    /// </summary>
    [Obsolete(
        "Pass the event store explicitly: new InMemoryDcbExecutor(domainTypes, new InMemoryEventStore()). This "
        + "constructor silently creates a private VOLATILE event store, which is invisible at the call site and has "
        + "reached production before. Behaviour is unchanged; only the silence is deprecated.")]
    public InMemoryDcbExecutor(DcbDomainTypes domainTypes) : this(domainTypes, new InternalInMemoryEventStore(domainTypes)) { }

    Task<ResultBox<ExecutionResult>> ICommandExecutor.ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken) =>
        _inner.ExecuteAsync(command, handlerFunc, cancellationToken);

    Task<ResultBox<ExecutionResult>> ICommandExecutor.ExecuteCommandAsync(
        Func<ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken) =>
        _inner.ExecuteCommandAsync(handlerFunc, cancellationToken);

    Task<ResultBox<ExecutionResult>> ICommandExecutor.ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken) =>
        _inner.ExecuteAsync(command, cancellationToken);

    Task<ResultBox<TagState>> ISekibanExecutor.GetTagStateAsync(TagStateId tagStateId) =>
        _inner.GetTagStateAsync(tagStateId);

    Task<ResultBox<TResult>> ISekibanExecutor.QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) =>
        _inner.QueryAsync(queryCommon);

    Task<ResultBox<ListQueryResult<TResult>>> ISekibanExecutor.QueryAsync<TResult>(
        IListQueryCommon<TResult> queryCommon) =>
        _inner.QueryAsync(queryCommon);

    Task<ResultBox<string>> ISekibanExecutor.GetLatestSortableUniqueIdAsync() =>
        _inner.GetLatestSortableUniqueIdAsync();

    Task<ResultBox<ProjectionHeadStatus>> ISekibanExecutor.GetProjectionHeadStatusAsync(
        string projectorName,
        string? expectedProjectorVersion) =>
        _inner.GetProjectionHeadStatusAsync(projectorName, expectedProjectorVersion);

    Task<ResultBox<EventStoreHeadStatus>> ISekibanExecutor.GetEventStoreHeadStatusAsync(bool includeTotalEventCount) =>
        _inner.GetEventStoreHeadStatusAsync(includeTotalEventCount);

    Task<ResultBox<SerializableTagState>> ISerializedSekibanDcbExecutor.GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _inner.GetSerializableTagStateAsync(tagStateId);

    Task<ResultBox<SerializedCommitResult>> ISerializedSekibanDcbExecutor.CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken) =>
        _inner.CommitSerializableEventsAsync(request, cancellationToken);

}
