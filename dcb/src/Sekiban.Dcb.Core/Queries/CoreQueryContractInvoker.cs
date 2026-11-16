using Sekiban.Dcb.MultiProjections;
using System.Collections;
using System.Reflection;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Invokes a query through its exception-based core contract (<see cref="ICoreMultiProjectionQuery{TMultiProjector,TQuery,TOutput}" />
///     or <see cref="ICoreMultiProjectionListQuery{TMultiProjector,TQuery,TOutput}" />). A facade query that returns its
///     own result type (for example a WithResult box) implements the core contract by unwrapping that result once, so
///     the core only ever sees values and thrown exceptions. The delegates call the static members directly: an
///     exception from the query reaches the caller as itself, never wrapped in a reflection exception.
/// </summary>
internal static class CoreQueryContractInvoker
{
    internal delegate object? SingleHandler(object projector, object query, IQueryContext context);

    internal delegate IEnumerable FilterHandler(object projector, object query, IQueryContext context);

    internal delegate IEnumerable SortHandler(IEnumerable items, object query, IQueryContext context);

    /// <summary>Returns the core single-result handler, or <c>null</c> when the query does not implement the core contract.</summary>
    internal static SingleHandler? TryCreateSingle(Type queryType) =>
        FindCoreArguments(queryType, typeof(ICoreMultiProjectionQuery<,,>)) is { } arguments
            ? CreateHandler<SingleHandler>(nameof(HandleSingle), arguments)
            : null;

    /// <summary>Returns the core filter and sort handlers, or <c>null</c> when the query does not implement the core contract.</summary>
    internal static (FilterHandler Filter, SortHandler Sort)? TryCreateList(Type queryType) =>
        FindCoreArguments(queryType, typeof(ICoreMultiProjectionListQuery<,,>)) is { } arguments
            ? (CreateHandler<FilterHandler>(nameof(HandleFilter), arguments),
                CreateHandler<SortHandler>(nameof(HandleSort), arguments))
            : null;

    private static Type[]? FindCoreArguments(Type queryType, Type coreContract) =>
        queryType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == coreContract)
            ?.GetGenericArguments();

    private static THandler CreateHandler<THandler>(string methodName, Type[] arguments) where THandler : Delegate =>
        typeof(CoreQueryContractInvoker)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(arguments)
            .CreateDelegate<THandler>();

    private static object? HandleSingle<TMultiProjector, TQuery, TOutput>(
        object projector,
        object query,
        IQueryContext context)
        where TMultiProjector : ICoreMultiProjector<TMultiProjector>
        where TQuery : ICoreMultiProjectionQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull =>
        TQuery.HandleQuery((TMultiProjector)projector, (TQuery)query, context);

    private static IEnumerable HandleFilter<TMultiProjector, TQuery, TOutput>(
        object projector,
        object query,
        IQueryContext context)
        where TMultiProjector : ICoreMultiProjector<TMultiProjector>
        where TQuery : ICoreMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull =>
        TQuery.HandleFilter((TMultiProjector)projector, (TQuery)query, context);

    private static IEnumerable HandleSort<TMultiProjector, TQuery, TOutput>(
        IEnumerable items,
        object query,
        IQueryContext context)
        where TMultiProjector : ICoreMultiProjector<TMultiProjector>
        where TQuery : ICoreMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
        where TOutput : notnull =>
        TQuery.HandleSort(items as IEnumerable<TOutput> ?? items.Cast<TOutput>(), (TQuery)query, context);
}
