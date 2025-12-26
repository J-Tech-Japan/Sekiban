using ResultBoxes;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class TagStateSerializationTests
{
    private readonly ITagStatePayloadTypes _payloadTypes;

    public TagStateSerializationTests()
    {
        var simpleTypes = new SimpleTagStatePayloadTypes(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        // Register test payload types
        simpleTypes.RegisterPayloadType<TestPayload>();
        simpleTypes.RegisterPayloadType<ComplexTestPayload>();

        _payloadTypes = simpleTypes;
    }

    [Fact]
    public void SerializePayload_WithSimplePayload_ShouldSucceed()
    {
        // Arrange
        var payload = new TestPayload { Value = "test", Number = 42 };

        // Act
        var result = _payloadTypes.SerializePayload(payload);

        // Assert
        Assert.True(result.IsSuccess);
        var bytes = result.GetValue();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void DeserializePayload_WithSimplePayload_ShouldRecreateOriginal()
    {
        // Arrange
        var original = new TestPayload { Value = "test", Number = 42 };
        var serializeResult = _payloadTypes.SerializePayload(original);
        Assert.True(serializeResult.IsSuccess);
        var bytes = serializeResult.GetValue();

        // Act
        var deserializeResult = _payloadTypes.DeserializePayload(nameof(TestPayload), bytes);

        // Assert
        Assert.True(deserializeResult.IsSuccess);
        var deserialized = deserializeResult.GetValue() as TestPayload;
        Assert.NotNull(deserialized);
        Assert.Equal(original.Value, deserialized.Value);
        Assert.Equal(original.Number, deserialized.Number);
    }

    [Fact]
    public void SerializePayload_WithEmptyPayload_ShouldReturnEmptyBytes()
    {
        // Arrange
        var payload = new EmptyTagStatePayload();

        // Act
        var result = _payloadTypes.SerializePayload(payload);

        // Assert
        Assert.True(result.IsSuccess);
        var bytes = result.GetValue();
        Assert.Empty(bytes);
    }

    [Fact]
    public void DeserializePayload_WithEmptyPayload_ShouldSucceed()
    {
        // Arrange & Act
        var result = _payloadTypes.DeserializePayload(nameof(EmptyTagStatePayload), Array.Empty<byte>());

        // Assert
        Assert.True(result.IsSuccess);
        var payload = result.GetValue();
        Assert.IsType<EmptyTagStatePayload>(payload);
    }

    [Fact]
    public void SerializePayload_WithComplexPayload_ShouldSucceed()
    {
        // Arrange
        var payload = new ComplexTestPayload
        {
            Id = Guid.NewGuid(),
            Items = new List<string> { "item1", "item2", "item3" },
            Metadata = new Dictionary<string, object>
            {
                ["key1"] = "value1",
                ["key2"] = 123
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        var serializeResult = _payloadTypes.SerializePayload(payload);
        Assert.True(serializeResult.IsSuccess);
        var bytes = serializeResult.GetValue();

        var deserializeResult = _payloadTypes.DeserializePayload(nameof(ComplexTestPayload), bytes);

        // Assert
        Assert.True(deserializeResult.IsSuccess);
        var deserialized = deserializeResult.GetValue() as ComplexTestPayload;
        Assert.NotNull(deserialized);
        Assert.Equal(payload.Id, deserialized.Id);
        Assert.Equal(payload.Items, deserialized.Items);
        Assert.Equal(payload.CreatedAt, deserialized.CreatedAt);
    }

    [Fact]
    public void DeserializePayload_WithUnregisteredType_ShouldReturnError()
    {
        // Arrange
        var bytes = System.Text.Encoding.UTF8.GetBytes("{}");

        // Act
        var result = _payloadTypes.DeserializePayload("UnregisteredType", bytes);

        // Assert
        Assert.False(result.IsSuccess);
        var error = result.GetException();
        Assert.Contains("not registered", error.Message);
    }

    [Fact]
    public void DeserializePayload_WithInvalidJson_ShouldReturnError()
    {
        // Arrange
        var bytes = System.Text.Encoding.UTF8.GetBytes("invalid json");

        // Act
        var result = _payloadTypes.DeserializePayload(nameof(TestPayload), bytes);

        // Assert
        Assert.False(result.IsSuccess);
    }

    // Test payload types
    public record TestPayload : ITagStatePayload
    {
        public string Value { get; init; } = string.Empty;
        public int Number { get; init; }
    }

    public record ComplexTestPayload : ITagStatePayload
    {
        public Guid Id { get; init; }
        public List<string> Items { get; init; } = new();
        public Dictionary<string, object> Metadata { get; init; } = new();
        public DateTimeOffset CreatedAt { get; init; }
    }
}