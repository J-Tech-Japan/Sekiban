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
///     A WithResult query handler that returns a null ResultBox fails with the messages the query core has always used
///     for a null handler result.
/// </summary>
public class NullResultBoxQueryMessageTests
{
    private readonly ISekibanExecutor _executor = new InMemoryDcbExecutorForTesting(CreateDomainTypes(), new InMemoryEventStore());

    [Fact]
    public async Task Single_query_null_box_reports_null_result()
    {
        var result = await _executor.QueryAsync(new NullBoxCountQuery());

        Assert.False(result.IsSuccess);
        var failure = Assert.IsType<InvalidOperationException>(result.GetException());
        Assert.Equal("HandleQuery returned null result", failure.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_query_null_box_reports_invalid_result_type(bool nullFilter)
    {
        var result = await _executor.QueryAsync(new NullBoxListQuery(nullFilter));

        Assert.False(result.IsSuccess);
        var failure = Assert.IsType<InvalidOperationException>(result.GetException());
        Assert.Equal("Query handler returned invalid result type", failure.Message);
    }

    private static DcbDomainTypes CreateDomainTypes()
    {
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<NullBoxProjector>();
        var queryTypes = new SimpleQueryTypes();
        queryTypes.RegisterQuery<NullBoxCountQuery>();
        queryTypes.RegisterListQuery<NullBoxListQuery>();
        return new DcbDomainTypes(
            new SimpleEventTypes(),
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            multiProjectorTypes,
            queryTypes,
            new JsonSerializerOptions());
    }

    public sealed record NullBoxProjector : IMultiProjector<NullBoxProjector>
    {
        public static string MultiProjectorName => "null-result-box-queries";
        public static string MultiProjectorVersion => "1";
        public static NullBoxProjector GenerateInitialPayload() => new();

        public static ResultBox<NullBoxProjector> Project(
            NullBoxProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    public sealed record NullBoxCountQuery : IMultiProjectionQuery<NullBoxProjector, NullBoxCountQuery, int>
    {
        public static ResultBox<int> HandleQuery(NullBoxProjector projector, NullBoxCountQuery query, IQueryContext context) =>
            null!;
    }

    public sealed record NullBoxListQuery(bool NullFilter, int? PageNumber = null, int? PageSize = null)
        : IMultiProjectionListQuery<NullBoxProjector, NullBoxListQuery, string>
    {
        public static ResultBox<IEnumerable<string>> HandleFilter(
            NullBoxProjector projector,
            NullBoxListQuery query,
            IQueryContext context) =>
            query.NullFilter ? null! : ResultBox.FromValue<IEnumerable<string>>(["a"]);

        public static ResultBox<IEnumerable<string>> HandleSort(
            IEnumerable<string> filteredList,
            NullBoxListQuery query,
            IQueryContext context) =>
            null!;
    }
}
