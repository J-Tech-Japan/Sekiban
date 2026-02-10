using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Sekiban.Dcb.Tests;

public class DualStateProjectionWrapperCloneTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly SimpleMultiProjectorTypes _multiProjectorTypes;
    private readonly JsonSerializerOptions _camelCaseOptions;

    public DualStateProjectionWrapperCloneTests()
    {
        var eventTypes = new SimpleEventTypes();
        var tagTypes = new SimpleTagTypes();
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();

        _multiProjectorTypes = new SimpleMultiProjectorTypes();
        _multiProjectorTypes.RegisterProjector<CamelCaseProjector>();

        _camelCaseOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            _multiProjectorTypes,
            new SimpleQueryTypes(),
            _camelCaseOptions);
    }

    [Fact]
    public void Constructor_ShouldCloneWithCustomJsonOptions_WhenProjectorHasCustomConverter()
    {
        // Given: a projector with nested data that requires camelCase serialization
        var initialProjector = new CamelCaseProjector
        {
            Detail = new NestedDetail("TestItem", 42)
        };

        // When: constructing the wrapper with camelCase options
        var wrapper = new DualStateProjectionWrapper<CamelCaseProjector>(
            initialProjector,
            CamelCaseProjector.MultiProjectorName,
            _multiProjectorTypes,
            _camelCaseOptions);

        // Then: construction succeeds and both states contain correct data
        var unsafeProjection = wrapper.GetUnsafeProjection(_domainTypes);
        Assert.Equal("TestItem", unsafeProjection.State.Detail.Name);
        Assert.Equal(42, unsafeProjection.State.Detail.Value);
    }

    [Fact]
    public void Constructor_ShouldProduceIndependentClone_WhenUnsafeStateIsCloned()
    {
        // Given: a projector with data
        var initialProjector = new CamelCaseProjector
        {
            Detail = new NestedDetail("Original", 100)
        };

        // When: constructing the wrapper
        var wrapper = new DualStateProjectionWrapper<CamelCaseProjector>(
            initialProjector,
            CamelCaseProjector.MultiProjectorName,
            _multiProjectorTypes,
            _camelCaseOptions);

        // Then: safe and unsafe states have identical data
        var safeWindowThreshold = new SortableUniqueId(
            "000000000000000000000000000000000000000000000000");
        var safeProjection = wrapper.GetSafeProjection(safeWindowThreshold, _domainTypes);
        var unsafeProjection = wrapper.GetUnsafeProjection(_domainTypes);

        Assert.Equal(safeProjection.State.Detail.Name, unsafeProjection.State.Detail.Name);
        Assert.Equal(safeProjection.State.Detail.Value, unsafeProjection.State.Detail.Value);
    }

    [Fact]
    public void Constructor_ShouldCloneWithSnapshotRestore_WhenRestoredFromSnapshot()
    {
        // Given: a projector with nested data, simulating snapshot restore
        var restoredProjector = new CamelCaseProjector
        {
            Detail = new NestedDetail("Restored", 999)
        };

        // When: constructing the wrapper as if restored from snapshot
        var wrapper = new DualStateProjectionWrapper<CamelCaseProjector>(
            restoredProjector,
            CamelCaseProjector.MultiProjectorName,
            _multiProjectorTypes,
            _camelCaseOptions,
            initialVersion: 5,
            initialLastEventId: Guid.NewGuid(),
            initialLastSortableUniqueId: null,
            isRestoredFromSnapshot: true);

        // Then: construction succeeds and data is preserved
        var unsafeProjection = wrapper.GetUnsafeProjection(_domainTypes);
        Assert.Equal("Restored", unsafeProjection.State.Detail.Name);
        Assert.Equal(999, unsafeProjection.State.Detail.Value);
    }
}

/// <summary>
///     A nested value object that uses a custom JsonConverter requiring camelCase property names.
/// </summary>
[JsonConverter(typeof(NestedDetailConverter))]
public record NestedDetail(string Name, int Value);

/// <summary>
///     Custom JsonConverter that reads/writes properties using camelCase names.
///     With default JsonSerializerOptions (PascalCase), deserialization produces
///     null/zero values because the property names don't match.
/// </summary>
public class NestedDetailConverter : JsonConverter<NestedDetail>
{
    public override NestedDetail Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject");
        }

        string? name = null;
        int value = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "name":
                    name = reader.GetString();
                    break;
                case "value":
                    value = reader.GetInt32();
                    break;
            }
        }

        if (name is null)
        {
            throw new JsonException(
                "Property 'name' not found. " +
                "This converter requires camelCase JsonSerializerOptions.");
        }

        return new NestedDetail(name, value);
    }

    public override void Write(
        Utf8JsonWriter writer, NestedDetail detail, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("name", detail.Name);
        writer.WriteNumber("value", detail.Value);
        writer.WriteEndObject();
    }
}

/// <summary>
///     Projector whose payload contains a NestedDetail with a custom JsonConverter
///     that assumes camelCase property names. If serialized with default options
///     (PascalCase), the converter throws during deserialization.
/// </summary>
public record CamelCaseProjector : IMultiProjector<CamelCaseProjector>
{
    public NestedDetail Detail { get; init; } = new("", 0);

    public static string MultiProjectorName => "CamelCaseProjector";
    public static string MultiProjectorVersion => "1.0.0";

    public static CamelCaseProjector GenerateInitialPayload() =>
        new() { Detail = new NestedDetail("", 0) };

    public static ResultBox<CamelCaseProjector> Project(
        CamelCaseProjector payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        return ResultBox.FromValue(payload);
    }
}
