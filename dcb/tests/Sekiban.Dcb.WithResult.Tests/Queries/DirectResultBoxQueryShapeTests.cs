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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Object_valued_single_query_returning_a_box_is_rejected_not_returned(bool failedBox)
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _executor.ExecuteQueryAsync<ShapeProjector, DirectBoxObjectQuery, object>(
                new DirectBoxObjectQuery(failedBox),
                () => Task.FromResult(ShapeProjector.GenerateInitialPayload())));

        Assert.Contains(typeof(DirectBoxObjectQuery).FullName!, failure.Message);
        Assert.Contains("HandleQuery returns ResultBox<Object>", failure.Message);
        Assert.Contains("WithResult IMultiProjectionQuery", failure.Message);
    }

    [Fact]
    public async Task Object_valued_list_query_returning_boxes_is_rejected_not_returned()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _executor.ExecuteListQueryAsync<ShapeProjector, DirectBoxObjectListQuery, object>(
                new DirectBoxObjectListQuery(),
                () => Task.FromResult(ShapeProjector.GenerateInitialPayload())));

        Assert.Contains(typeof(DirectBoxObjectListQuery).FullName!, failure.Message);
        Assert.Contains("HandleFilter returns ResultBox<IEnumerable<Object>>", failure.Message);
        Assert.Contains("WithResult IMultiProjectionListQuery", failure.Message);
    }

    [Fact]
    public async Task Object_valued_query_returning_a_value_type_fails_with_guidance()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _executor.ExecuteQueryAsync<ShapeProjector, ValueTypeObjectQuery, object>(
                new ValueTypeObjectQuery(),
                () => Task.FromResult(ShapeProjector.GenerateInitialPayload())));

        Assert.Contains("HandleQuery returns Int32, but Object is required", failure.Message);
    }

    [Fact]
    public async Task Mismatched_return_of_a_type_nested_in_a_generic_type_fails_with_guidance()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _executor.ExecuteQueryAsync<ShapeProjector, NestedReturnQuery, int>(
                new NestedReturnQuery(),
                () => Task.FromResult(ShapeProjector.GenerateInitialPayload())));

        Assert.Contains("HandleQuery returns Inner<String>, but Int32 is required", failure.Message);
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

    public sealed record DirectBoxObjectQuery(bool FailedBox) : IQueryCommon<DirectBoxObjectQuery, object>
    {
        public static ResultBox<object> HandleQuery(ShapeProjector projector, DirectBoxObjectQuery query, IQueryContext context) =>
            query.FailedBox
                ? ResultBox.Error<object>(new InvalidOperationException("box failure"))
                : ResultBox.FromValue<object>("value");
    }

    public sealed record DirectBoxObjectListQuery : IListQueryCommon<DirectBoxObjectListQuery, object>
    {
        public static ResultBox<IEnumerable<object>> HandleFilter(
            ShapeProjector projector,
            DirectBoxObjectListQuery query,
            IQueryContext context) =>
            ResultBox.FromValue<IEnumerable<object>>(["a"]);

        public static ResultBox<IEnumerable<object>> HandleSort(
            IEnumerable<object> filteredList,
            DirectBoxObjectListQuery query,
            IQueryContext context) =>
            ResultBox.FromValue(filteredList);
    }

    public sealed record ValueTypeObjectQuery : IQueryCommon<ValueTypeObjectQuery, object>
    {
        public static int HandleQuery(ShapeProjector projector, ValueTypeObjectQuery query, IQueryContext context) => 7;
    }

    public sealed record NestedReturnQuery : IQueryCommon<NestedReturnQuery, int>
    {
        public static Outer<string>.Inner HandleQuery(ShapeProjector projector, NestedReturnQuery query, IQueryContext context) =>
            new();
    }

    public sealed class Outer<T>
    {
        public sealed class Inner;
    }
}
