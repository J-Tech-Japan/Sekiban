using System;
using System.Collections.Generic;
using System.Linq;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class SerializableQuerySerializationTests
{
    private readonly DcbDomainTypes _domainTypes = CreateDomainTypes();

    [Fact]
    public async Task SerializableQueryParameter_RoundTrips_Query()
    {
        var query = new TestQuery("criteria");

        var serialized = await SerializableQueryParameter.CreateFromAsync(
            query,
            _domainTypes.JsonSerializerOptions);
        var deserializedBox = await serialized.ToQueryAsync(_domainTypes);

        Assert.True(deserializedBox.IsSuccess);
        var deserialized = Assert.IsType<TestQuery>(deserializedBox.GetValue());
        Assert.Equal(query, deserialized);
    }

    [Fact]
    public async Task SerializableQueryResult_RoundTrips_SingleValue()
    {
        var query = new TestQuery("criteria");
        var result = new TestResult("payload", "criteria");
        var general = new QueryResultGeneral(result, typeof(TestResult).FullName ?? string.Empty, query);

        var serialized = await SerializableQueryResult.CreateFromAsync(
            general,
            _domainTypes.JsonSerializerOptions);
        var roundTripBox = await serialized.ToQueryResultAsync(_domainTypes);

        Assert.True(roundTripBox.IsSuccess);
        var typedBox = roundTripBox.GetValue().ToTypedResult<TestResult>();
        Assert.True(typedBox.IsSuccess);
        Assert.Equal(result, typedBox.GetValue());
    }

    [Fact]
    public async Task SerializableListQueryResult_RoundTrips_ListValue()
    {
        var query = new TestListQuery(PageNumber: 1, PageSize: 10);
        var items = new List<object>
        {
            new TestRecord("alpha"),
            new TestRecord("beta")
        };
        var general = new ListQueryResultGeneral(
            items.Count,
            1,
            1,
            10,
            items,
            typeof(TestRecord).AssemblyQualifiedName ?? typeof(TestRecord).FullName ?? nameof(TestRecord),
            query);

        var serialized = await SerializableListQueryResult.CreateFromAsync(
            general,
            _domainTypes.JsonSerializerOptions);
        var roundTripBox = await serialized.ToListQueryResultAsync(_domainTypes);

        Assert.True(roundTripBox.IsSuccess);
        var typedBox = roundTripBox.GetValue().ToTypedResult<TestRecord>();
        Assert.True(typedBox.IsSuccess);
        Assert.Equal(items.Cast<TestRecord>(), typedBox.GetValue().Items);
    }

    private static DcbDomainTypes CreateDomainTypes() =>
        DcbDomainTypes.Simple(builder =>
        {
            builder.MultiProjectorTypes.RegisterProjector<TestProjector>();
            builder.QueryTypes.RegisterQuery<TestProjector, TestQuery, TestResult>();
            builder.QueryTypes.RegisterListQuery<TestProjector, TestListQuery, TestRecord>();
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

    public record TestProjector(string Value, IReadOnlyList<TestRecord> Items) : IMultiProjector<TestProjector>
    {
        public TestProjector() : this("initial", Array.Empty<TestRecord>()) { }

        public static string MultiProjectorName => "TestProjector";
        public static string MultiProjectorVersion => "1.0.0";

        public static ResultBox<TestProjector> Project(
            TestProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);

        public static TestProjector GenerateInitialPayload() =>
            new("initial", Array.Empty<TestRecord>());
    }

    public record TestQuery(string Criteria)
        : IMultiProjectionQuery<TestProjector, TestQuery, TestResult>
    {
        public static ResultBox<TestResult> HandleQuery(
            TestProjector projector,
            TestQuery query,
            IQueryContext context) =>
            ResultBox.FromValue(new TestResult(projector.Value, query.Criteria));
    }

    public record TestResult(string ProjectorValue, string Criteria);

    public record TestListQuery(int? PageNumber = 1, int? PageSize = 10)
        : IMultiProjectionListQuery<TestProjector, TestListQuery, TestRecord>
    {
        public static ResultBox<IEnumerable<TestRecord>> HandleFilter(
            TestProjector projector,
            TestListQuery query,
            IQueryContext context) =>
            ResultBox.FromValue<IEnumerable<TestRecord>>(projector.Items);

        public static ResultBox<IEnumerable<TestRecord>> HandleSort(
            IEnumerable<TestRecord> filteredList,
            TestListQuery query,
            IQueryContext context) =>
            ResultBox.FromValue<IEnumerable<TestRecord>>(filteredList);
    }

    public record TestRecord(string Name);
}
