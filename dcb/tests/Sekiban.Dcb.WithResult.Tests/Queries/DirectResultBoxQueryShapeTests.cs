using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Tests.Queries;

/// <summary>
///     A query that implements the common query contract directly but returns a ResultBox from its handlers is no
///     longer supported (11.0). The query core reports it with an InvalidOperationException naming the query type and
///     the contracts to implement, on every execution, and other query types keep working.
/// </summary>
public class DirectResultBoxQueryShapeTests
{
    private readonly GeneralQueryExecutor _executor = new(new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task Single_query_returning_a_box_directly_fails_with_guidance_every_time()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _executor.ExecuteQueryAsync<ShapeProjector, DirectBoxCountQuery, int>(
                    new DirectBoxCountQuery(),
                    () => Task.FromResult(ShapeProjector.GenerateInitialPayload())));

            Assert.Contains(typeof(DirectBoxCountQuery).FullName!, failure.Message);
            Assert.Contains("HandleQuery returns ResultBox<Int32>", failure.Message);
            Assert.Contains("WithResult IMultiProjectionQuery", failure.Message);
            Assert.Contains("ICoreMultiProjectionQuery", failure.Message);
        }

        var supported = await _executor.ExecuteQueryAsync<ShapeProjector, WithResultCountQuery, int>(
            new WithResultCountQuery(),
            () => Task.FromResult(ShapeProjector.GenerateInitialPayload()));
        Assert.Equal(7, supported);
    }

    [Fact]
    public async Task List_query_returning_boxes_directly_fails_with_guidance_every_time()
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _executor.ExecuteListQueryAsync<ShapeProjector, DirectBoxListQuery, string>(
                    new DirectBoxListQuery(),
                    () => Task.FromResult(ShapeProjector.GenerateInitialPayload())));

            Assert.Contains(typeof(DirectBoxListQuery).FullName!, failure.Message);
            Assert.Contains("HandleFilter returns ResultBox<IEnumerable<String>>", failure.Message);
            Assert.Contains("WithResult IMultiProjectionListQuery", failure.Message);
            Assert.Contains("ICoreMultiProjectionListQuery", failure.Message);
        }

        var supported = await _executor.ExecuteListQueryAsync<ShapeProjector, WithResultListQuery, string>(
            new WithResultListQuery(),
            () => Task.FromResult(ShapeProjector.GenerateInitialPayload()));
        Assert.Equal(["a"], supported.Items);
    }

    public sealed record ShapeProjector : IMultiProjector<ShapeProjector>
    {
        public static string MultiProjectorName => "direct-result-box-query-shape";
        public static string MultiProjectorVersion => "1";
        public static ShapeProjector GenerateInitialPayload() => new();

        public static ResultBox<ShapeProjector> Project(
            ShapeProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);
    }

    public sealed record DirectBoxCountQuery : IQueryCommon<DirectBoxCountQuery, int>
    {
        public static ResultBox<int> HandleQuery(ShapeProjector projector, DirectBoxCountQuery query, IQueryContext context) =>
            ResultBox.FromValue(7);
    }

    public sealed record DirectBoxListQuery : IListQueryCommon<DirectBoxListQuery, string>
    {
        public static ResultBox<IEnumerable<string>> HandleFilter(
            ShapeProjector projector,
            DirectBoxListQuery query,
            IQueryContext context) =>
            ResultBox.FromValue<IEnumerable<string>>(["a"]);

        public static ResultBox<IEnumerable<string>> HandleSort(
            IEnumerable<string> filteredList,
            DirectBoxListQuery query,
            IQueryContext context) =>
            ResultBox.FromValue(filteredList);
    }

    public sealed record WithResultCountQuery : IMultiProjectionQuery<ShapeProjector, WithResultCountQuery, int>
    {
        public static ResultBox<int> HandleQuery(ShapeProjector projector, WithResultCountQuery query, IQueryContext context) =>
            ResultBox.FromValue(7);
    }

    public sealed record WithResultListQuery(int? PageNumber = null, int? PageSize = null) : IMultiProjectionListQuery<ShapeProjector, WithResultListQuery, string>
    {
        public static ResultBox<IEnumerable<string>> HandleFilter(
            ShapeProjector projector,
            WithResultListQuery query,
            IQueryContext context) =>
            ResultBox.FromValue<IEnumerable<string>>(["a"]);

        public static ResultBox<IEnumerable<string>> HandleSort(
            IEnumerable<string> filteredList,
            WithResultListQuery query,
            IQueryContext context) =>
            ResultBox.FromValue(filteredList);
    }
}
