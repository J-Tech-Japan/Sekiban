using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
namespace Sekiban.Dcb.Tests.Queries;

/// <summary>
///     Query handlers are invoked by reflection. A handler written against the exception-based core contract
///     (<see cref="ICoreMultiProjectionQuery{TMultiProjector,TQuery,TOutput}" />) that throws must reach the caller
///     as the exception it threw — not wrapped in a TargetInvocationException — and cancellation stays cancellation.
/// </summary>
public class ReflectedQueryHandlerExceptionTests
{
    private readonly ISekibanExecutor _executor;

    public ReflectedQueryHandlerExceptionTests()
    {
        var domainTypes = CreateDomainTypes();
        _executor = new InMemoryDcbExecutorForTesting(domainTypes, new InMemoryEventStore());
    }

    [Fact]
    public async Task Single_query_handler_failure_keeps_its_type()
    {
        var result = await _executor.QueryAsync(new ThrowingCountQuery(ThrowMode.Custom));

        Assert.False(result.IsSuccess);
        Assert.IsType<QueryHandlerFailure>(result.GetException());
    }

    [Fact]
    public async Task Single_query_handler_cancellation_stays_cancellation()
    {
        var result = await _executor.QueryAsync(new ThrowingCountQuery(ThrowMode.Cancel));

        Assert.False(result.IsSuccess);
        Assert.IsType<OperationCanceledException>(result.GetException());
    }

    [Theory]
    [InlineData(ThrowStage.Filter)]
    [InlineData(ThrowStage.Sort)]
    public async Task List_query_handler_failure_keeps_its_type(ThrowStage stage)
    {
        var result = await _executor.QueryAsync(new ThrowingListQuery(stage, ThrowMode.Custom));

        Assert.False(result.IsSuccess);
        Assert.IsType<QueryHandlerFailure>(result.GetException());
    }

    [Theory]
    [InlineData(ThrowStage.Filter)]
    [InlineData(ThrowStage.Sort)]
    public async Task List_query_handler_cancellation_stays_cancellation(ThrowStage stage)
    {
        var result = await _executor.QueryAsync(new ThrowingListQuery(stage, ThrowMode.Cancel));

        Assert.False(result.IsSuccess);
        Assert.IsType<OperationCanceledException>(result.GetException());
    }

    private static DcbDomainTypes CreateDomainTypes()
    {
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<ThrowingQueryProjector>();
        var queryTypes = new SimpleQueryTypes();
        queryTypes.RegisterQuery<ThrowingCountQuery>();
        queryTypes.RegisterListQuery<ThrowingListQuery>();
        return new DcbDomainTypes(
            new SimpleEventTypes(),
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            multiProjectorTypes,
            queryTypes,
            new JsonSerializerOptions());
    }

    private static Exception Failure(ThrowMode mode) =>
        mode == ThrowMode.Cancel ? new OperationCanceledException("handler cancelled") : new QueryHandlerFailure();

    public enum ThrowMode { Custom, Cancel }

    public enum ThrowStage { Filter, Sort }

    public sealed class QueryHandlerFailure() : Exception("query handler failed");

    public sealed record ThrowingQueryProjector : IMultiProjector<ThrowingQueryProjector>
    {
        public static string MultiProjectorName => "reflected-query-handler-exceptions";
        public static string MultiProjectorVersion => "1";
        public static ThrowingQueryProjector GenerateInitialPayload() => new();

        public static ResultBox<ThrowingQueryProjector> Project(
            ThrowingQueryProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    public sealed record ThrowingCountQuery(ThrowMode Mode)
        : ICoreMultiProjectionQuery<ThrowingQueryProjector, ThrowingCountQuery, int>
    {
        public static int HandleQuery(ThrowingQueryProjector projector, ThrowingCountQuery query, IQueryContext context) =>
            throw Failure(query.Mode);
    }

    public sealed record ThrowingListQuery(ThrowStage Stage, ThrowMode Mode, int? PageNumber = null, int? PageSize = null)
        : ICoreMultiProjectionListQuery<ThrowingQueryProjector, ThrowingListQuery, string>
    {
        public static IEnumerable<string> HandleFilter(
            ThrowingQueryProjector projector,
            ThrowingListQuery query,
            IQueryContext context) =>
            query.Stage == ThrowStage.Filter ? throw Failure(query.Mode) : ["a", "b"];

        public static IEnumerable<string> HandleSort(
            IEnumerable<string> filteredList,
            ThrowingListQuery query,
            IQueryContext context) =>
            query.Stage == ThrowStage.Sort ? throw Failure(query.Mode) : filteredList;
    }
}
